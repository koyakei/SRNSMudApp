#region

using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Models;

using Tag = SRNSMudApp.Data.Tag;

#endregion

namespace SRNSMudApp.Services;

/// <summary>TagDetail ページの表示データ。</summary>
public sealed record TagDetailPageData(
    Tag? Tag,
    bool IsFollowing,
    IReadOnlyList<Item> RelatedItems,
    IReadOnlyList<Tag> RelatedTags,
    IReadOnlyList<TagWeightLedger> WeightLedgers,
    IReadOnlyList<PublicTradeOffer> PublicOffers,
    IReadOnlyList<TaggingRequestEntity> PendingRequests,
    RightAssetOverviewData? RightAssetOverview = null);

/// <summary>
///     TagDetail コンポーネント用のデータアクセスを分離するインターフェース。
///     コンポーネントから DbContext への直接依存を断ち、単体テストでモック可能にする。
/// </summary>
public interface ITagDetailDataProvider
{
    /// <summary>タグ詳細の表示データを取得する。</summary>
    /// <param name="tagId">取得対象のタグID。</param>
    /// <param name="currentUserId">現在のログインユーザーID（未ログイン時は null）。</param>
    /// <returns>タグ詳細、関連アイテム、関連タグ、履歴などの集約データ。</returns>
    Task<TagDetailPageData> GetTagDetailAsync(int tagId, string? currentUserId);

    /// <summary>タグのフォロー状態を切り替え、切替後の状態を返す。</summary>
    /// <param name="tagId">フォロー対象のタグID。</param>
    /// <param name="currentUserId">現在のログインユーザーID。</param>
    /// <returns>フォロー中になった場合は true、解除された場合は false。</returns>
    Task<bool> ToggleFollowAsync(int tagId, string currentUserId);

    /// <summary>タグを削除する。削除に成功した場合は true を返す。</summary>
    /// <param name="tagId">削除対象のタグID。</param>
    /// <param name="isAdmin">管理者の場合は true（ロックをバイパスして削除可能）。</param>
    /// <returns>削除に成功した場合は true、見つからないかロックされている場合は false。</returns>
    Task<bool> DeleteTagAsync(int tagId, bool isAdmin = false);

    /// <summary>タグを削除し、詳細な操作結果を返す。</summary>
    /// <param name="tagId">削除対象のタグID。</param>
    /// <param name="currentUserId">現在のユーザーID。</param>
    /// <param name="isAdmin">管理者の場合は true。</param>
    /// <returns>削除操作の結果。</returns>
    Task<TagDeleteOperationResult> DeleteTagWithResultAsync(int tagId, string? currentUserId, bool isAdmin = false);
}

