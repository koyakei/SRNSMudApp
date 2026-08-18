using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Models.Unions;

namespace SRNSMudApp.Services;

public class NotificationService(IDbContextFactory<ApplicationDbContext> dbFactory) : INotificationService
{
    public async Task<List<NotificationDto>> GetUserNotificationsAsync(string userId)
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
            .Where(i => i.ParentItemId != null && i.ParentItem!.OwnerId == userId && i.OwnerId != userId)
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
            .Where(i => i.TaggingRequestEntityId != null &&
                        i.TaggingRequest!.RequesterUserId == userId &&
                        i.OwnerId != userId)
            .ToListAsync();

        List<NotificationReadState> readStates = await context.NotificationReadStates!
            .Where(n => n.UserId == userId)
            .ToListAsync();

        var notifications = new List<NotificationDto>();

        foreach (TaggingRequestEntity req in tagRequests)
        {
            var isRead = readStates.Any(rs => rs.SourceId == req.Id && rs.SourceType == "TagRequest");
            var typeStr = req.RequestType switch
            {
                TaggingRequestType.Add => "追加",
                TaggingRequestType.DecreaseWeight => "削除",
                _ => "不明"
            };
            var tagName = req.RequestedTag?.Name ?? "不明なタグ";
            var msg = $"{tagName}の{typeStr}リクエストが届いています。";

            notifications.Add(new NotificationDto
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
                Message = msg,
                CreatedAt = new DateTimeOffset(req.CreatedDate, TimeSpan.Zero),
                TargetUrl = new RelativeUrl($"/ItemDetail/{req.TargetItemId}"),
                IsRead = isRead,
                ActorName = "システム",
                AssociatedItemId = req.TargetItemId,
                HighlightTagId = req.RequestedTagId
            });
        }

        foreach (TaggingRequestEntity req in rejectedRequests)
        {
            var isRead = readStates.Any(rs => rs.SourceId == req.Id && rs.SourceType == "RequestRejected");
            var commentMsg = string.IsNullOrWhiteSpace(req.RejectComment) switch
            {
                true => "",
                false => $"\n理由: {req.RejectComment}"
            };
            var typeStr = req.RequestType switch
            {
                TaggingRequestType.Add => "追加",
                TaggingRequestType.DecreaseWeight => "削除",
                _ => "不明"
            };
            var tagName = req.RequestedTag?.Name ?? "不明なタグ";
            var msg = $"あなたの{tagName}の{typeStr}リクエストが却下されました。{commentMsg}";

            notifications.Add(new NotificationDto
            {
                SourceId = req.Id,
                Kind = new RequestRejectedNotification(
                    RequestId: req.Id,
                    TagName: req.RequestedTag?.Name,
                    RequestType: req.RequestType,
                    RejectComment: req.RejectComment,
                    TargetItemId: req.TargetItemId,
                    TargetTagId: req.RequestedTagId
                ),
                Message = msg,
                CreatedAt = req.RejectedAt ?? new DateTimeOffset(req.CreatedDate, TimeSpan.Zero),
                TargetUrl = new RelativeUrl($"/ItemDetail/{req.TargetItemId}"),
                IsRead = isRead,
                ActorName = "システム",
                AssociatedItemId = req.TargetItemId,
                HighlightTagId = req.RequestedTagId
            });
        }

        foreach (TaggingRequestEntity req in approvedRequests)
        {
            var isRead = readStates.Any(rs => rs.SourceId == req.Id && rs.SourceType == "RequestApproved");
            var typeStr = req.RequestType switch
            {
                TaggingRequestType.Add => "追加",
                TaggingRequestType.DecreaseWeight => "削除",
                _ => "不明"
            };
            var tagName = req.RequestedTag?.Name ?? "不明なタグ";
            var msg = $"あなたの{tagName}の{typeStr}リクエストが承認されました。";

            notifications.Add(new NotificationDto
            {
                SourceId = req.Id,
                Kind = new RequestApprovedNotification(
                    RequestId: req.Id,
                    TagName: req.RequestedTag?.Name,
                    RequestType: req.RequestType,
                    TargetItemId: req.TargetItemId,
                    TargetTagId: req.RequestedTagId
                ),
                Message = msg,
                // Using UpdatedDate as an approximation of when it was executed
                CreatedAt = new DateTimeOffset(req.UpdatedDate, TimeSpan.Zero),
                TargetUrl = new RelativeUrl($"/ItemDetail/{req.TargetItemId}"),
                IsRead = isRead,
                ActorName = "システム",
                AssociatedItemId = req.TargetItemId,
                HighlightTagId = req.RequestedTagId
            });
        }

        foreach (Item reply in itemReplies)
        {
            var isRead = readStates.Any(rs => rs.SourceId == reply.Id && rs.SourceType == "ItemReply");
            var ownerName = reply.Owner?.UserName ?? "不明なユーザー";

            notifications.Add(new NotificationDto
            {
                SourceId = reply.Id,
                Kind = new ItemReplyNotification(
                    ReplyItemId: reply.Id,
                    ParentItemId: reply.ParentItemId ?? 0,
                    ActorName: ownerName
                ),
                Message = $"{ownerName}さんがあなたのアイテムにリプライしました。",
                CreatedAt = new DateTimeOffset(reply.CreatedDate, TimeSpan.Zero),
                TargetUrl = new RelativeUrl($"/ItemDetail/{reply.ParentItemId}"),
                IsRead = isRead,
                ActorName = ownerName,
                AssociatedItemId = reply.Id
            });
        }

        foreach (Item reply in requestReplies)
        {
            var isRead = readStates.Any(rs => rs.SourceId == reply.Id && rs.SourceType == "RequestReply");
            var ownerName = reply.Owner?.UserName ?? "不明なユーザー";

            notifications.Add(new NotificationDto
            {
                SourceId = reply.Id,
                Kind = new RequestReplyNotification(
                    ReplyItemId: reply.Id,
                    RequestId: reply.TaggingRequestEntityId ?? 0,
                    ActorName: ownerName
                ),
                Message = $"{ownerName}さんがあなたのリクエストに返信しました。",
                CreatedAt = new DateTimeOffset(reply.CreatedDate, TimeSpan.Zero),
                TargetUrl = new RelativeUrl($"/ItemDetail/{reply.TaggingRequestEntityId}"), // Adjust according to your routing for request detail
                IsRead = isRead,
                ActorName = ownerName,
                AssociatedItemId = reply.Id
            });
        }

        return [.. notifications.OrderByDescending(n => n.CreatedAt)];
    }

    public async Task<int> GetUnreadCountAsync(string userId)
    {
        var notifications = await GetUserNotificationsAsync(userId);
        return notifications.Count(n => !n.IsRead);
    }

    public async Task MarkAsReadAsync(string userId, int sourceId, string sourceType)
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();

        NotificationReadState? existing = await context.NotificationReadStates!
            .FirstOrDefaultAsync(n => n.UserId == userId && n.SourceId == sourceId && n.SourceType == sourceType);

        Option<NotificationReadState> option = Option<NotificationReadState>.Create(existing);
        await (option switch
        {
            None _ => AddNewReadStateAsync(context, userId, sourceId, sourceType),
            Some<NotificationReadState> _ => Task.CompletedTask,
            null => Task.CompletedTask
        });
    }

    private static async Task AddNewReadStateAsync(ApplicationDbContext context, string userId, int sourceId, string sourceType)
    {
        _ = context.NotificationReadStates!.Add(new NotificationReadState
        {
            UserId = userId,
            SourceId = sourceId,
            SourceType = sourceType,
            ReadAt = DateTimeOffset.UtcNow
        });
        _ = await context.SaveChangesAsync();
    }
}