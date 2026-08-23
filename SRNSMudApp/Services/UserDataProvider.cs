#region

using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;

using Item = SRNSMudApp.Data.Item;
using Tag = SRNSMudApp.Data.Tag;

#endregion

namespace SRNSMudApp.Services;

/// <summary>ユーザー詳細ページの表示データ。</summary>
public sealed record UserDetailPageData(
    ApplicationUser? User,
    List<Tag> UserTags,
    List<Item> UserItems);

/// <summary>
///     ユーザー系コンポーネント用のデータアクセスを分離するインターフェース。
///     コンポーネントから DbContext への直接依存を断ち、単体テストでモック可能にする。
/// </summary>
public interface IUserDataProvider
{
    Task<UserDetailPageData> GetUserDetailAsync(string userId);

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
    public async Task<UserDetailPageData> GetUserDetailAsync(string userId)
    {
        await using ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync();
        ApplicationUser? user =
            await dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);

        switch (user)
        {
            case null:
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

        return new UserDetailPageData(user, userTags, userItems);
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
        var query = dbContext.Users.AsQueryable();

        return await (string.IsNullOrEmpty(value) switch
        {
            true => query.AsNoTracking().Take(50).ToListAsync(token),
            false => dbContext.Users
                .Where(x => x.NormalizedUserName != null && x.NormalizedUserName.Contains(value.ToUpper()))
                .AsNoTracking()
                .Take(50)
                .ToListAsync(token)
        });
    }

    public async Task<List<ApplicationUser>> GetAllUsersAsync()
    {
        await using ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync();
        return await dbContext.Users.AsNoTracking().ToListAsync();
    }
    public async Task<ApplicationUser?> FindUserByIdAsync(string userId)
    {
        await using ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync();
        return await dbContext.Users.FindAsync(userId);
    }
}