public class TagDetailDataProvider(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    ITagLockService? tagLockService = null,
    ITagCommandService? tagCommandService = null,
    IRightAssetDataProvider? rightAssetDataProvider = null) : ITagDetailDataProvider
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
    private readonly ITagLockService? _tagLockService = tagLockService;
    private readonly ITagCommandService? _tagCommandService = tagCommandService;
    private readonly IRightAssetDataProvider _rightAssetDataProvider =
        rightAssetDataProvider ?? new RightAssetDataProvider(dbFactory);
    public async Task<TagDetailPageData> GetTagDetailAsync(int tagId, string? currentUserId)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();
        Tag? tag = await context.Tags
            .Include(t => t.Owner)
            .Include(t => t.AutoApproveUserGroups)
                .ThenInclude(g => g.UserGroup)
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tagId);

        var isFollowing = false;
        if (tag is not null && currentUserId is not null)
        {
            isFollowing = await context.UserTagFollows!
                .AnyAsync(utf => utf.TagId == tagId && utf.OwnerId == currentUserId);
        }

        List<Item> relatedItems = await context.Items
            .Include(i => i.Owner)
            .Include(i => i.TargetUserGroup)
            .Include(i => i.TagRelations)
            .ThenInclude(tr => tr.Tag)
            .ThenInclude(t => t.Owner)
            .Include(i => i.AsRequestOf)
            .ThenInclude(r => r.Target)
            .ThenInclude(t => t.Item)
            .Include(i => i.AsRequestOf)
            .ThenInclude(r => r.RequestedTag)
            .Where(i => i.TagRelations.Any(tr => tr.TagId == tagId))
            .WhereVisibleToUser(context, currentUserId)
            .OrderByDescending(i => i.UpdatedDate)
            .AsNoTracking()
            .ToListAsync();

        List<Tag> relatedTags = await context.Tags
            .Include(t => t.Owner)
            .Include(t => t.TargetTagRelations)
            .ThenInclude(tr => tr.Tag)
            .ThenInclude(t => t.Owner)
            .Where(t => t.TargetTagRelations.Any(tr => tr.TagId == tagId))
            .OrderByDescending(t => t.UpdatedDate)
            .AsNoTracking()
            .ToListAsync();

        List<TagWeightLedger> weightLedgers = await context.TagWeightLedgers!
            .Include(l => l.Owner)
            .Where(l => l.TagId == tagId || l.TargetTagId == tagId)
            .OrderByDescending(l => l.CreatedDate)
            .AsNoTracking()
            .ToListAsync();

        List<PublicTradeOffer> publicOffers = await context.PublicTradeOffers!
            .Include(o => o.Owner)
            .Include(o => o.OfferedTag)
            .Where(o => o.OfferedTagId == tagId)
            .OrderByDescending(o => o.CreatedDate)
            .AsNoTracking()
            .ToListAsync();

        List<TaggingRequestEntity> pendingRequests = await context.TaggingRequestEntities!
            .Include(r => r.Target).ThenInclude(t => t.Item)
            .Include(r => r.Owner)
            .Include(r => r.RequestedTag)
            .Include(r => r.Replies)
            .Where(r => r.RequestedTagId == tagId && r.Status == TradeStatus.Proposed)
            .OrderByDescending(r => r.CreatedDate)
            .AsNoTracking()
            .ToListAsync();

        RightAssetOverviewData? rightAssetOverview =
            await _rightAssetDataProvider.GetRightAssetOverviewByTagIdAsync(tagId);

        return new TagDetailPageData(
            tag,
            isFollowing,
            relatedItems,
            relatedTags,
            weightLedgers,
            publicOffers,
            pendingRequests,
            rightAssetOverview);
    }

    public async Task<bool> ToggleFollowAsync(int tagId, string currentUserId)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();
        UserTagFollow? followRecord = await context.UserTagFollows!
            .FirstOrDefaultAsync(utf => utf.TagId == tagId && utf.OwnerId == currentUserId);

        if (followRecord is not null)
        {
            _ = context.UserTagFollows!.Remove(followRecord);
            _ = await context.SaveChangesAsync();
            return false;
        }

        var newFollow = new UserTagFollow
        {
            TagId = tagId,
            OwnerId = currentUserId
        };
        _ = context.UserTagFollows!.Add(newFollow);
        _ = await context.SaveChangesAsync();
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteTagAsync(int tagId, bool isAdmin = false)
    {
        var result = await DeleteTagWithResultAsync(tagId, null, isAdmin);
        return result == TagDeleteOperationResult.Success;
    }

    /// <inheritdoc />
    public async Task<TagDeleteOperationResult> DeleteTagWithResultAsync(int tagId, string? currentUserId, bool isAdmin = false)
    {
        if (_tagCommandService is not null)
        {
            return await _tagCommandService.DeleteTagAsync(tagId, currentUserId, isAdmin);
        }

        if (_tagLockService != null && !isAdmin)
        {
            var isLocked = await _tagLockService.IsTagOrSiblingLockedAsync(tagId);
            if (isLocked)
            {
                return TagDeleteOperationResult.Locked;
            }
        }

        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();
        Tag? tagToDelete = await context.Tags.FindAsync(tagId);
        if (tagToDelete is null)
        {
            return TagDeleteOperationResult.NotFound;
        }

        if (tagToDelete.IsSystem || tagToDelete.Name == Tag.RootTagName)
        {
            return TagDeleteOperationResult.SystemTag;
        }

        if (!isAdmin && currentUserId is not null && tagToDelete.OwnerId != currentUserId)
        {
            return TagDeleteOperationResult.Unauthorized;
        }

        _ = context.Tags.Remove(tagToDelete);
        _ = await context.SaveChangesAsync();
        return TagDeleteOperationResult.Success;
    }
}