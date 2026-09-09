using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;

namespace SRNSMudApp.Services;

/// <summary>
///     ユーザーグループ関連のデータアクセスおよびビジネスロジックを提供するインターフェース。
/// </summary>
public interface IUserGroupDataProvider
{
    /// <summary>指定ユーザーがオーナーまたはメンバーであるグループ一覧を取得する。</summary>
    Task<IReadOnlyList<UserGroup>> GetUserGroupsForUserAsync(string userId, CancellationToken token = default);

    /// <summary>指定ユーザーがオーナー（管理者）であるグループ一覧を取得する。</summary>
    Task<IReadOnlyList<UserGroup>> GetManagedGroupsAsync(string userId, CancellationToken token = default);

    /// <summary>指定グループIDのグループ（オーナー・メンバー含む）を取得する。</summary>
    Task<UserGroup?> GetUserGroupByIdAsync(int groupId, CancellationToken token = default);

    /// <summary>新規ユーザーグループを作成し、作成者をメンバーに追加する。</summary>
    Task<UserGroup> CreateUserGroupAsync(string name, string? description, string ownerUserId, CancellationToken token = default);

    /// <summary>ユーザーグループの基本情報（名前、説明）を更新する。オーナーのみ実行可能。</summary>
    Task<bool> UpdateUserGroupAsync(int groupId, string name, string? description, string currentUserId, CancellationToken token = default);

    /// <summary>ユーザーグループを削除する。オーナーのみ実行可能。</summary>
    Task<bool> DeleteUserGroupAsync(int groupId, string currentUserId, CancellationToken token = default);

    /// <summary>グループにメンバーを追加する。オーナーのみ実行可能。</summary>
    Task<bool> AddMemberAsync(int groupId, string userId, string currentUserId, CancellationToken token = default);

    /// <summary>グループからメンバーを削除する。オーナーまたはメンバー本人のみ実行可能。</summary>
    Task<bool> RemoveMemberAsync(int groupId, string userId, string currentUserId, CancellationToken token = default);
}

