using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Models.Unions;

// CA1508: union 型 (Option<T> / CheckAuth 結果など) の網羅的パターンマッチでは、先行アームの後の
// Some / エラー型アームが静的に「常に真」とみなされるが、網羅性確保のためアームは必須。
// 解析器の誤検知のため、ファイル単位で抑制する。
#pragma warning disable CA1508

// IDE0010 / IDE0072: union 型・enum の網羅的 switch に対する「Populate switch」は、
// 全ケース列挙済み・default 併記済みでも解消されない解析器の誤検知のため抑制する。
#pragma warning disable IDE0010, IDE0072

namespace SRNSMudApp.Services;

public class NotificationService(IDbContextFactory<ApplicationDbContext> dbFactory) : INotificationService
{
    public async Task<IReadOnlyList<NotificationDto>> GetUserNotificationsAsync(string userId)
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();

        // 1. Tag Requests targeting the user
        List<TaggingRequestEntity> tagRequests = await context.TaggingRequestEntities!
            .Include(r => r.TargetItem)
            .Include(r => r.RequestedTag)
            .Where(r => r.RequesterUserId != userId &&
                        (r.TargetItem.OwnerId == userId || r.RequestedTag.OwnerId == userId))
            .ToListAsync();

        // 2. Item Replies targeting the user
        List<Item> itemReplies = await context.Items!
            .Include(i => i.ParentItem)
            .Include(i => i.Owner)
            .Where(i => i.ParentItemId != 0 && i.ParentItem!.OwnerId == userId && i.OwnerId != userId)
            .ToListAsync();

        // 3. Rejected requests for the user
        List<TaggingRequestEntity> rejectedRequests = await context.TaggingRequestEntities!
            .Include(r => r.TargetItem)
            .Include(r => r.RequestedTag)
            .Where(r => r.RequesterUserId == userId && r.Status == TradeStatus.Rejected)
            .ToListAsync();

        // 4. Approved requests for the user
        List<TaggingRequestEntity> approvedRequests = await context.TaggingRequestEntities!
            .Include(r => r.TargetItem)
            .Include(r => r.RequestedTag)
            .Where(r => r.RequesterUserId == userId && r.Status == TradeStatus.Executed)
            .ToListAsync();

        // 5. Replies to the user's requests
        List<Item> requestReplies = await context.Items!
            .Include(i => i.TaggingRequest)
            .Include(i => i.Owner)
            .Where(i => i.TaggingRequestEntityId != 0 &&
                        i.TaggingRequest!.RequesterUserId == userId &&
                        i.OwnerId != userId)
            .ToListAsync();

        List<NotificationReadState> readStates = await context.NotificationReadStates!
            .Where(n => n.UserId == userId)
            .ToListAsync();

        // 純粋な変換は宣言的に構築し、最後にソートする (mainRules: 再代入撲滅 / modern-csharp: Prefer LINQ)
        IEnumerable<NotificationDto> notifications =
            BuildTagRequestNotifications(tagRequests, readStates)
                .Concat(BuildRejectedRequestNotifications(rejectedRequests, readStates))
                .Concat(BuildApprovedRequestNotifications(approvedRequests, readStates))
                .Concat(BuildReplyNotifications(itemReplies, readStates, "ItemReply"))
                .Concat(BuildReplyNotifications(requestReplies, readStates, "RequestReply"));

