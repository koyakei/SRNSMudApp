using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;

namespace SRNSMudApp.Services;

/// <summary>ItemDetail ページの表示データ。</summary>
public sealed record ItemDetailPageData(
    Item Item,
    IReadOnlyList<Tag> AllTags,
    IReadOnlyList<TagRelationToTag> AllTagRelationsToTags,
    IReadOnlyList<TagWeightLedger> Ledgers,
    IReadOnlyList<Item>? Ancestors = null,
    IReadOnlyList<Item>? Replies = null,
    IReadOnlyList<Item>? Siblings = null,
    IReadOnlyList<Item>? Quotes = null)
{
    public IReadOnlyList<Item> Ancestors { get; init; } = Ancestors ?? [];
    public IReadOnlyList<Item> Replies { get; init; } = Replies ?? [];
    public IReadOnlyList<Item> Siblings { get; init; } = Siblings ?? [];
    public IReadOnlyList<Item> Quotes { get; init; } = Quotes ?? [];
}

/// <summary>
///     ItemDetail コンポーネント用のデータアクセスを分離するインターフェース。
///     コンポーネントから DbContext への直接依存を断ち、単体テストでモック可能にする。
/// </summary>
public interface IItemDetailDataProvider
{
    /// <summary>アイテム詳細の表示データを取得する。アイテムが存在しない場合は null。</summary>
    Task<ItemDetailPageData?> GetItemDetailAsync(int itemId, CancellationToken cancellationToken = default);

    /// <summary>閲覧ユーザーの可視性を考慮してアイテム詳細の表示データを取得する。閲覧権限がない場合は null。</summary>
    /// <param name="itemId">アイテムID。</param>
    /// <param name="currentUserId">閲覧ユーザーID。未ログイン時は null。</param>
    /// <param name="cancellationToken">キャンセレーショントークン。</param>
    /// <returns>アイテム詳細データ。閲覧権限がない場合または存在しない場合は null。</returns>
    Task<ItemDetailPageData?> GetItemDetailAsync(int itemId, string? currentUserId, CancellationToken cancellationToken = default);

    /// <summary>閲覧ユーザーの可視性および管理者権限を考慮してアイテム詳細の表示データを取得する。閲覧権限がない場合は null。</summary>
    /// <param name="itemId">アイテムID。</param>
    /// <param name="currentUserId">閲覧ユーザーID。未ログイン時は null。</param>
    /// <param name="isAdmin">管理者権限フラグ。</param>
    /// <param name="cancellationToken">キャンセレーショントークン。</param>
    /// <returns>アイテム詳細データ。閲覧権限がない場合または存在しない場合は null。</returns>
    Task<ItemDetailPageData?> GetItemDetailAsync(int itemId, string? currentUserId, bool isAdmin, CancellationToken cancellationToken = default);
}

