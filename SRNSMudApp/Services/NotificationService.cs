using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SRNSMudApp.Data;
using SRNSMudApp.Models;

namespace SRNSMudApp.Services;

public class NotificationService(IDbContextFactory<ApplicationDbContext> dbFactory) : INotificationService
{
    public async Task<List<NotificationDto>> GetUserNotificationsAsync(string userId)
    {
        await using var context = await dbFactory.CreateDbContextAsync();

        // 1. Tag Requests targeting the user
        // 自分に対するリクエスト（自分が作成者ではない、かつ、対象アイテムまたはタグのオーナーが自分であるもの）
        var tagRequests = await context.TaggingRequestEntities!
            .Include(r => r.TargetItem)
            .Include(r => r.RequestedTag)
            .Where(r => r.RequesterUserId != userId && (r.TargetItem.OwnerId == userId || r.RequestedTag.OwnerId == userId))
            .ToListAsync();

        // 2. Item Replies targeting the user
        // 自分のアイテムに対する別ユーザーからのリプライ
        var itemReplies = await context.Items!
            .Include(i => i.ParentItem)
            .Include(i => i.Owner)
            .Where(i => i.ParentItemId != null && i.ParentItem!.OwnerId == userId && i.OwnerId != userId)
            .ToListAsync();

        var readStates = await context.NotificationReadStates!
            .Where(n => n.UserId == userId)
            .ToListAsync();

        var notifications = new List<NotificationDto>();

        foreach (var req in tagRequests)
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

        foreach (var reply in itemReplies)
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

        return notifications.OrderByDescending(n => n.CreatedAt).ToList();
    }

    public async Task MarkAsReadAsync(string userId, int sourceId, string sourceType)
    {
        await using var context = await dbFactory.CreateDbContextAsync();
        
        var existing = await context.NotificationReadStates!
            .FirstOrDefaultAsync(n => n.UserId == userId && n.SourceId == sourceId && n.SourceType == sourceType);

        if (existing == null)
        {
            context.NotificationReadStates!.Add(new NotificationReadState
            {
                UserId = userId,
                SourceId = sourceId,
                SourceType = sourceType,
                ReadAt = DateTimeOffset.UtcNow
            });
            await context.SaveChangesAsync();
        }
    }
}
