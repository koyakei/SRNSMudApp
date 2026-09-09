// Services/UserDataProvider.cs
#region

using System.Globalization;

using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;

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
    IReadOnlyList<ApplicationUser>? FollowerUsers = null);

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
}

public class UserDataProvider(IDbContextFactory<ApplicationDbContext> dbFactory) : IUserDataProvider
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));

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
            .Include(i => i.TagRelations)
            .ThenInclude(tr => tr.Tag)
            .ThenInclude(t => t.Owner)
            .AsNoTracking()
            .Where(i => i.OwnerId == userId)
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

        return new UserDetailPageData(
            user,
            userTags,
            userItems,
            isFollowing,
            followingCount,
            followersCount,
            followingUsers,
            followerUsers);
    }

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
        return await dbContext.Users.FindAsync(userId);
    }
}