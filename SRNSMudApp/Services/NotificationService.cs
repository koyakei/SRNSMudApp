using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Models;

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
            var typeStr = req.RequestType == TaggingRequestType.Add ? "追加" : "削除";
            var msg = $"{req.RequestedTag?.Name ?? "不明なタグ"}の{typeStr}リクエストが届いています。";

            notifications.Add(new NotificationDto
            {
                SourceId = req.Id,
                Type = "TagRequest",
                Message = msg,
                CreatedAt = new DateTimeOffset(req.CreatedDate, TimeSpan.Zero),
                TargetUrl = $"/ItemDetail/{req.TargetItemId}",
                IsRead = isRead,
                ActorName = "システム",
                Icon = MudBlazor.Icons.Material.Filled.Mail,
                IconColor = "Primary",
                AssociatedItemId = req.TargetItemId,
                HighlightTagId = req.RequestedTagId,
                RequestInfo = new Components.Shared.RequestInfo
                {
                    IsTaggingRequest = true,
                    RequestType = req.RequestType,
                    ProposedWeight = req.ProposedWeight,
                    TargetItemId = req.TargetItemId,
                    TargetItemContent = req.TargetItem?.Content,
                    TargetTagId = req.RequestedTagId,
                    TargetTagName = req.RequestedTag?.Name,
                    Status = req.Status
                }
            });
        }

        foreach (TaggingRequestEntity req in rejectedRequests)
        {
            var isRead = readStates.Any(rs => rs.SourceId == req.Id && rs.SourceType == "RequestRejected");
            var commentMsg = string.IsNullOrWhiteSpace(req.RejectComment) ? "" : $"\n理由: {req.RejectComment}";
            var typeStr = req.RequestType == TaggingRequestType.Add ? "追加" : "削除";
            var msg = $"あなたの{req.RequestedTag?.Name ?? "不明なタグ"}の{typeStr}リクエストが却下されました。{commentMsg}";

            notifications.Add(new NotificationDto
            {
                SourceId = req.Id,
                Type = "RequestRejected",
                Message = msg,
                CreatedAt = req.RejectedAt ?? new DateTimeOffset(req.CreatedDate, TimeSpan.Zero),
                TargetUrl = $"/ItemDetail/{req.TargetItemId}",
                IsRead = isRead,
                ActorName = "システム",
                Icon = MudBlazor.Icons.Material.Filled.Cancel,
                IconColor = "Error",
                AssociatedItemId = req.TargetItemId,
                HighlightTagId = req.RequestedTagId,
                RequestInfo = new Components.Shared.RequestInfo
                {
                    IsTaggingRequest = true,
                    RequestType = req.RequestType,
                    ProposedWeight = req.ProposedWeight,
                    TargetItemId = req.TargetItemId,
                    TargetItemContent = req.TargetItem?.Content,
                    TargetTagId = req.RequestedTagId,
                    TargetTagName = req.RequestedTag?.Name,
                    Status = req.Status
                }
            });
        }

        foreach (TaggingRequestEntity req in approvedRequests)
        {
            var isRead = readStates.Any(rs => rs.SourceId == req.Id && rs.SourceType == "RequestApproved");
            var typeStr = req.RequestType == TaggingRequestType.Add ? "追加" : "削除";
            var msg = $"あなたの{req.RequestedTag?.Name ?? "不明なタグ"}の{typeStr}リクエストが承認されました。";

            notifications.Add(new NotificationDto
            {
                SourceId = req.Id,
                Type = "RequestApproved",
                Message = msg,
                // Using UpdatedDate as an approximation of when it was executed
                CreatedAt = new DateTimeOffset(req.UpdatedDate, TimeSpan.Zero),
                TargetUrl = $"/ItemDetail/{req.TargetItemId}",
                IsRead = isRead,
                ActorName = "システム",
                Icon = MudBlazor.Icons.Material.Filled.CheckCircle,
                IconColor = "Success",
                AssociatedItemId = req.TargetItemId,
                HighlightTagId = req.RequestedTagId,
                RequestInfo = new Components.Shared.RequestInfo
                {
                    IsTaggingRequest = true,
                    RequestType = req.RequestType,
                    ProposedWeight = req.ProposedWeight,
                    TargetItemId = req.TargetItemId,
                    TargetItemContent = req.TargetItem?.Content,
                    TargetTagId = req.RequestedTagId,
                    TargetTagName = req.RequestedTag?.Name,
                    Status = req.Status
                }
            });
        }

        foreach (Item reply in itemReplies)
        {
            var isRead = readStates.Any(rs => rs.SourceId == reply.Id && rs.SourceType == "ItemReply");
            var ownerName = reply.Owner?.UserName ?? "不明なユーザー";

            notifications.Add(new NotificationDto
            {
                SourceId = reply.Id,
                Type = "ItemReply",
                Message = $"{ownerName}さんがあなたのアイテムにリプライしました。",
                CreatedAt = new DateTimeOffset(reply.CreatedDate, TimeSpan.Zero),
                TargetUrl = $"/ItemDetail/{reply.ParentItemId}",
                IsRead = isRead,
                ActorName = ownerName,
                Icon = MudBlazor.Icons.Material.Filled.Reply,
                IconColor = "Info",
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
                Type = "RequestReply",
                Message = $"{ownerName}さんがあなたのリクエストに返信しました。",
                CreatedAt = new DateTimeOffset(reply.CreatedDate, TimeSpan.Zero),
                TargetUrl = $"/ItemDetail/{reply.TaggingRequestEntityId}", // Adjust according to your routing for request detail
                IsRead = isRead,
                ActorName = ownerName,
                Icon = MudBlazor.Icons.Material.Filled.Forum,
                IconColor = "Secondary",
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

        if (existing == null)
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
}