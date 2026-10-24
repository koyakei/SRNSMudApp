using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Models;

namespace SRNSMudApp.Services;

/// <summary>
///     アイテムへのリプライやメッセージング関連の操作を担当するドメインサービス。
/// </summary>
public class ItemReplyService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    INotificationService? notificationService = null) : IItemReplyService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
    private readonly INotificationService? _notificationService = notificationService;

    public async Task<Item?> AddReplyToRequestAsync(int requestId, string userId, string message)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();
        var reply = new Item
        {
            TaggingRequestEntityId = requestId,
            OwnerId = userId,
            Content = message,
            CreatedDate = DateTime.UtcNow
        };

        _ = context.Items!.Add(reply);
        _ = await context.SaveChangesAsync();

        return await context.Items
            .Include(r => r.Owner)
            .Include(r => r.TagRelations)
            .ThenInclude(tr => tr.Tag)
            .FirstOrDefaultAsync(r => r.Id == reply.Id);
    }

    public async Task<IReadOnlyList<Item>> GetItemRepliesAsync(
        int parentItemId,
        string? currentUserId = null,
        bool isAdmin = false)
    {
        if (parentItemId <= 0)
        {
            return [];
        }

        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();

        // 1. 通常のリプライを取得 (可視性フィルタ適用)
        List<Item> regularReplies = await context.Items!
            .WhereVisibleToUser(context, currentUserId, isAdmin)
            .Include(i => i.Owner)
            .Include(i => i.TagRelations)
                .ThenInclude(tr => tr.Tag)
            .Include(i => i.NotificationRecipients)
            .Include(i => i.TargetUserGroup)
            .Where(i => i.ParentItemId == parentItemId)
            .OrderBy(i => i.CreatedDate)
            .ToListAsync();

        var replyIds = new HashSet<int>(regularReplies.Select(r => r.Id));

        // 2. タグ履歴（TagWeightLedger）由来のコメントを取得
        List<TagWeightLedger> ledgers = await context.TagWeightLedgers
            .Include(l => l.Tag)
                .ThenInclude(t => t.Owner)
            .Include(l => l.CommentItem)
                .ThenInclude(ci => ci!.Owner)
            .Include(l => l.CommentItem)
                .ThenInclude(ci => ci!.TagRelations)
                    .ThenInclude(tr => tr.Tag)
            .Include(l => l.CommentItem)
                .ThenInclude(ci => ci!.NotificationRecipients)
            .Include(l => l.CommentItem)
                .ThenInclude(ci => ci!.TargetUserGroup)
            .Include(l => l.TagRelation)
                .ThenInclude(tr => tr!.Tag)
                    .ThenInclude(t => t.Owner)
            .Include(l => l.TagRelation)
                .ThenInclude(tr => tr!.CommentItem)
                    .ThenInclude(ci => ci!.Owner)
            .Include(l => l.TagRelation)
                .ThenInclude(tr => tr!.CommentItem)
                    .ThenInclude(ci => ci!.TagRelations)
                        .ThenInclude(tr => tr.Tag)
            .Include(l => l.TagRelation)
                .ThenInclude(tr => tr!.CommentItem)
                    .ThenInclude(ci => ci!.NotificationRecipients)
            .Include(l => l.TagRelation)
                .ThenInclude(tr => tr!.CommentItem)
                    .ThenInclude(ci => ci!.TargetUserGroup)
            .Where(l => (l.ItemId == parentItemId || (l.TagRelation != null && l.TagRelation.ItemId == parentItemId))
                        && (l.CommentItemId != null || (l.TagRelation != null && l.TagRelation.CommentItemId != null)))
            .OrderBy(l => l.CreatedDate)
            .ToListAsync();

        List<Item> tagComments = [];
        var processedCommentItemIds = new HashSet<int>(replyIds);

        foreach (TagWeightLedger ledger in ledgers)
        {
            Item? commentItem = ledger.EffectiveCommentItem;
            if (commentItem is not null &&
                !string.IsNullOrWhiteSpace(commentItem.Content) &&
                await commentItem.IsItemVisibleToUserAsync(context, currentUserId, isAdmin) &&
                processedCommentItemIds.Add(commentItem.Id))
            {
                var tagName = ledger.Tag?.Name
                    ?? ledger.TagRelation?.Tag?.Name
                    ?? (!string.IsNullOrEmpty(ledger.TagNameSnapshot) ? ledger.TagNameSnapshot : "タグ");
                var tagOwnerName = ledger.Tag?.Owner?.UserName
                    ?? ledger.TagRelation?.Tag?.Owner?.UserName;
                var weight = ledger.Delta != 0 ? ledger.Delta : (ledger.TagRelation?.Weight ?? 1);

                commentItem.TagActionInfo = new TagActionMetadata(tagName, tagOwnerName, weight);
                tagComments.Add(commentItem);
            }
        }

        // 3. TagWeightLedger にまだ登録されていない TagRelation の CommentItem を補完
        List<TagRelation> relationsWithComment = await context.TagRelations
            .Include(tr => tr.Tag)
                .ThenInclude(t => t.Owner)
            .Include(tr => tr.CommentItem)
                .ThenInclude(ci => ci!.Owner)
            .Include(tr => tr.CommentItem)
                .ThenInclude(ci => ci!.TagRelations)
                    .ThenInclude(tr => tr.Tag)
            .Include(tr => tr.CommentItem)
                .ThenInclude(ci => ci!.NotificationRecipients)
            .Include(tr => tr.CommentItem)
                .ThenInclude(ci => ci!.TargetUserGroup)
            .Where(tr => tr.ItemId == parentItemId && tr.CommentItemId != null)
            .OrderBy(tr => tr.CreatedDate)
            .ToListAsync();

        foreach (TagRelation tr in relationsWithComment)
        {
            Item? commentItem = tr.CommentItem;
            if (commentItem is not null &&
                !string.IsNullOrWhiteSpace(commentItem.Content) &&
                await commentItem.IsItemVisibleToUserAsync(context, currentUserId, isAdmin) &&
                processedCommentItemIds.Add(commentItem.Id))
            {
                var tagName = tr.Tag?.Name ?? "タグ";
                var tagOwnerName = tr.Tag?.Owner?.UserName;
                var weight = tr.Weight;

                commentItem.TagActionInfo = new TagActionMetadata(tagName, tagOwnerName, weight);
                tagComments.Add(commentItem);
            }
        }

        // 4. 時系列順にマージ
        return [.. regularReplies.Concat(tagComments).OrderBy(r => r.CreatedDate)];
    }

    public async Task<int> GetItemReplyCountAsync(int parentItemId)
    {
        if (parentItemId <= 0)
        {
            return 0;
        }

        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();

        var regularReplyCount = await context.Items!
            .CountAsync(i => i.ParentItemId == parentItemId);

        var regularReplyIds = await context.Items!
            .Where(i => i.ParentItemId == parentItemId)
            .Select(i => i.Id)
            .ToListAsync();

        var ledgerCommentItemIds = await context.TagWeightLedgers
            .Where(l => (l.ItemId == parentItemId || (l.TagRelation != null && l.TagRelation.ItemId == parentItemId))
                        && (l.CommentItemId != null || (l.TagRelation != null && l.TagRelation.CommentItemId != null)))
            .Select(l => l.CommentItemId ?? l.TagRelation!.CommentItemId)
            .Where(id => id != null)
            .Select(id => id!.Value)
            .Distinct()
            .ToListAsync();

        var relationCommentItemIds = await context.TagRelations
            .Where(tr => tr.ItemId == parentItemId && tr.CommentItemId != null)
            .Select(tr => tr.CommentItemId!.Value)
            .Distinct()
            .ToListAsync();

        var allCommentItemIds = ledgerCommentItemIds
            .Concat(relationCommentItemIds)
            .Except(regularReplyIds)
            .Distinct()
            .ToList();

        if (allCommentItemIds.Count == 0)
        {
            return regularReplyCount;
        }

        var validCommentCount = await context.Items!
            .Where(i => allCommentItemIds.Contains(i.Id) && !string.IsNullOrWhiteSpace(i.Content))
            .CountAsync();

        return regularReplyCount + validCommentCount;
    }

    public async Task<Item?> AddItemReplyAsync(
        int parentItemId,
        string content,
        string userId,
        IEnumerable<string>? targetUserIds = null,
        bool isPrivate = false,
        int? targetUserGroupId = null)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();

        List<TagRelation> inheritedRelations = await context.TagRelations
            .Where(tr => tr.ItemId == parentItemId)
            .ToListAsync();

        var parentRootId = await context.Items
            .Where(i => i.Id == parentItemId)
            .Select(i => i.RootItemId)
            .FirstOrDefaultAsync();

        var replyItem = new Item
        {
            Content = content,
            OwnerId = userId,
            ParentItemId = parentItemId,
            RootItemId = parentRootId ?? parentItemId,
            IsPrivate = isPrivate,
            TargetUserGroupId = isPrivate ? targetUserGroupId : null,
            CreatedDate = DateTime.UtcNow,
            UpdatedDate = DateTime.UtcNow
        };

        _ = context.Items!.Add(replyItem);
        _ = await context.SaveChangesAsync();

        // 通知対象ユーザー（Twitterライクなメンション先）の永続化
        List<string> recipientIds;
        if (targetUserIds != null)
        {
            recipientIds = targetUserIds.Where(id => !string.IsNullOrWhiteSpace(id) && id != userId).Distinct().ToList();
        }
        else
        {
            // 省略時はスレッド参加者（親アイテムオーナー + 既存リプライ投稿者、自分を除く）を自動対象とする
            var parentOwnerId = await context.Items
                .Where(i => i.Id == parentItemId)
                .Select(i => i.OwnerId)
                .FirstOrDefaultAsync();

            var replierIds = await context.Items
                .Where(i => i.ParentItemId == parentItemId)
                .Select(i => i.OwnerId)
                .ToListAsync();

            recipientIds = [.. (new[] { parentOwnerId }.Concat(replierIds))
                .Where(id => !string.IsNullOrEmpty(id) && id != userId)
                .Distinct()!];
        }

        // Issue 4-2: 会話からオプトアウト（ConversationOptOut）したユーザーを通知対象から除外する
        int threadRootId = replyItem.RootItemId ?? parentItemId;
        List<string> optedOutUserIds = await context.ConversationOptOuts
            .Where(o => o.RootItemId == threadRootId)
            .Select(o => o.UserId)
            .ToListAsync();

        if (optedOutUserIds.Count > 0)
        {
            recipientIds = recipientIds.Where(id => !optedOutUserIds.Contains(id)).ToList();
        }

        if (recipientIds.Count > 0)
        {
            var recipients = recipientIds.Select(rid => new ItemReplyNotificationRecipient
            {
                ReplyItemId = replyItem.Id,
                RecipientUserId = rid,
                CreatedDate = DateTimeOffset.UtcNow
            });
            context.ItemReplyNotificationRecipients.AddRange(recipients);
            _ = await context.SaveChangesAsync();

            if (_notificationService != null)
            {
                string notificationBody = replyItem.IsPrivate
                    ? "非公開の返信が届きました。"
                    : (replyItem.Content.Length > 100 ? replyItem.Content[..97] + "..." : replyItem.Content);

                foreach (string rid in recipientIds)
                {
                    _ = _notificationService.NotifyUserAsync(
                        rid,
                        "新しい返信",
                        notificationBody,
                        $"/items/{parentItemId}");
                }
            }
        }

        if (inheritedRelations.Count > 0)
        {
            var replyTagRelations = inheritedRelations
                .Select(relation => new TagRelation
                {
                    ItemId = replyItem.Id,
                    TagId = relation.TagId,
                    Weight = relation.Weight,
                    OwnerId = userId,
                    CreatedDate = DateTime.UtcNow,
                    UpdatedDate = DateTime.UtcNow
                })
                .ToList();

            context.TagRelations.AddRange(replyTagRelations);
            _ = await context.SaveChangesAsync();
        }

        return await context.Items
            .Include(i => i.Owner)
            .Include(i => i.TagRelations)
            .ThenInclude(tr => tr.Tag)
            .Include(i => i.NotificationRecipients)
            .FirstOrDefaultAsync(i => i.Id == replyItem.Id);
    }

    public async Task<bool> ToggleConversationOptOutAsync(int rootItemId, string userId)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();
        var existing = await context.ConversationOptOuts
            .FirstOrDefaultAsync(o => o.RootItemId == rootItemId && o.UserId == userId);

        if (existing is not null)
        {
            context.ConversationOptOuts.Remove(existing);
        }
        else
        {
            context.ConversationOptOuts.Add(new ConversationOptOut
            {
                RootItemId = rootItemId,
                UserId = userId,
                CreatedDate = DateTimeOffset.UtcNow
            });
        }

        await context.SaveChangesAsync();
        return existing is null; // Returns true if opted out, false if opted in
    }

    public async Task<bool> IsUserOptedOutAsync(int rootItemId, string userId)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();
        return await context.ConversationOptOuts
            .AnyAsync(o => o.RootItemId == rootItemId && o.UserId == userId);
    }

    public async Task<HashSet<string>> GetOptedOutUsersAsync(int rootItemId)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();
        var userIds = await context.ConversationOptOuts
            .Where(o => o.RootItemId == rootItemId)
            .Select(o => o.UserId)
            .ToListAsync();

        return [.. userIds];
    }
}