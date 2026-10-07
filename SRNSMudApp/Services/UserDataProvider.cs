// Services/UserDataProvider.cs
#region

using System.Globalization;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Models;

using Item = SRNSMudApp.Data.Item;
using Tag = SRNSMudApp.Data.Tag;

#endregion

namespace SRNSMudApp.Services;

/// <summary>ユーザー詳細ページの表示データ。</summary>
public sealed record UserDetailPageData(
    ApplicationUser? User,
    IReadOnlyList<Tag> UserTags,
    IReadOnlyList<Item> UserItems,
    bool IsFollowing = false,
    int FollowingCount = 0,
    int FollowersCount = 0,
    IReadOnlyList<ApplicationUser>? FollowingUsers = null,
    IReadOnlyList<ApplicationUser>? FollowerUsers = null,
    IReadOnlyList<Tag>? ReactionTags = null,
    UserProfileDto? Profile = null)
{
    /// <summary>
    ///     公開プロファイル DTO を取得する（Profile が明示されていない場合は User から生成）。
    /// </summary>
    public UserProfileDto? UserProfile => Profile ?? UserProfileDto.FromEntity(User);
}


/// <summary>
///     ユーザー系コンポーネント用のデータアクセスを分離するインターフェース。
///     コンポーネントから DbContext への直接依存を断ち、単体テストでモック可能にする。
/// </summary>
public interface IUserDataProvider
{
    /// <summary>ユーザー詳細ページの表示データを取得する。</summary>
    Task<UserDetailPageData> GetUserDetailAsync(string userId);

    /// <summary>ユーザー詳細ページの表示データを取得する (閲覧者のフォロー状態を含む)。</summary>
    Task<UserDetailPageData> GetUserDetailAsync(string userId, string? currentUserId);

    /// <summary>ユーザーのフォロー状態を切り替え、切り替え後の状態（フォロー中なら true、解除なら false）を返す。</summary>
    Task<bool> ToggleFollowUserAsync(string currentUserId, string targetUserId);

    /// <summary>指定したユーザーをフォロー中かどうかを判定する。</summary>
    Task<bool> IsFollowingUserAsync(string currentUserId, string targetUserId);

    /// <summary>指定ユーザーがフォローしているユーザー数を取得する。</summary>
    Task<int> GetFollowingCountAsync(string userId);

    /// <summary>指定ユーザーのフォロワー数を取得する。</summary>
    Task<int> GetFollowersCountAsync(string userId);

    /// <summary>指定ユーザーがフォローしているユーザーの一覧を取得する。</summary>
    Task<List<ApplicationUser>> GetFollowingUsersAsync(string userId);

    /// <summary>指定ユーザーをフォローしているフォロワーの一覧を取得する。</summary>
    Task<List<ApplicationUser>> GetFollowerUsersAsync(string userId);

    /// <summary>ユーザー名の部分一致でユーザーを検索する (最大 10 件)。</summary>
    Task<List<ApplicationUser>> SearchUsersAsync(string? value, CancellationToken token = default);

    /// <summary>正規化ユーザー名の前方一致 (大文字) でユーザーを検索する (最大 50 件)。空の場合は先頭 50 件。</summary>
    Task<List<ApplicationUser>> SearchUsersByNormalizedNameAsync(string? value, CancellationToken token = default);

    /// <summary>全ユーザーを取得する。</summary>
    Task<List<ApplicationUser>> GetAllUsersAsync();

    Task<ApplicationUser?> FindUserByIdAsync(string userId);
    Task<List<ApplicationUser>> GetUsersByIdsAsync(IEnumerable<string> userIds);

    /// <summary>指定したユーザーが特定のロールに所属しているかを判定する。</summary>
    Task<bool> IsUserInRoleAsync(ApplicationUser user, string role);

    /// <summary>指定したユーザーの Admin ロールを更新する。</summary>
    Task<IdentityResult> UpdateUserAdminRoleAsync(string userId, bool isAdmin);

    /// <summary>指定したユーザーのBAN（利用停止）状態を設定する。</summary>
    Task<IdentityResult> SetUserBanStatusAsync(string userId, bool isBanned, string? reason = null);

