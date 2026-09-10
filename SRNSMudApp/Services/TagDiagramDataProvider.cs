using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;

namespace SRNSMudApp.Services;

/// <summary>
///     <see cref="ITagDiagramDataProvider" /> の既定実装。
/// </summary>
public class TagDiagramDataProvider(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    ITagEdgeService tagEdgeService) : ITagDiagramDataProvider
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
    private readonly ITagEdgeService _tagEdgeService =
        tagEdgeService ?? throw new ArgumentNullException(nameof(tagEdgeService));

    /// <inheritdoc />

    /// <inheritdoc />
    public async Task<List<int>> GetContextTagIdsForItemAsync(int itemId)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();
        var item = await context.Items
            .Include(i => i.TagRelations)
            .FirstOrDefaultAsync(i => i.Id == itemId);

        if (item == null) return [];

        var tagIds = new HashSet<int>();

        foreach (var tr in item.TagRelations)
        {
            tagIds.Add(tr.TagId);
        }

        var matches = SRNSMudApp.Components.UI.ItemCardViewModel.InternalLinkRegex().Matches(item.Content ?? "");
        var linkedItemIds = new List<int>();
        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            var url = match.Value;
            if (url.StartsWith("/TagDetail/", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(url.AsSpan("/TagDetail/".Length), out var tId))
                {
                    tagIds.Add(tId);
                }
            }
            else if (url.StartsWith("/ItemDetail/", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(url.AsSpan("/ItemDetail/".Length), out var iId))
                {
                    linkedItemIds.Add(iId);
                }
            }
        }

        if (item.QuotedItemId.HasValue && !linkedItemIds.Contains(item.QuotedItemId.Value))
        {
            linkedItemIds.Add(item.QuotedItemId.Value);
        }

        if (linkedItemIds.Count > 0)
        {
            var linkedTags = await context.TagRelations
                .Where(tr => linkedItemIds.Contains(tr.ItemId))
                .Select(tr => tr.TagId)
                .ToListAsync();

            foreach (var tId in linkedTags)
            {
                tagIds.Add(tId);
            }
        }

        return tagIds.ToList();
    }

    /// <summary>
    /// 指定したアイテムに関連する内部リンク先のItemを取得する。
    /// </summary>
    public async Task<List<Item>> GetContextItemsAsync(int itemId)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();
        var item = await context.Items.FirstOrDefaultAsync(i => i.Id == itemId);
        if (item == null) return [];

        var linkedItemIds = new List<int> { itemId };
        var matches = SRNSMudApp.Components.UI.ItemCardViewModel.InternalLinkRegex().Matches(item.Content ?? "");
        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            var url = match.Value;
            if (url.StartsWith("/ItemDetail/", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(url.AsSpan("/ItemDetail/".Length), out var iId))
                {
                    linkedItemIds.Add(iId);
                }
            }
        }

        if (item.QuotedItemId.HasValue && !linkedItemIds.Contains(item.QuotedItemId.Value))
        {
            linkedItemIds.Add(item.QuotedItemId.Value);
        }

        if (linkedItemIds.Count > 0)
        {
            return await context.Items
                .Include(i => i.TagRelations)
                .Where(i => linkedItemIds.Contains(i.Id))
                .ToListAsync();
        }
        return [];
    }

    /// <inheritdoc />
    public async Task<List<Tag>> LoadAllTagsAsync()
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();
        return await context.Tags
            .Where(t => !Tag.VoteTagNames.Contains(t.Name) && !Tag.ReactionTagNames.Contains(t.Name))
            .OrderBy(t => t.Name)
            .AsNoTracking()
            .ToListAsync();
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<TagEdge>> LoadAllEdgesAsync() =>
        _tagEdgeService.GetAllEdgesAsync();

    /// <inheritdoc />
    public Task<List<RightAsset>> GetAvailableRightAssetsAsync(string userId, int targetTagId) =>
        _tagEdgeService.GetAvailableRightAssetsAsync(userId, targetTagId);

    /// <inheritdoc />
    public Task<Result<TagEdge>> CreateEdgeAsync(int sourceTagId, int targetTagId, string ownerId) =>
        _tagEdgeService.CreateEdgeAsync(sourceTagId, targetTagId, ownerId);

    /// <inheritdoc />
    public Task<Result<bool>> DeleteEdgeAsync(int edgeId, string ownerId) =>
        _tagEdgeService.DeleteEdgeAsync(edgeId, ownerId);

    /// <inheritdoc />
    public Task<Result<TagEdgeTagAttachment>> AttachTagToEdgeAsync(
        int edgeId, int tagId, int rightAssetId, string currentUserId, int weight = 1) =>
        _tagEdgeService.AttachTagToEdgeAsync(edgeId, tagId, rightAssetId, currentUserId, weight);

    /// <inheritdoc />
    public Task<Result<bool>> DetachTagFromEdgeAsync(int attachmentId, string currentUserId) =>
        _tagEdgeService.DetachTagFromEdgeAsync(attachmentId, currentUserId);
}