/// <summary>
///     ユーザーグループのデータアクセスプロバイダー実装。
/// </summary>
public class UserGroupDataProvider(IDbContextFactory<ApplicationDbContext> dbFactory) : IUserGroupDataProvider
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));

    public async Task<IReadOnlyList<UserGroup>> GetUserGroupsForUserAsync(string userId, CancellationToken token = default)
    {
        await using ApplicationDbContext dbContext = await _dbFactory.CreateDbContextAsync(token);
        return await dbContext.UserGroups
            .AsNoTracking()
            .Include(g => g.Owner)
            .Include(g => g.Members)
                .ThenInclude(m => m.User)
            .Where(g => g.OwnerId == userId || g.Members.Any(m => m.UserId == userId))
            .OrderByDescending(g => g.UpdatedDate)
            .ToListAsync(token);
    }

    public async Task<IReadOnlyList<UserGroup>> GetManagedGroupsAsync(string userId, CancellationToken token = default)
    {
        await using ApplicationDbContext dbContext = await _dbFactory.CreateDbContextAsync(token);
        return await dbContext.UserGroups
            .AsNoTracking()
            .Include(g => g.Owner)
            .Include(g => g.Members)
                .ThenInclude(m => m.User)
            .Where(g => g.OwnerId == userId)
            .OrderBy(g => g.Name)
            .ToListAsync(token);
    }

    public async Task<UserGroup?> GetUserGroupByIdAsync(int groupId, CancellationToken token = default)
    {
        await using ApplicationDbContext dbContext = await _dbFactory.CreateDbContextAsync(token);
        return await dbContext.UserGroups
            .AsNoTracking()
            .Include(g => g.Owner)
            .Include(g => g.Members)
                .ThenInclude(m => m.User)
            .FirstOrDefaultAsync(g => g.Id == groupId, token);
    }

    public async Task<UserGroup> CreateUserGroupAsync(string name, string? description, string ownerUserId, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerUserId);

        await using ApplicationDbContext dbContext = await _dbFactory.CreateDbContextAsync(token);

        var group = new UserGroup
        {
            Name = name.Trim(),
            Description = description?.Trim() ?? string.Empty,
            OwnerId = ownerUserId,
            CreatedDate = DateTime.UtcNow,
            UpdatedDate = DateTime.UtcNow
        };

        // オーナー自身を初期メンバーとして登録
        var member = new UserGroupMember
        {
            UserGroup = group,
            UserId = ownerUserId,
            OwnerId = ownerUserId,
            CreatedDate = DateTime.UtcNow,
            UpdatedDate = DateTime.UtcNow
        };

        group.Members.Add(member);
        dbContext.UserGroups.Add(group);
        await dbContext.SaveChangesAsync(token);

        return group;
    }

    public async Task<bool> UpdateUserGroupAsync(int groupId, string name, string? description, string currentUserId, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        await using ApplicationDbContext dbContext = await _dbFactory.CreateDbContextAsync(token);
        UserGroup? group = await dbContext.UserGroups.FirstOrDefaultAsync(g => g.Id == groupId, token);
        if (group is null || group.OwnerId != currentUserId)
        {
            return false;
        }

        group.Name = name.Trim();
        group.Description = description?.Trim() ?? string.Empty;
        group.UpdatedDate = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(token);
        return true;
    }

    public async Task<bool> DeleteUserGroupAsync(int groupId, string currentUserId, CancellationToken token = default)
    {
        await using ApplicationDbContext dbContext = await _dbFactory.CreateDbContextAsync(token);
        UserGroup? group = await dbContext.UserGroups.FirstOrDefaultAsync(g => g.Id == groupId, token);
        if (group is null || group.OwnerId != currentUserId)
        {
            return false;
        }

        dbContext.UserGroups.Remove(group);
        await dbContext.SaveChangesAsync(token);
        return true;
    }

    public async Task<bool> AddMemberAsync(int groupId, string userId, string currentUserId, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        await using ApplicationDbContext dbContext = await _dbFactory.CreateDbContextAsync(token);
        UserGroup? group = await dbContext.UserGroups
            .Include(g => g.Members)
            .FirstOrDefaultAsync(g => g.Id == groupId, token);

        if (group is null || group.OwnerId != currentUserId)
        {
            return false;
        }

        // 既にメンバーならスキップして成功扱い
        if (group.Members.Any(m => m.UserId == userId))
        {
            return true;
        }

        // ユーザーが存在するか確認
        bool userExists = await dbContext.Users.AnyAsync(u => u.Id == userId, token);
        if (!userExists)
        {
            return false;
        }

        var member = new UserGroupMember
        {
            UserGroupId = groupId,
            UserId = userId,
            OwnerId = currentUserId,
            CreatedDate = DateTime.UtcNow,
            UpdatedDate = DateTime.UtcNow
        };

        dbContext.UserGroupMembers.Add(member);
        group.UpdatedDate = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(token);
        return true;
    }

    public async Task<bool> RemoveMemberAsync(int groupId, string userId, string currentUserId, CancellationToken token = default)
    {
        await using ApplicationDbContext dbContext = await _dbFactory.CreateDbContextAsync(token);
        UserGroup? group = await dbContext.UserGroups.FirstOrDefaultAsync(g => g.Id == groupId, token);
        if (group is null)
        {
            return false;
        }

        // オーナーか、またはメンバー本人による脱退のみ許可
        if (group.OwnerId != currentUserId && userId != currentUserId)
        {
            return false;
        }

        // オーナー自身をグループから削除することは不可
        if (userId == group.OwnerId)
        {
            return false;
        }

        UserGroupMember? member = await dbContext.UserGroupMembers
            .FirstOrDefaultAsync(m => m.UserGroupId == groupId && m.UserId == userId, token);

        if (member is null)
        {
            return false;
        }

        dbContext.UserGroupMembers.Remove(member);
        group.UpdatedDate = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(token);
        return true;
    }
}