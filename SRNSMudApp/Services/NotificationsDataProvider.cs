#region

using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;

#endregion

namespace SRNSMudApp.Services;

/// <summary>
///     NotificationsPage コンポーネント用のデータアクセスを分離するインターフェース。
///     コンポーネントから DbContext への直接依存を断ち、単体テストでモック可能にする。
/// </summary>
public interface INotificationsDataProvider
{
    /// <summary>通知に関連付けられたアイテムを関連データ込みで取得する。</summary>
    Task<List<Item>> GetAssociatedItemsAsync(IReadOnlyList<int> itemIds, CancellationToken cancellationToken = default);
}

/// <summary>
///     NotificationsPage コンポーネント用データアクセスプロバイダーの実装。
/// </summary>
/// <param name="dbFactory">DbContext ファクトリ。</param>
public class NotificationsDataProvider(IDbContextFactory<ApplicationDbContext> dbFactory)
    : INotificationsDataProvider
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));

    /// <inheritdoc />
    public async Task<List<Item>> GetAssociatedItemsAsync(IReadOnlyList<int> itemIds, CancellationToken cancellationToken = default)
    {
        switch (itemIds.Count)
        {
            case 0:
                return [];
            default:
                break;
        }

        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync(cancellationToken);
        return await context.Items
            .AsNoTracking()
            .Include(i => i.Owner)
            .Include(i => i.TagRelations)
            .ThenInclude(tr => tr.Tag)
            .ThenInclude(t => t.Owner)
            .Include(i => i.AsRequestOf)
            .ThenInclude(r => r.Target)
            .ThenInclude(t => t.Item)
            .Include(i => i.AsRequestOf)
            .ThenInclude(r => r.RequestedTag)
            .Where(i => itemIds.Contains(i.Id))
            .ToListAsync(cancellationToken);
    }
}