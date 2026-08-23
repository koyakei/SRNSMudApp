using System.Diagnostics.CodeAnalysis;
using System.Numerics.Tensors;

using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;

namespace SRNSMudApp.Services;

/// <summary>タグフィルタ条件 (ユーザー名指定は任意)。</summary>
public sealed record ItemListFilter(int TagId, string? UserName);

/// <summary>ソート条件。</summary>
public sealed record ItemListSort(int TagId, bool Ascending);

/// <summary>一覧ページの表示データ。</summary>
public sealed record ItemListPageData(IReadOnlyList<Item> Items, IReadOnlyList<Tag> Tags);

/// <summary>JSON エクスポート用の生データ。</summary>
public sealed record ItemListExportData(
    Dictionary<int, Tag> AllTags,
    IReadOnlyList<TagRelation> ItemTagRelations,
    IReadOnlyList<TagRelationToTag> TagToTagRelations);

/// <summary>
///     ItemList コンポーネント用のデータアクセスを分離するインターフェース。
///     コンポーネントから DbContext への直接依存を断ち、単体テストでモック可能にする。
/// </summary>
public interface IItemListDataProvider
{
    /// <summary>ID 群に一致するタグを取得する。</summary>
    Task<Dictionary<int, Tag>> GetTagsByIdsAsync(IEnumerable<int> tagIds);

    Task<Tag?> FindTagByNameAsync(string tagName);

    /// <summary>タグ名候補をテキスト + ベクトル類似度で検索する (末尾に " @" を付けて返す)。</summary>
    Task<IReadOnlyList<string>> SearchTagNameSuggestionsAsync(string searchText, CancellationToken token = default);

    /// <summary>タグに付けられたユーザー名候補を検索する ("タグ名 @ユーザー名" 形式で返す)。</summary>
    Task<IReadOnlyList<string>> SearchTagUserNamesAsync(string tagName, string userSearch, CancellationToken token = default);

    /// <summary>フィルタ・ソート条件を適用したアイテム / タグ一覧を取得する。</summary>
    Task<ItemListPageData> LoadItemsAndTagsAsync(
        IReadOnlyList<ItemListFilter> filters,
        IReadOnlyList<ItemListSort> sorts);

    /// <summary>エクスポートに必要な生データを取得する。</summary>
    Task<ItemListExportData> LoadExportDataAsync(IReadOnlyList<int> itemIds);
}