    /// <summary>ユーザーのタグ提案類似度閾値設定を更新する。</summary>
    Task UpdateTagSuggestionThresholdsAsync(string userId, float strongThreshold, float candidateThreshold);

    /// <summary>ユーザーの内部リンク自動変換設定を更新する。</summary>
    Task UpdateLinkConversionSettingsAsync(string userId, bool isEnabled, float threshold);
}

public class UserDataProvider(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    UserManager<ApplicationUser>? userManager = null) : IUserDataProvider
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
    private readonly UserManager<ApplicationUser>? _userManager = userManager;

    public Task<UserDetailPageData> GetUserDetailAsync(string userId) => GetUserDetailAsync(userId, null);

    public async Task<UserDetailPageData> GetUserDetailAsync(string userId, string? currentUserId = null)
    {
        await using ApplicationDbContext dbContext = await _dbFactory.CreateDbContextAsync();
        ApplicationUser? user =
            await dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);

        if (user is null)
        {
            return new UserDetailPageData(null, [], []);
        }

        await EnsureReactionTagsAsync(dbContext, userId);

        List<Tag> userTags = await dbContext.Tags
            .Include(t => t.Owner)
            .Include(t => t.TargetTagRelations)
            .ThenInclude(tr => tr.Tag)
            .ThenInclude(t => t.Owner)
            .AsNoTracking()
            .Where(t => t.OwnerId == userId)
            .OrderByDescending(t => t.UpdatedDate)
            .ToListAsync();

        List<Item> userItems = await dbContext.Items
            .Include(i => i.Owner)
            .Include(i => i.TargetUserGroup)
            .Include(i => i.TagRelations)
            .ThenInclude(tr => tr.Tag)
            .ThenInclude(t => t.Owner)
            .AsNoTracking()
            .Where(i => i.OwnerId == userId)
            .WhereVisibleToUser(dbContext, currentUserId)
            .OrderByDescending(i => i.UpdatedDate)
            .ToListAsync();

        int followingCount = await dbContext.UserFollows
            .CountAsync(uf => uf.OwnerId == userId);

        int followersCount = await dbContext.UserFollows
            .CountAsync(uf => uf.FollowedUserId == userId);

        var isFollowing = false;
        if (!string.IsNullOrEmpty(currentUserId) && currentUserId != userId)
        {
            isFollowing = await dbContext.UserFollows
                .AnyAsync(uf => uf.OwnerId == currentUserId && uf.FollowedUserId == userId);
        }

        List<ApplicationUser> followingUsers = await dbContext.UserFollows
            .Where(uf => uf.OwnerId == userId)
            .OrderByDescending(uf => uf.CreatedDate)
            .Select(uf => uf.FollowedUser)
            .AsNoTracking()
            .ToListAsync();

        List<ApplicationUser> followerUsers = await dbContext.UserFollows
            .Where(uf => uf.FollowedUserId == userId)
            .OrderByDescending(uf => uf.CreatedDate)
            .Select(uf => uf.Owner)
            .AsNoTracking()
            .ToListAsync();

        List<Tag> reactionTags = userTags
            .Where(t => ReactionTagNames.IsReactionTagName(t.Name))
            .OrderBy(t => GetReactionOrder(t.Name))
            .ToList();

        return new UserDetailPageData(
            user,
            userTags,
            userItems,
            isFollowing,
            followingCount,
            followersCount,
            followingUsers,
            followerUsers,
            reactionTags);
    }

    private static async Task EnsureReactionTagsAsync(ApplicationDbContext dbContext, string userId)
    {
        string[] reactionNames = [.. ReactionTagNames.All];
        var existingNames = await dbContext.Tags
            .Where(t => t.OwnerId == userId && t.IsSystem && reactionNames.Contains(t.Name))
            .Select(t => t.Name)
            .ToListAsync();

        if (existingNames.Count == reactionNames.Length)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var created = false;
        foreach (var name in reactionNames)
        {
            if (!existingNames.Contains(name))
            {
                dbContext.Tags.Add(new Tag
                {
                    Name = name,
                    IsSystem = true,
                    OwnerId = userId,
                    CreatedDate = now,
                    UpdatedDate = now
                });
                created = true;
            }
        }

        if (created)
        {
            try
            {
                await dbContext.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // 並行作成時のユニーク制約競合は許容
            }
        }
    }

    private static int GetReactionOrder(string name) => name switch
    {
        ReactionTagNames.Shinji => 0,
        ReactionTagNames.Zen => 1,
        ReactionTagNames.Bi => 2,
        _ => int.MaxValue
    };

    public async Task<bool> ToggleFollowUserAsync(string currentUserId, string targetUserId)
    {
        if (string.IsNullOrEmpty(currentUserId) || string.IsNullOrEmpty(targetUserId) || currentUserId == targetUserId)
        {
            return false;
        }

        await using ApplicationDbContext dbContext = await _dbFactory.CreateDbContextAsync();

        bool targetUserExists = await dbContext.Users.AnyAsync(u => u.Id == targetUserId);
        if (!targetUserExists)
        {
            return false;
        }

        UserFollow? existing = await dbContext.UserFollows
            .FirstOrDefaultAsync(uf => uf.OwnerId == currentUserId && uf.FollowedUserId == targetUserId);

        switch (existing)
        {
            case not null:
                _ = dbContext.UserFollows.Remove(existing);
                _ = await dbContext.SaveChangesAsync();
                return false;
            default:
                {
                    var newFollow = new UserFollow
                    {
                        OwnerId = currentUserId,
                        FollowedUserId = targetUserId,
                        CreatedDate = DateTime.UtcNow,
                        UpdatedDate = DateTime.UtcNow
                    };
                    _ = dbContext.UserFollows.Add(newFollow);
                    _ = await dbContext.SaveChangesAsync();
                    return true;
                }
        }
    }

    public async Task<bool> IsFollowingUserAsync(string currentUserId, string targetUserId)
    {
        if (string.IsNullOrEmpty(currentUserId) || string.IsNullOrEmpty(targetUserId))
        {
            return false;
        }

        await using ApplicationDbContext dbContext = await _dbFactory.CreateDbContextAsync();
        return await dbContext.UserFollows
            .AnyAsync(uf => uf.OwnerId == currentUserId && uf.FollowedUserId == targetUserId);
    }

    public async Task<int> GetFollowingCountAsync(string userId)
    {
        await using ApplicationDbContext dbContext = await _dbFactory.CreateDbContextAsync();
        return await dbContext.UserFollows.CountAsync(uf => uf.OwnerId == userId);
    }

    public async Task<int> GetFollowersCountAsync(string userId)
    {
        await using ApplicationDbContext dbContext = await _dbFactory.CreateDbContextAsync();
        return await dbContext.UserFollows.CountAsync(uf => uf.FollowedUserId == userId);
    }

    public async Task<List<ApplicationUser>> GetFollowingUsersAsync(string userId)
    {
        await using ApplicationDbContext dbContext = await _dbFactory.CreateDbContextAsync();
        return await dbContext.UserFollows
            .Where(uf => uf.OwnerId == userId)
            .OrderByDescending(uf => uf.CreatedDate)
            .Select(uf => uf.FollowedUser)
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<List<ApplicationUser>> GetFollowerUsersAsync(string userId)
    {
        await using ApplicationDbContext dbContext = await _dbFactory.CreateDbContextAsync();
        return await dbContext.UserFollows
            .Where(uf => uf.FollowedUserId == userId)
            .OrderByDescending(uf => uf.CreatedDate)
            .Select(uf => uf.Owner)
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<List<ApplicationUser>> SearchUsersAsync(string? value, CancellationToken token = default)
    {
        await using ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync(token);
        IQueryable<ApplicationUser> query = dbContext.Users.AsQueryable();
        query = string.IsNullOrEmpty(value) switch
        {
            false => query.Where(u => u.UserName!.Contains(value)),
            true => query
        };
        return await query.Take(10).ToListAsync(token);
    }

    public async Task<List<ApplicationUser>> SearchUsersByNormalizedNameAsync(
        string? value,
        CancellationToken token = default)
    {
        await using ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync(token);
        IQueryable<ApplicationUser> query = dbContext.Users.AsQueryable();

        return await (string.IsNullOrEmpty(value) switch
        {
            true => query.AsNoTracking().Take(50).ToListAsync(token),
            false => dbContext.Users
#pragma warning disable CA1862
                .Where(x => x.NormalizedUserName != null && x.NormalizedUserName.Contains(value.ToUpper(CultureInfo.InvariantCulture)))
#pragma warning restore CA1862
                .AsNoTracking()
                .Take(50)
                .ToListAsync(token)
        });
    }

    public async Task<List<ApplicationUser>> GetAllUsersAsync()
    {
        await using ApplicationDbContext dbContext = await _dbFactory.CreateDbContextAsync();
        return await dbContext.Users.AsNoTracking().ToListAsync();
    }
    public async Task<ApplicationUser?> FindUserByIdAsync(string userId)
    {
        await using ApplicationDbContext dbContext = await _dbFactory.CreateDbContextAsync();
        return await dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
    }

    public async Task<List<ApplicationUser>> GetUsersByIdsAsync(IEnumerable<string> userIds)
    {
        await using ApplicationDbContext dbContext = await _dbFactory.CreateDbContextAsync();
        var idList = userIds.ToList();
        return await dbContext.Users.AsNoTracking().Where(u => idList.Contains(u.Id)).ToListAsync();
    }

    public async Task<bool> IsUserInRoleAsync(ApplicationUser user, string role)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (_userManager is null)
        {
            return false;
        }

        return await _userManager.IsInRoleAsync(user, role);
    }

    public async Task<IdentityResult> UpdateUserAdminRoleAsync(string userId, bool isAdmin)
    {
        if (_userManager is null)
        {
            throw new InvalidOperationException("UserManager is not configured.");
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return IdentityResult.Failed(new IdentityError { Description = "ユーザーが見つかりません。" });
        }

        return isAdmin
            ? await _userManager.AddToRoleAsync(user, "Admin")
            : await _userManager.RemoveFromRoleAsync(user, "Admin");
    }

    public async Task<IdentityResult> SetUserBanStatusAsync(string userId, bool isBanned, string? reason = null)
    {
        if (_userManager is null)
        {
            throw new InvalidOperationException("UserManager is not configured.");
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return IdentityResult.Failed(new IdentityError { Description = "ユーザーが見つかりません。" });
        }

        if (string.Equals(user.UserName, "system", StringComparison.OrdinalIgnoreCase))
        {
            return IdentityResult.Failed(new IdentityError { Description = "システムユーザーをBANすることはできません。" });
        }

        user.IsBanned = isBanned;
        user.BannedAt = isBanned ? DateTimeOffset.UtcNow : null;
        user.BanReason = isBanned ? reason : null;

        if (isBanned)
        {
            user.LockoutEnabled = true;
            user.LockoutEnd = DateTimeOffset.MaxValue;
        }
        else
        {
            user.LockoutEnd = null;
        }

        _ = await _userManager.UpdateSecurityStampAsync(user);
        return await _userManager.UpdateAsync(user);
    }

    public async Task UpdateTagSuggestionThresholdsAsync(string userId, float strongThreshold, float candidateThreshold)
    {
        await using ApplicationDbContext dbContext = await _dbFactory.CreateDbContextAsync();
        ApplicationUser? user = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is not null)
        {
            user.TagSuggestionStrongThreshold = strongThreshold;
            user.TagSuggestionCandidateThreshold = candidateThreshold;
            _ = await dbContext.SaveChangesAsync();
        }
    }

    public async Task UpdateLinkConversionSettingsAsync(string userId, bool isEnabled, float threshold)
    {
        await using ApplicationDbContext dbContext = await _dbFactory.CreateDbContextAsync();
        ApplicationUser? user = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is not null)
        {
            user.IsLinkConversionEnabled = isEnabled;
            user.LinkConversionThreshold = threshold;
            _ = await dbContext.SaveChangesAsync();
        }
    }
}