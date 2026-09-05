#region

using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;

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
    IReadOnlyList<TaggingRequestEntity> PendingRequests);

/// <summary>
///     TagDetail コンポーネント用のデータアクセスを分離するインターフェース。
///     コンポーネントから DbContext への直接依存を断ち、単体テストでモック可能にする。
/// </summary>
public interface ITagDetailDataProvider
{
    /// <summary>タグ詳細の表示データを取得する。</summary>
    Task<TagDetailPageData> GetTagDetailAsync(int tagId, string? currentUserId);

    /// <summary>タグのフォロー状態を切り替え、切替後の状態を返す。</summary>
    Task<bool> ToggleFollowAsync(int tagId, string currentUserId);
}

public class TagDetailDataProvider(IDbContextFactory<ApplicationDbContext> dbFactory) : ITagDetailDataProvider
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
    public async Task<TagDetailPageData> GetTagDetailAsync(int tagId, string? currentUserId)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();
        Tag? tag = await context.Tags
            .Include(t => t.Owner)
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tagId);

        var isFollowing = false;
        if (tag is not null)
        {
            switch (currentUserId)
            {
                case not null:
                    isFollowing = await context.UserTagFollows!
                        .AnyAsync(utf => utf.TagId == tagId && utf.OwnerId == currentUserId);
                    break;
                default:
                    break;
            }
        }

        List<Item> relatedItems = await context.Items
            .Include(i => i.Owner)
            .Include(i => i.TagRelations)
            .ThenInclude(tr => tr.Tag)
            .ThenInclude(t => t.Owner)
            .Include(i => i.AsRequestOf)
            .ThenInclude(r => r.Target)
            .ThenInclude(t => t.Item)
            .Include(i => i.AsRequestOf)
            .ThenInclude(r => r.RequestedTag)
            .Where(i => i.TagRelations.Any(tr => tr.TagId == tagId))
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

        return new TagDetailPageData(
            tag,
            isFollowing,
            relatedItems,
            relatedTags,
            weightLedgers,
            publicOffers,
            pendingRequests);
    }

    public async Task<bool> ToggleFollowAsync(int tagId, string currentUserId)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();
        UserTagFollow? followRecord = await context.UserTagFollows!
            .FirstOrDefaultAsync(utf => utf.TagId == tagId && utf.OwnerId == currentUserId);

        switch (followRecord)
        {
            case not null:
                _ = context.UserTagFollows!.Remove(followRecord);
                _ = await context.SaveChangesAsync();
                return false;
            default:
                {
                    var newFollow = new UserTagFollow
                    {
                        TagId = tagId,
                        OwnerId = currentUserId
                    };
                    _ = context.UserTagFollows!.Add(newFollow);
                    _ = await context.SaveChangesAsync();
                    return true;
                }
        }
    }
}