public class ItemListDataProvider(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    ITagEmbeddingService tagEmbeddingService) : IItemListDataProvider
{
    public async Task<Dictionary<int, Tag>> GetTagsByIdsAsync(IEnumerable<int> tagIds)
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();
        List<Tag> tags = await context.Tags
            .Where(t => tagIds.Contains(t.Id))
            .ToListAsync();
        return tags.ToDictionary(t => t.Id);
    }

    public async Task<Tag?> FindTagByNameAsync(string tagName)
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();
        return await context.Tags.FirstOrDefaultAsync(t => t.Name == tagName);
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "ユーザー入力由来の任意の例外を UI 向けメッセージに変換するため広く捕捉する")]
    public async Task<IReadOnlyList<string>> SearchTagNameSuggestionsAsync(
        string searchText,
        CancellationToken token = default)
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync(token);
        try
        {
            var queryVector = (await tagEmbeddingService.GenerateEmbeddingAsync(searchText)).ToArray();

            List<Tag> textMatches = await context.Tags
                .Where(t => t.Name.Contains(searchText) || t.Content.Contains(searchText))
                .AsNoTracking()
                .ToListAsync(token);

            List<Tag> vectorTags = await context.Tags
                .Where(t => t.Embedding != null)
                .AsNoTracking()
                .ToListAsync(token);

            var vectorMatches = vectorTags
                .Where(t => t.Embedding.Length == queryVector.Length)
                .OrderByDescending(t => TensorPrimitives.CosineSimilarity(t.Embedding, queryVector))
                .Take(10)
                .ToList();

            return [.. textMatches.Concat(vectorMatches)
                .Select(t => t.Name + " @") // 末尾に " @" を付与
                .Distinct()
                .Take(10)];
        }
        catch
        {
            return await context.Tags
                .Where(t => t.Name.Contains(searchText) || t.Content.Contains(searchText))
                .AsNoTracking()
                .Select(t => t.Name + " @")
                .Distinct()
                .Take(10)
                .ToListAsync(token);
        }
    }

    public async Task<IReadOnlyList<string>> SearchTagUserNamesAsync(
        string tagName,
        string userSearch,
        CancellationToken token = default)
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync(token);

        IQueryable<string?> usersQuery = context.TagRelations
            .AsNoTracking()
            .Where(tr => tr.Tag.Name == tagName)
            .Select(tr => tr.Owner.UserName)
            .Where(u => u != null);

        usersQuery = string.IsNullOrWhiteSpace(userSearch) switch
        {
            false => usersQuery.Where(u => u!.Contains(userSearch)),
            true => usersQuery
        };

        List<string?> users = await usersQuery.Distinct().Take(10).ToListAsync(token);

        return (users.Count == 0 && string.IsNullOrWhiteSpace(userSearch)) switch
        {
            true => [tagName + " @"],
            false => [.. users.Select(u => tagName + " @" + u)]
        };
    }

    public async Task<ItemListPageData> LoadItemsAndTagsAsync(
        IReadOnlyList<ItemListFilter> filters,
        IReadOnlyList<ItemListSort> sorts)
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();
        IQueryable<Item> query = context.Items
            .AsNoTracking()
            .Include(i => i.Owner)
            .Include(i => i.TagRelations)
            .ThenInclude(tr => tr.Tag)
            .ThenInclude(t => t.Owner)
            .Include(i => i.AsRequestOf)
            .ThenInclude(r => r.TargetItem)
            .Include(i => i.AsRequestOf)
            .ThenInclude(r => r.RequestedTag)
            .AsQueryable();

        IQueryable<Tag> tagQuery = context.Tags
            .AsNoTracking()
            .Include(t => t.Owner)
            .Include(t => t.TargetTagRelations)
            .ThenInclude(tr => tr.Tag)
            .ThenInclude(t => t.Owner)
            .AsQueryable();

        // タグフィルタ適用（AND 検索: 選択した全タグ/ユーザーの条件を満たす Item/Tag のみ）
        List<Tag> foundTags;
        if (filters.Count != 0)
        {
            foreach (ItemListFilter filter in filters)
            {
                if (string.IsNullOrWhiteSpace(filter.UserName))
                {
                    query = query.Where(i => i.TagRelations.Any(tr => tr.TagId == filter.TagId));
                    tagQuery = tagQuery.Where(t => t.TargetTagRelations.Any(tr => tr.TagId == filter.TagId));
                }
                else
                {
                    query = query.Where(i =>
                        i.TagRelations.Any(tr =>
                            tr.TagId == filter.TagId && tr.Owner.UserName == filter.UserName));
                    tagQuery = tagQuery.Where(t =>
                        t.TargetTagRelations.Any(tr =>
                            tr.TagId == filter.TagId && tr.Owner.UserName == filter.UserName));
                }
            }

            foundTags = await ApplyTagSort(tagQuery, sorts).ToListAsync();
        }
        else
        {
            foundTags = [];
        }

        List<Item> items = await ApplyItemSort(query, sorts).ToListAsync();
        return new ItemListPageData(items, foundTags);
    }

    public async Task<ItemListExportData> LoadExportDataAsync(IReadOnlyList<int> itemIds)
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();

        Dictionary<int, Tag> allTags = await context.Tags.ToDictionaryAsync(t => t.Id);

        List<TagRelation> itemTags = await context.TagRelations
            .Where(tr => itemIds.Contains(tr.ItemId))
            .ToListAsync();

        var relatedTagIds = itemTags.Select(t => t.TagId).Distinct().ToList();

        List<TagRelationToTag> tagToTags = await context.TagRelationToTags
            .Where(trt => relatedTagIds.Contains(trt.TargetTagId))
            .ToListAsync();

        return new ItemListExportData(allTags, itemTags, tagToTags);
    }

    private static IQueryable<Item> ApplyItemSort(IQueryable<Item> query, IReadOnlyList<ItemListSort> sorts)
    {
        if (sorts.Count == 0)
        {
            return query.OrderByDescending(i => i.UpdatedDate);
        }

        // ソート条件の畳み込みは Aggregate で宣言的に表現する (mainRules: 再代入撲滅)
        IOrderedQueryable<Item>? orderedQuery = sorts.Aggregate(
            (IOrderedQueryable<Item>?)null,
            (current, sort) => ApplyItemSortStep(query, current, sort));

        return orderedQuery ?? query.OrderByDescending(i => i.UpdatedDate);
    }

    private static IQueryable<Tag> ApplyTagSort(IQueryable<Tag> query, IReadOnlyList<ItemListSort> sorts)
    {
        if (sorts.Count == 0)
        {
            return query.OrderByDescending(t => t.UpdatedDate);
        }

        // ソート条件の畳み込みは Aggregate で宣言的に表現する (mainRules: 再代入撲滅)
        IOrderedQueryable<Tag>? orderedQuery = sorts.Aggregate(
            (IOrderedQueryable<Tag>?)null,
            (current, sort) => ApplyTagSortStep(query, current, sort));

        return orderedQuery ?? query.OrderByDescending(t => t.UpdatedDate);
    }

    private static IOrderedQueryable<Item> ApplyItemSortStep(
        IQueryable<Item> query, IOrderedQueryable<Item>? current, ItemListSort sort)
    {
        var targetId = sort.TagId;
        return current == null
            ? sort.Ascending
                ? query.OrderBy(i => i.TagRelations.Where(tr => tr.TagId == targetId).Sum(tr => (int?)tr.Weight) ?? 0)
                : query.OrderByDescending(i => i.TagRelations.Where(tr => tr.TagId == targetId).Sum(tr => (int?)tr.Weight) ?? 0)
            : sort.Ascending
                ? current.ThenBy(i => i.TagRelations.Where(tr => tr.TagId == targetId).Sum(tr => (int?)tr.Weight) ?? 0)
                : current.ThenByDescending(i => i.TagRelations.Where(tr => tr.TagId == targetId).Sum(tr => (int?)tr.Weight) ?? 0);
    }

    private static IOrderedQueryable<Tag> ApplyTagSortStep(
        IQueryable<Tag> query, IOrderedQueryable<Tag>? current, ItemListSort sort)
    {
        var targetId = sort.TagId;
        return current == null
            ? sort.Ascending
                ? query.OrderBy(t => t.TargetTagRelations.Where(tr => tr.TagId == targetId).Sum(tr => (int?)tr.Weight) ?? 0)
                : query.OrderByDescending(t => t.TargetTagRelations.Where(tr => tr.TagId == targetId).Sum(tr => (int?)tr.Weight) ?? 0)
            : sort.Ascending
                ? current.ThenBy(t => t.TargetTagRelations.Where(tr => tr.TagId == targetId).Sum(tr => (int?)tr.Weight) ?? 0)
                : current.ThenByDescending(t => t.TargetTagRelations.Where(tr => tr.TagId == targetId).Sum(tr => (int?)tr.Weight) ?? 0);
    }
}