        return [.. notifications.OrderByDescending(n => n.CreatedAt)];
    }

    public async Task<int> GetUnreadCountAsync(string userId)
    {
        IReadOnlyList<NotificationDto> notifications = await GetUserNotificationsAsync(userId);
        return notifications.Count(n => !n.IsRead);
    }

    public async Task MarkAsReadAsync(string userId, int sourceId, string sourceType)
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();

        NotificationReadState? existing = await context.NotificationReadStates!
            .FirstOrDefaultAsync(n => n.UserId == userId && n.SourceId == sourceId && n.SourceType == sourceType);

        var option = Option<NotificationReadState>.Create(existing);
        await (option switch
        {
            None => AddNewReadStateAsync(context, userId, sourceId, sourceType),
            Some<NotificationReadState> => Task.CompletedTask,
            null => Task.CompletedTask
        });
    }

    private static async Task AddNewReadStateAsync(ApplicationDbContext context, string userId, int sourceId, string sourceType)
    {
        _ = context.NotificationReadStates.Add(new NotificationReadState
        {
            UserId = userId,
            SourceId = sourceId,
            SourceType = sourceType,
            ReadAt = DateTimeOffset.UtcNow
        });
        _ = await context.SaveChangesAsync();
    }

    private static string GetRequestTypeLabel(TaggingRequestType? type) => type switch
    {
        TaggingRequestType.Add => "追加",
        TaggingRequestType.DecreaseWeight => "削除",
        _ => "不明"
    };

    private static bool IsRead(List<NotificationReadState> readStates, int sourceId, string sourceType) =>
        readStates.Any(rs => rs.SourceId == sourceId && rs.SourceType == sourceType);

    /// <summary>自分宛てのタグ付けリクエストから通知 DTO を生成する。</summary>
    private static IEnumerable<NotificationDto> BuildTagRequestNotifications(
        IEnumerable<TaggingRequestEntity> requests,
        List<NotificationReadState> readStates) =>
        requests.Select(req => new NotificationDto
        {
            SourceId = req.Id,
            Kind = new TagRequestNotification(
                RequestId: req.Id,
                RequestType: req.RequestType,
                TargetItemId: req.TargetItemId,
                TargetTagName: req.RequestedTag?.Name,
                TargetTagId: req.RequestedTagId,
                ProposedWeight: req.ProposedWeight,
                Status: req.Status
            ),
            Message = $"{req.RequestedTag?.Name ?? "不明なタグ"}の{GetRequestTypeLabel(req.RequestType)}リクエストが届いています。",
            CreatedAt = new DateTimeOffset(req.CreatedDate, TimeSpan.Zero),
            TargetUrl = new RelativeUrl($"/ItemDetail/{req.TargetItemId}"),
            IsRead = IsRead(readStates, req.Id, "TagRequest"),
            ActorName = "システム",
            AssociatedItemId = req.TargetItemId,
            HighlightTagId = req.RequestedTagId
        });

    /// <summary>リクエスタ自身の却下済みリクエストから通知 DTO を生成する。</summary>
    private static IEnumerable<NotificationDto> BuildRejectedRequestNotifications(
        IEnumerable<TaggingRequestEntity> requests,
        List<NotificationReadState> readStates) =>
        requests.Select(req =>
        {
            var commentMsg = string.IsNullOrWhiteSpace(req.Rejection is RejectionReason r ? r.Reason : "") switch
            {
                true => "",
                false => $"\n理由: {(req.Rejection is RejectionReason rr ? rr.Reason : "")}"
            };
            return new NotificationDto
            {
                SourceId = req.Id,
                Kind = new RequestRejectedNotification(
                    RequestId: req.Id,
                    TagName: req.RequestedTag?.Name,
                    RequestType: req.RequestType,
                    RejectComment: req.Rejection is RejectionReason rr2 ? rr2.Reason : "",
                    TargetItemId: req.TargetItemId,
                    TargetTagId: req.RequestedTagId
                ),
                Message = $"あなたの{req.RequestedTag?.Name ?? "不明なタグ"}の{GetRequestTypeLabel(req.RequestType)}リクエストが却下されました。{commentMsg}",
                CreatedAt = new DateTimeOffset(req.UpdatedDate, TimeSpan.Zero),
                TargetUrl = new RelativeUrl($"/ItemDetail/{req.TargetItemId}"),
                IsRead = IsRead(readStates, req.Id, "RequestRejected"),
                ActorName = "システム",
                AssociatedItemId = req.TargetItemId,
                HighlightTagId = req.RequestedTagId
            };
        });

    /// <summary>リクエスタ自身の承認済みリクエストから通知 DTO を生成する。</summary>
    private static IEnumerable<NotificationDto> BuildApprovedRequestNotifications(
        IEnumerable<TaggingRequestEntity> requests,
        List<NotificationReadState> readStates) =>
        requests.Select(req => new NotificationDto
        {
            SourceId = req.Id,
            Kind = new RequestApprovedNotification(
                RequestId: req.Id,
                TagName: req.RequestedTag?.Name,
                RequestType: req.RequestType,
                TargetItemId: req.TargetItemId,
                TargetTagId: req.RequestedTagId
            ),
            // UpdatedDate を実行時刻の近似として使用する
            Message = $"あなたの{req.RequestedTag?.Name ?? "不明なタグ"}の{GetRequestTypeLabel(req.RequestType)}リクエストが承認されました。",
            CreatedAt = new DateTimeOffset(req.UpdatedDate, TimeSpan.Zero),
            TargetUrl = new RelativeUrl($"/ItemDetail/{req.TargetItemId}"),
            IsRead = IsRead(readStates, req.Id, "RequestApproved"),
            ActorName = "システム",
            AssociatedItemId = req.TargetItemId,
            HighlightTagId = req.RequestedTagId
        });

    /// <summary>リプライ / リクエスト返信から通知 DTO を生成する。</summary>
    private static IEnumerable<NotificationDto> BuildReplyNotifications(
        IEnumerable<Item> replies, List<NotificationReadState> readStates, string sourceType) =>
        replies.Select(reply =>
        {
            var ownerName = reply.Owner?.UserName ?? "不明なユーザー";
            var isRequestReply = sourceType == "RequestReply";
            // 対応するクエリで ID != 0 が保証されているため既定値は使用されない (CS8629 回避)
            var relatedItemId = isRequestReply
                ? reply.TaggingRequestEntityId.GetValueOrDefault()
                : reply.ParentItemId.GetValueOrDefault();
            return new NotificationDto
            {
                SourceId = reply.Id,
                Kind = isRequestReply
                    ? new RequestReplyNotification(
                        ReplyItemId: reply.Id,
                        RequestId: relatedItemId,
                        ActorName: ownerName
                    )
                    : new ItemReplyNotification(
                        ReplyItemId: reply.Id,
                        ParentItemId: relatedItemId,
                        ActorName: ownerName
                    ),
                Message = $"{ownerName}さんがあなたの{(isRequestReply ? "リクエスト" : "アイテム")}に{(isRequestReply ? "返信" : "リプライ")}しました。",
                CreatedAt = new DateTimeOffset(reply.CreatedDate, TimeSpan.Zero),
                TargetUrl = new RelativeUrl($"/ItemDetail/{relatedItemId}"),
                IsRead = IsRead(readStates, reply.Id, sourceType),
                ActorName = ownerName,
                AssociatedItemId = reply.Id
            };
        });
}