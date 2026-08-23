using System.Numerics.Tensors;

using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;

namespace SRNSMudApp.Services;

/// <summary>タグフィルタ条件 (ユーザー名指定は任意)。</summary>
public sealed record ItemListFilter(int TagId, string? UserName);

/// <summary>ソート条件。</summary>
public sealed record ItemListSort(int TagId, bool Ascending);

/// <summary>一覧ページの表示データ。</summary>
public sealed record ItemListPageData(List<Item> Items, List<Tag> Tags);

/// <summary>JSON エクスポート用の生データ。</summary>
public sealed record ItemListExportData(
    Dictionary<int, Tag> AllTags,
    List<TagRelation> ItemTagRelations,
    List<TagRelationToTag> TagToTagRelations);

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
    Task<List<string>> SearchTagNameSuggestionsAsync(string searchText, CancellationToken token = default);

    /// <summary>タグに付けられたユーザー名候補を検索する ("タグ名 @ユーザー名" 形式で返す)。</summary>
    Task<List<string>> SearchTagUserNamesAsync(string tagName, string userSearch, CancellationToken token = default);

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

    public async Task<List<string>> SearchTagNameSuggestionsAsync(
        string searchText,
        CancellationToken token = default)
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync(token);
        try
        {
            float[] queryVector = (await tagEmbeddingService.GenerateEmbeddingAsync(searchText)).ToArray();

            List<Tag> textMatches = await context.Tags
                .Where(t => t.Name.Contains(searchText) || t.Content.Contains(searchText))
                .AsNoTracking()
                .ToListAsync(token);

            List<Tag> vectorTags = await context.Tags
                .Where(t => t.Embedding != null)
                .AsNoTracking()
                .ToListAsync(token);

            List<Tag> vectorMatches = vectorTags
                .Where(t => t.Embedding.Length == queryVector.Length)
                .OrderByDescending(t => TensorPrimitives.CosineSimilarity(t.Embedding, queryVector))
                .Take(10)
                .ToList();

            return textMatches.Concat(vectorMatches)
                .Select(t => t.Name + " @") // 末尾に " @" を付与
                .Distinct()
                .Take(10)
                .ToList();
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

    public async Task<List<string>> SearchTagUserNamesAsync(
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
            false => users.Select(u => tagName + " @" + u).ToList()
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
        switch (filters.Count != 0)
        {
            case true:
                foreach (ItemListFilter filter in filters)
                {
                    switch (string.IsNullOrWhiteSpace(filter.UserName))
                    {
                        case true:
                            query = query.Where(i => i.TagRelations.Any(tr => tr.TagId == filter.TagId));
                            tagQuery = tagQuery.Where(t => t.TargetTagRelations.Any(tr => tr.TagId == filter.TagId));
                            break;
                        case false:
                            query = query.Where(i =>
                                i.TagRelations.Any(tr =>
                                    tr.TagId == filter.TagId && tr.Owner.UserName == filter.UserName));
                            tagQuery = tagQuery.Where(t =>
                                t.TargetTagRelations.Any(tr =>
                                    tr.TagId == filter.TagId && tr.Owner.UserName == filter.UserName));
                            break;
                    }
                }

                foundTags = await ApplyTagSort(tagQuery, sorts).ToListAsync();
                break;
            case false:
                foundTags = [];
                break;
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

        List<int> relatedTagIds = itemTags.Select(t => t.TagId).Distinct().ToList();

        List<TagRelationToTag> tagToTags = await context.TagRelationToTags
            .Where(trt => relatedTagIds.Contains(trt.TargetTagId))
            .ToListAsync();

        return new ItemListExportData(allTags, itemTags, tagToTags);
    }

    private static IQueryable<Item> ApplyItemSort(IQueryable<Item> query, IReadOnlyList<ItemListSort> sorts)
    {
        switch (sorts.Count == 0)
        {
            case true:
                return query.OrderByDescending(i => i.UpdatedDate);
        }

        IOrderedQueryable<Item>? orderedQuery = null;

        foreach (ItemListSort sort in sorts)
        {
            var targetId = sort.TagId;
            switch (orderedQuery)
            {
                case null:
                    orderedQuery = sort.Ascending
                        ? query.OrderBy(i => i.TagRelations.Where(tr => tr.TagId == targetId).Sum(tr => (int?)tr.Weight) ?? 0)
                        : query.OrderByDescending(i => i.TagRelations.Where(tr => tr.TagId == targetId).Sum(tr => (int?)tr.Weight) ?? 0);
                    break;
                case not null:
                    orderedQuery = sort.Ascending
                        ? orderedQuery.ThenBy(i => i.TagRelations.Where(tr => tr.TagId == targetId).Sum(tr => (int?)tr.Weight) ?? 0)
                        : orderedQuery.ThenByDescending(i => i.TagRelations.Where(tr => tr.TagId == targetId).Sum(tr => (int?)tr.Weight) ?? 0);
                    break;
            }
        }

        return orderedQuery ?? query.OrderByDescending(i => i.UpdatedDate);
    }

    private static IQueryable<Tag> ApplyTagSort(IQueryable<Tag> query, IReadOnlyList<ItemListSort> sorts)
    {
        switch (sorts.Count == 0)
        {
            case true:
                return query.OrderByDescending(t => t.UpdatedDate);
        }

        IOrderedQueryable<Tag>? orderedQuery = null;

        foreach (ItemListSort sort in sorts)
        {
            var targetId = sort.TagId;
            switch (orderedQuery)
            {
                case null:
                    orderedQuery = sort.Ascending
                        ? query.OrderBy(t => t.TargetTagRelations.Where(tr => tr.TagId == targetId).Sum(tr => (int?)tr.Weight) ?? 0)
                        : query.OrderByDescending(t => t.TargetTagRelations.Where(tr => tr.TagId == targetId).Sum(tr => (int?)tr.Weight) ?? 0);
                    break;
                case not null:
                    orderedQuery = sort.Ascending
                        ? orderedQuery.ThenBy(t => t.TargetTagRelations.Where(tr => tr.TagId == targetId).Sum(tr => (int?)tr.Weight) ?? 0)
                        : orderedQuery.ThenByDescending(t => t.TargetTagRelations.Where(tr => tr.TagId == targetId).Sum(tr => (int?)tr.Weight) ?? 0);
                    break;
            }
        }

        return orderedQuery ?? query.OrderByDescending(t => t.UpdatedDate);
    }
}
