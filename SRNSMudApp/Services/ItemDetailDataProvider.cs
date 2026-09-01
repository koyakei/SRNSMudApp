#region

using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;

#endregion

namespace SRNSMudApp.Services;

/// <summary>ItemDetail ページの表示データ。</summary>
public sealed record ItemDetailPageData(
    Item Item,
    IReadOnlyList<Tag> AllTags,
    IReadOnlyList<TagRelationToTag> AllTagRelationsToTags,
    IReadOnlyList<TagWeightLedger> Ledgers);

/// <summary>
///     ItemDetail コンポーネント用のデータアクセスを分離するインターフェース。
///     コンポーネントから DbContext への直接依存を断ち、単体テストでモック可能にする。
/// </summary>
public interface IItemDetailDataProvider
{
    /// <summary>アイテム詳細の表示データを取得する。アイテムが存在しない場合は null。</summary>
    Task<ItemDetailPageData?> GetItemDetailAsync(int itemId, CancellationToken cancellationToken = default);
}

/// <summary>
///     ItemDetail コンポーネント用データアクセスプロバイダーの実装。
/// </summary>
/// <param name="dbFactory">DbContext ファクトリ。</param>
public class ItemDetailDataProvider(IDbContextFactory<ApplicationDbContext> dbFactory)
    : IItemDetailDataProvider
{
    /// <inheritdoc />
    public async Task<ItemDetailPageData?> GetItemDetailAsync(int itemId, CancellationToken cancellationToken = default)
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync(cancellationToken);
        Item? item = await context.Items
            .Include(i => i.Owner)
            .Include(i => i.TagRelations)
            .ThenInclude(tr => tr.Tag)
            .ThenInclude(t => t.Owner)
            .Include(i => i.TagRelations)
            .ThenInclude(tr => tr.Tag)
            .ThenInclude(t => t.TargetTagRelations)
            .ThenInclude(ttr => ttr.Tag)
            .ThenInclude(t => t.Owner)
            .Include(i => i.AsRequestOf)
            .ThenInclude(r => r.Target)
            .ThenInclude(t => t.Item)
            .Include(i => i.AsRequestOf)
            .ThenInclude(r => r.RequestedTag)
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == itemId, cancellationToken);

        if (item is null)
        {
            return null;
        }

        List<TagWeightLedger> ledgers = await context.TagWeightLedgers
            .Include(l => l.Owner)
            .Include(l => l.TagRelation)
            .ThenInclude(tr => tr.Tag)
            .Where(l => l.ItemId == itemId || (l.TagRelation != null && l.TagRelation.ItemId == itemId))
            .OrderByDescending(l => l.CreatedDate)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        List<Tag> allTags = await context.Tags.AsNoTracking().ToListAsync(cancellationToken);
        List<TagRelationToTag> allTagRelationsToTags =
            await context.TagRelationToTags.Include(ttr => ttr.Tag).AsNoTracking().ToListAsync(cancellationToken);

        return new ItemDetailPageData(item, allTags, allTagRelationsToTags, ledgers);
    }
}