/// <summary>
///     ItemDetail コンポーネント用データアクセスプロバイダーの実装。
/// </summary>
/// <param name="dbFactory">DbContext ファクトリ。</param>
public class ItemDetailDataProvider(IDbContextFactory<ApplicationDbContext> dbFactory)
    : IItemDetailDataProvider
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));

    private static IQueryable<Item> IncludeItemDetails(IQueryable<Item> query) =>
        query
            .Include(i => i.Owner)
            .Include(i => i.TargetUserGroup)
            .Include(i => i.QuotedItem)
                .ThenInclude(q => q!.Owner)
            .Include(i => i.TagRelations)
            .ThenInclude(tr => tr.Tag)
            .ThenInclude(t => t.Owner)
            .Include(i => i.TagRelations)
            .ThenInclude(tr => tr.Tag)
            .ThenInclude(t => t.TargetTagRelations)
            .ThenInclude(ttr => ttr.Tag)
            .ThenInclude(t => t.Owner)
            .Include(i => i.TagRelations)
            .ThenInclude(tr => tr.Owner)
            .Include(i => i.AsRequestOf)
            .ThenInclude(r => r.Target)
            .ThenInclude(t => t.Item)
            .Include(i => i.AsRequestOf)
            .ThenInclude(r => r.RequestedTag)
            .Include(i => i.NotificationRecipients)
            .AsNoTracking();

    /// <inheritdoc />
    public Task<ItemDetailPageData?> GetItemDetailAsync(int itemId, CancellationToken cancellationToken = default) =>
        GetItemDetailAsync(itemId, null, false, cancellationToken);

    /// <inheritdoc />
    public Task<ItemDetailPageData?> GetItemDetailAsync(int itemId, string? currentUserId, CancellationToken cancellationToken = default) =>
        GetItemDetailAsync(itemId, currentUserId, false, cancellationToken);

    /// <inheritdoc />
    public async Task<ItemDetailPageData?> GetItemDetailAsync(int itemId, string? currentUserId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync(cancellationToken);
        Item? item = await IncludeItemDetails(context.Items)
            .FirstOrDefaultAsync(i => i.Id == itemId, cancellationToken);

        if (item is null)
        {
            return null;
        }

        if (!await item.IsItemVisibleToUserAsync(context, currentUserId, isAdmin))
        {
            return null;
        }

        List<Item> ancestors = [];
        if (item.ParentItemId.HasValue)
        {
            var parentId = item.ParentItemId.Value;
            FormattableString sql = $@"
                WITH AncestorTree AS (
                    SELECT Id, ParentItemId, 0 AS Depth
                    FROM Items
                    WHERE Id = {parentId}
                    UNION ALL
                    SELECT i.Id, i.ParentItemId, a.Depth + 1
                    FROM Items i
                    INNER JOIN AncestorTree a ON i.Id = a.ParentItemId
                )
                SELECT Id FROM AncestorTree
                ORDER BY Depth DESC
            ";
            var ancestorIds = await context.Database.SqlQuery<int>(sql).ToListAsync(cancellationToken);

            if (ancestorIds.Count > 0)
            {
                var ancestorItems = await IncludeItemDetails(context.Items)
                    .Where(i => ancestorIds.Contains(i.Id))
                    .WhereVisibleToUser(context, currentUserId, isAdmin)
                    .ToDictionaryAsync(i => i.Id, cancellationToken);

                // CTE でソートされた ancestorIds の深さ順を維持してエンティティ一覧を構築
                ancestors = [.. ancestorIds
                    .Select(id => ancestorItems.GetValueOrDefault(id))
                    .OfType<Item>()];
            }
        }

        // 子方向 (リプライ一覧)
        List<Item> replies = await IncludeItemDetails(context.Items)
            .Where(i => i.ParentItemId == itemId)
            .WhereVisibleToUser(context, currentUserId, isAdmin)
            .OrderBy(i => i.CreatedDate)
            .ToListAsync(cancellationToken);

        // 兄弟方向 (同一親への他のリプライ)
        List<Item> siblings = [];
        if (item.ParentItemId.HasValue)
        {
            siblings = await IncludeItemDetails(context.Items)
                .Where(i => i.ParentItemId == item.ParentItemId.Value && i.Id != itemId)
                .WhereVisibleToUser(context, currentUserId, isAdmin)
                .OrderBy(i => i.CreatedDate)
                .ToListAsync(cancellationToken);
        }

        // 引用方向 (このアイテムを引用しているアイテム一覧)
        List<Item> quotes = await IncludeItemDetails(context.Items)
            .Where(i => i.QuotedItemId == itemId)
            .WhereVisibleToUser(context, currentUserId, isAdmin)
            .OrderByDescending(i => i.CreatedDate)
            .ToListAsync(cancellationToken);

        List<TagWeightLedger> ledgers = await context.TagWeightLedgers
            .Include(l => l.Owner)
            .Include(l => l.Tag)
            .Include(l => l.CommentItem)
                .ThenInclude(ci => ci!.Owner)
            .Include(l => l.CommentItem)
                .ThenInclude(ci => ci!.TagRelations)
                    .ThenInclude(tr => tr.Tag)
            .Include(l => l.TagRelation)
                .ThenInclude(tr => tr!.Tag)
            .Include(l => l.TagRelation)
                .ThenInclude(tr => tr!.CommentItem)
                    .ThenInclude(ci => ci!.Owner)
            .Include(l => l.TagRelation)
                .ThenInclude(tr => tr!.CommentItem)
                    .ThenInclude(ci => ci!.TagRelations)
                        .ThenInclude(tr => tr.Tag)
            .Where(l => l.ItemId == itemId || (l.TagRelation != null && l.TagRelation.ItemId == itemId))
            .OrderByDescending(l => l.CreatedDate)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        // 既存の TagRelation に対する元帳 (TagWeightLedger) が存在しない場合（アイテム作成時の初期タグ付与など）、
        // 付与されたタグの履歴として表示できるよう補完する
        if (item.TagRelations is { Count: > 0 })
        {
            var existingLedgerRelationIds = ledgers
                .Where(l => l.SourceId.HasValue)
                .Select(l => l.SourceId!.Value)
                .ToHashSet();

            var existingLedgerTagIds = ledgers
                .Where(l => l.ItemId == itemId)
                .Select(l => l.TagId)
                .ToHashSet();

            foreach (TagRelation tr in item.TagRelations)
            {
                if (!existingLedgerRelationIds.Contains(tr.Id) && !existingLedgerTagIds.Contains(tr.TagId))
                {
                    ledgers.Add(new TagWeightLedger
                    {
                        TagId = tr.TagId,
                        Tag = tr.Tag!,
                        TagNameSnapshot = tr.Tag?.Name ?? string.Empty,
                        ItemId = itemId,
                        SourceType = "TagRelationInsert",
                        SourceId = tr.Id,
                        TagRelation = tr,
                        CommentItemId = tr.CommentItemId,
                        CommentItem = tr.CommentItem,
                        Delta = tr.Weight,
                        PreviousWeight = 0,
                        NewWeight = tr.Tag?.CachedWeight ?? tr.Weight,
                        IsOwnerAction = tr.OwnerId == item.OwnerId,
                        Reason = "タグの新規追加",
                        OwnerId = tr.OwnerId,
                        Owner = tr.Owner ?? tr.Tag?.Owner ?? item.Owner,
                        CreatedDate = tr.CreatedDate,
                        UpdatedDate = tr.UpdatedDate
                    });
                }
            }

            ledgers = [.. ledgers.OrderByDescending(l => l.CreatedDate)];
        }

        List<Tag> allTags = await context.Tags
            .Include(t => t.TargetUserGroup)
            .WhereVisibleToUser(context, currentUserId, isAdmin)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        List<TagRelationToTag> allTagRelationsToTags =
            await context.TagRelationToTags
                .Include(ttr => ttr.Tag)
                .Where(ttr => ttr.Tag.Owner == null || !ttr.Tag.Owner.IsBanned)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

        List<Item> allItemsToFilter = [item, .. ancestors, .. replies, .. siblings, .. quotes];
        await allItemsToFilter.FilterVisibleTagRelationsAsync(context, currentUserId, isAdmin, cancellationToken);

        return new ItemDetailPageData(item, allTags, allTagRelationsToTags, ledgers, ancestors, replies, siblings, quotes);
    }
}