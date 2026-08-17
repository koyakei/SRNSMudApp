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
        // 自分に対するリクエスト（自分が作成者ではない、かつ、対象アイテムまたはタグのオーナーが自分であるもの）
        List<TaggingRequestEntity> tagRequests = await context.TaggingRequestEntities!
            .Include(r => r.TargetItem)
            .Include(r => r.RequestedTag)
            .Where(r => r.RequesterUserId != userId &&
                        (r.TargetItem.OwnerId == userId || r.RequestedTag.OwnerId == userId))
            .ToListAsync();

        // 2. Item Replies targeting the user
        // 自分のアイテムに対する別ユーザーからのリプライ
        List<Item> itemReplies = await context.Items!
            .Include(i => i.ParentItem)
            .Include(i => i.Owner)
            .Where(i => i.ParentItemId != null && i.ParentItem!.OwnerId == userId && i.OwnerId != userId)
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
                IsRead = isRead
            });
        }

        // 3. Rejected requests for the user
        List<TaggingRequestEntity> rejectedRequests = await context.TaggingRequestEntities!
            .Include(r => r.TargetItem)
            .Include(r => r.RequestedTag)
            .Where(r => r.RequesterUserId == userId && r.Status == TradeStatus.Rejected)
            .ToListAsync();

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
                IsRead = isRead
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
                IsRead = isRead
            });
        }

        return [.. notifications.OrderByDescending(n => n.CreatedAt)];
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
                UserId = userId, SourceId = sourceId, SourceType = sourceType, ReadAt = DateTimeOffset.UtcNow
            });
            _ = await context.SaveChangesAsync();
        }
    }
}