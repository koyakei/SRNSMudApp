using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Models;

namespace SRNSMudApp.Services;

/// <summary>
///     NotificationsPage コンポーネント用のデータアクセスを分離するインターフェース。
///     コンポーネントから DbContext への直接依存を断ち、単体テストでモック可能にする。
/// </summary>
public interface INotificationsDataProvider
{
    /// <summary>通知に関連付けられたアイテムを関連データ込みで取得する。</summary>
    Task<List<Item>> GetAssociatedItemsAsync(IReadOnlyList<int> itemIds, CancellationToken cancellationToken = default);

    /// <summary>ユーザー向けの各種通知生成に必要な未加工エンティティ群を取得する。</summary>
    Task<NotificationRawData> GetNotificationRawDataAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>通知を既読として記録する。</summary>
    Task MarkAsReadAsync(string userId, int sourceId, string sourceType, CancellationToken cancellationToken = default);

    /// <summary>指定された複数の通知を一括で既読として記録する。</summary>
    Task MarkAllAsReadAsync(string userId, IEnumerable<(int SourceId, string SourceType)> items, CancellationToken cancellationToken = default);
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
        return await context.Database.ExecuteWithStrategyAsync(async () =>
            await context.Items
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
                .ToListAsync(cancellationToken));
    }

    /// <inheritdoc />
    public async Task<NotificationRawData> GetNotificationRawDataAsync(string userId, CancellationToken cancellationToken = default)
    {
        // 14並列の Task.Run + Task.WhenAll によるクエリ同時実行は、接続プールと SQL Server の
        // メモリリソースプール (default) のクエリ実行ワークスペースメモリを枯渇させ
        // SqlException (insufficient system memory in resource pool 'default') を引き起こすため、
        // 単一の DbContext で順次非同期実行してメモリプレッシャーを回避する。
        // また、ネットワーク瞬断やトランスポートエラー（TCP Provider, error: 2）発生時に
        // 再実行戦略 (ExecutionStrategy) によって自動再試行されるよう ExecuteWithStrategyAsync で包括する。
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync(cancellationToken);

        return await context.Database.ExecuteWithStrategyAsync(async () =>
        {
            // 1. Tag Requests targeting the user
            List<TaggingRequestEntity> tagRequests = await context.TaggingRequestEntities!
                .AsNoTracking()
                .Include(r => r.Target).ThenInclude(t => t.Item)
                .Include(r => r.RequestedTag)
                .Where(r => r.RequesterUserId != userId &&
                            (r.Target.OwnerId == userId || r.RequestedTag.OwnerId == userId))
                .ToListAsync(cancellationToken);

            // 2. Item Replies targeting the user
            List<Item> itemReplies = await context.Items!
                .AsNoTracking()
                .Include(i => i.ParentItem)
                .Include(i => i.Owner)
                .Include(i => i.NotificationRecipients)
                .Where(i => i.ParentItemId != 0 &&
                            (i.NotificationRecipients.Any(r => r.RecipientUserId == userId) ||
                             (!i.NotificationRecipients.Any() && i.ParentItem!.OwnerId == userId)) &&
                            i.OwnerId != userId &&
                            !i.Content.StartsWith("【タグ操作権限リクエスト】") &&
                            !i.ItemKindJson.Contains("TagPermissionRequestPayload"))
                .ToListAsync(cancellationToken);

            // 3 & 4. Rejected and Approved requests for the user (統合して1回のクエリで取得)
            List<TaggingRequestEntity> userTaggingRequests = await context.TaggingRequestEntities!
                .AsNoTracking()
                .Include(r => r.Target).ThenInclude(t => t.Item)
                .Include(r => r.RequestedTag)
                .Where(r => r.RequesterUserId == userId &&
                            (r.Status == TradeStatus.Rejected || r.Status == TradeStatus.Executed))
                .ToListAsync(cancellationToken);

            List<TaggingRequestEntity> rejectedRequests = [.. userTaggingRequests.Where(r => r.Status == TradeStatus.Rejected)];
            List<TaggingRequestEntity> approvedRequests = [.. userTaggingRequests.Where(r => r.Status == TradeStatus.Executed)];

            // 5. Replies to the user's requests
            List<Item> requestReplies = await context.Items!
                .AsNoTracking()
                .Include(i => i.TaggingRequest)
                .Include(i => i.Owner)
                .Where(i => i.TaggingRequestEntityId != 0 &&
                            i.TaggingRequest!.RequesterUserId == userId &&
                            i.OwnerId != userId)
                .ToListAsync(cancellationToken);

            // 6. Read states
            List<NotificationReadState> readStates = await context.NotificationReadStates!
                .AsNoTracking()
                .Where(n => n.UserId == userId)
                .ToListAsync(cancellationToken);

            // 7. Resolved reports submitted by the user
            List<ContentReport> resolvedReports = await context.ContentReports!
                .AsNoTracking()
                .Where(r => r.OwnerId == userId && r.Status != ReportStatus.Pending && r.HandledDate != null)
                .ToListAsync(cancellationToken);

            // 8. Split requests targeting the user (as item owner)
            List<ItemSplitRequest> splitRequests = await context.ItemSplitRequests!
                .AsNoTracking()
                .Include(r => r.RequesterUser)
                .Include(r => r.OriginalItem)
                .Where(r => r.OwnerUserId == userId && r.RequesterUserId != userId)
                .ToListAsync(cancellationToken);

            // 9. Resolved split requests (approved/rejected) for the requester
            List<ItemSplitRequest> resolvedSplitRequests = await context.ItemSplitRequests!
                .AsNoTracking()
                .Include(r => r.OwnerUser)
                .Include(r => r.OriginalItem)
                .Where(r => r.RequesterUserId == userId &&
                            (r.Status == TradeStatus.Executed || r.Status == TradeStatus.Rejected))
                .ToListAsync(cancellationToken);

            // 10. Tag Content proposals targeting the user (as tag owner)
            List<TagContentProposal> tagContentProposals = await context.TagContentProposals!
                .AsNoTracking()
                .Include(p => p.RequesterUser)
                .Include(p => p.Tag)
                .Where(p => p.OwnerUserId == userId && p.RequesterUserId != userId)
                .ToListAsync(cancellationToken);

            // 11. Resolved Tag Content proposals (approved/rejected) for the requester
            List<TagContentProposal> resolvedTagContentProposals = await context.TagContentProposals!
                .AsNoTracking()
                .Include(p => p.OwnerUser)
                .Include(p => p.Tag)
                .Where(p => p.RequesterUserId == userId &&
                            (p.Status == TradeStatus.Executed || p.Status == TradeStatus.Rejected))
                .ToListAsync(cancellationToken);

            // 12. Tag Name proposals targeting the user (as tag owner)
            List<TagNameProposal> tagNameProposals = await context.TagNameProposals!
                .AsNoTracking()
                .Include(p => p.RequesterUser)
                .Include(p => p.Tag)
                .Where(p => p.OwnerUserId == userId && p.RequesterUserId != userId)
                .ToListAsync(cancellationToken);

            // 13. Resolved Tag Name proposals (approved/rejected) for the requester
            List<TagNameProposal> resolvedTagNameProposals = await context.TagNameProposals!
                .AsNoTracking()
                .Include(p => p.OwnerUser)
                .Include(p => p.Tag)
                .Where(p => p.RequesterUserId == userId &&
                            (p.Status == TradeStatus.Executed || p.Status == TradeStatus.Rejected))
                .ToListAsync(cancellationToken);

            // 14. TagRelation comments targeting the user (as item owner)
            List<TagRelation> tagRelationComments = await context.TagRelations!
                .AsNoTracking()
                .Include(tr => tr.Item)
                .Include(tr => tr.Tag)
                .Include(tr => tr.Owner)
                .Include(tr => tr.CommentItem)
                .Where(tr => tr.Item.OwnerId == userId && tr.OwnerId != userId && tr.CommentItem != null && !string.IsNullOrEmpty(tr.CommentItem.Content))
                .ToListAsync(cancellationToken);

            // 15. Tag Permission requests targeting the user
            List<Item> tagPermissionRequests = await context.Items!
                .AsNoTracking()
                .Include(i => i.Owner)
                .Include(i => i.NotificationRecipients)
                .Where(i => i.NotificationRecipients.Any(r => r.RecipientUserId == userId) &&
                            i.OwnerId != userId &&
                            (i.ItemKindJson.Contains("RequestedTagId") ||
                             i.ItemKindJson.Contains("TagPermissionRequestPayload") ||
                             (i.Content != null && i.Content.StartsWith("【タグ操作権限リクエスト】"))))
                .ToListAsync(cancellationToken);

            // 16. Resolved Tag Permission requests (approved/rejected) for the requester
            List<Item> resolvedTagPermissionRequests = await context.Items!
                .AsNoTracking()
                .Include(i => i.Owner)
                .Include(i => i.NotificationRecipients)
                .Where(i => i.OwnerId == userId &&
                            (i.ItemKindJson.Contains("RequestedTagId") ||
                             i.ItemKindJson.Contains("TagPermissionRequestPayload") ||
                             (i.Content != null && i.Content.StartsWith("【タグ操作権限リクエスト】"))))
                .ToListAsync(cancellationToken);

            return new NotificationRawData(
                tagRequests,
                itemReplies,
                rejectedRequests,
                approvedRequests,
                requestReplies,
                readStates,
                resolvedReports,
                splitRequests,
                resolvedSplitRequests,
                tagContentProposals,
                resolvedTagContentProposals,
                tagNameProposals,
                resolvedTagNameProposals,
                tagRelationComments,
                tagPermissionRequests,
                resolvedTagPermissionRequests);
        });
    }

    /// <inheritdoc />
    public async Task MarkAsReadAsync(string userId, int sourceId, string sourceType, CancellationToken cancellationToken = default)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync(cancellationToken);

        await context.Database.ExecuteWithStrategyAsync(async () =>
        {
            NotificationReadState? existing = await context.NotificationReadStates
                .FirstOrDefaultAsync(n => n.UserId == userId && n.SourceId == sourceId && n.SourceType == sourceType, cancellationToken);

            if (existing is null)
            {
                _ = context.NotificationReadStates.Add(new NotificationReadState
                {
                    UserId = userId,
                    SourceId = sourceId,
                    SourceType = sourceType,
                    ReadAt = DateTimeOffset.UtcNow
                });
                _ = await context.SaveChangesAsync(cancellationToken);
            }
        });
    }

    /// <inheritdoc />
    public async Task MarkAllAsReadAsync(string userId, IEnumerable<(int SourceId, string SourceType)> items, CancellationToken cancellationToken = default)
    {
        var itemList = items.ToList();
        if (itemList.Count == 0)
        {
            return;
        }

        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync(cancellationToken);

        await context.Database.ExecuteWithStrategyAsync(async () =>
        {
            List<NotificationReadState> existing = await context.NotificationReadStates
                .Where(n => n.UserId == userId)
                .ToListAsync(cancellationToken);

            var now = DateTimeOffset.UtcNow;
            List<NotificationReadState> toAdd = [.. itemList
                .Where(item => !existing.Any(e => e.SourceId == item.SourceId && e.SourceType == item.SourceType))
                .Select(item => new NotificationReadState
                {
                    UserId = userId,
                    SourceId = item.SourceId,
                    SourceType = item.SourceType,
                    ReadAt = now
                })];

            if (toAdd.Count > 0)
            {
                context.NotificationReadStates.AddRange(toAdd);
                _ = await context.SaveChangesAsync(cancellationToken);
            }
        });
    }
}