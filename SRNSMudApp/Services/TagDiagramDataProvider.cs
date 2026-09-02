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
    public async Task<List<RightAsset>> GetAvailableRightAssetsAsync(string userId, int targetTagId)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();
        var assets = await context.RightAssets
            .Where(r => r.OwnerId == userId && r.TargetTagId == targetTagId && !r.IsBurned && r.Amount > 0)
            .OrderByDescending(r => r.Amount)
            .AsNoTracking()
            .ToListAsync();

        if (assets.Count == 0 && !string.IsNullOrWhiteSpace(userId))
        {
            var tag = await context.Tags.FindAsync(targetTagId);
            if (tag != null && tag.OwnerId == userId)
            {
                // タグオーナー自身で未消費 RightAsset が存在しない場合、自動的に新規 RightAsset を発行して付与
                var newAsset = new RightAsset
                {
                    OwnerId = userId,
                    TargetTagId = targetTagId,
                    Amount = 10,
                    IsBurned = false,
                    Status = new NotBurned()
                };
                _ = context.RightAssets.Add(newAsset);
                _ = await context.SaveChangesAsync();
                assets.Add(newAsset);
            }
        }

        return assets;
    }

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