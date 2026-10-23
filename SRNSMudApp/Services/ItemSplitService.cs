using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Resources;

namespace SRNSMudApp.Services;

/// <summary>
///     他ユーザーの Item に対する「選択テキストを別アイテムに分割」リクエストの申請、承認、却下、取り下げを管理するドメインサービス実装。
/// </summary>
public partial class ItemSplitService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    INotificationService notificationService,
    ILogger<ItemSplitService>? logger = null) : IItemSplitService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
    private readonly INotificationService _notificationService =
        notificationService ?? throw new ArgumentNullException(nameof(notificationService));
    private readonly ILogger<ItemSplitService> _logger =
        logger ?? NullLogger<ItemSplitService>.Instance;

    [LoggerMessage(EventId = 3001, Level = LogLevel.Information, Message = "Split request created: RequestId={RequestId}, OriginalItemId={OriginalItemId}, Requester={RequesterUserId}, QuoteItemId={QuoteItemId}")]
    private static partial void LogSplitRequested(ILogger logger, int requestId, int originalItemId, string requesterUserId, int? quoteItemId);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Information, Message = "Direct split executed: OriginalItemId={OriginalItemId}, NewItemId={NewItemId}, Owner={OwnerUserId}")]
    private static partial void LogSplitDirectly(ILogger logger, int originalItemId, int newItemId, string ownerUserId);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Information, Message = "Split request approved: RequestId={RequestId}, OriginalItemId={OriginalItemId}, NewItemId={NewItemId}, Owner={OwnerUserId}, QuoteItemId={QuoteItemId}")]
    private static partial void LogSplitApproved(ILogger logger, int requestId, int originalItemId, int newItemId, string ownerUserId, int? quoteItemId);

    [LoggerMessage(EventId = 3004, Level = LogLevel.Information, Message = "Split request rejected: RequestId={RequestId}, Owner={OwnerUserId}, Reason={Reason}")]
    private static partial void LogSplitRejected(ILogger logger, int requestId, string ownerUserId, string? reason);

    [LoggerMessage(EventId = 3005, Level = LogLevel.Information, Message = "Split request canceled: RequestId={RequestId}, Requester={RequesterUserId}")]
    private static partial void LogSplitCanceled(ILogger logger, int requestId, string requesterUserId);

    /// <inheritdoc />
    public async Task<Result<ItemSplitRequest>> RequestSplitAsync(
        int originalItemId,
        string selectedText,
        string requesterUserId,
        int? requesterQuoteItemId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(selectedText))
        {
            return Result.Fail<ItemSplitRequest>(ErrorMessages.SplitTextRequired);
        }

        if (string.IsNullOrWhiteSpace(requesterUserId))
        {
            return Result.Fail<ItemSplitRequest>(ErrorMessages.SplitRequesterRequired);
        }

        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync(cancellationToken);

        Item? originalItem = await context.Items
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == originalItemId, cancellationToken);

        if (originalItem is null)
        {
            return Result.Fail<ItemSplitRequest>(ErrorMessages.SplitItemNotFound);
        }

        if (originalItem.OwnerId == requesterUserId)
        {
            return Result.Fail<ItemSplitRequest>(ErrorMessages.SplitSelfItemNotAllowed);
        }

        string normalizedContent = (originalItem.Content ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal);
        string normalizedSelected = selectedText.Replace("\r\n", "\n", StringComparison.Ordinal);

        if (string.IsNullOrEmpty(originalItem.Content) || !normalizedContent.Contains(normalizedSelected, StringComparison.Ordinal))
        {
            return Result.Fail<ItemSplitRequest>(ErrorMessages.SplitSelectedTextNotInContent);
        }

        // requesterQuoteItemId が指定された場合、その引用アイテムが requesterUserId 所有のものかを確認する
        if (requesterQuoteItemId.HasValue)
        {
            bool quoteItemValid = await context.Items.AnyAsync(
                i => i.Id == requesterQuoteItemId.Value && i.OwnerId == requesterUserId,
                cancellationToken);
            if (!quoteItemValid)
            {
                return Result.Fail<ItemSplitRequest>(ErrorMessages.SplitQuoteItemNotFound);
            }
        }

        // 同一アイテム・同一テキスト・同一送信者による重複リクエスト（申請中）を防止
        bool duplicateExists = await context.ItemSplitRequests
            .AnyAsync(r => r.OriginalItemId == originalItemId &&
                           r.RequesterUserId == requesterUserId &&
                           r.SelectedText == selectedText &&
                           r.Status == TradeStatus.Proposed, cancellationToken);

        if (duplicateExists)
        {
            return Result.Fail<ItemSplitRequest>(ErrorMessages.SplitDuplicateRequestExists);
        }

        ItemSplitRequest request = new()
        {
            OriginalItemId = originalItemId,
            RequesterUserId = requesterUserId,
            OwnerUserId = originalItem.OwnerId,
            SelectedText = selectedText,
            Status = TradeStatus.Proposed,
            OwnerId = requesterUserId, // BaseEntity.OwnerId は作成者 (Requester)
            RequesterQuoteItemId = requesterQuoteItemId,
            CreatedDate = DateTime.UtcNow,
            UpdatedDate = DateTime.UtcNow
        };

        _ = context.ItemSplitRequests.Add(request);
        _ = await context.SaveChangesAsync(cancellationToken);

        _notificationService.NotifyNotificationsChanged();

        LogSplitRequested(_logger, request.Id, originalItemId, requesterUserId, requesterQuoteItemId);

        // アイテムオーナーに分割リクエストを通知
        if (originalItem.OwnerId != requesterUserId)
        {
            await _notificationService.NotifyUserAsync(
                originalItem.OwnerId,
                "分割リクエスト",
                "あなたのアイテムに対する分割リクエストが届きました。",
                "/notifications", cancellationToken);
        }

        return Result.Ok(request);
    }

    /// <inheritdoc />
    public async Task<Result<Item>> SplitDirectlyAsync(
        int originalItemId,
        string selectedText,
        string ownerUserId,
        CancellationToken cancellationToken = default)
    {
        if (originalItemId <= 0)
        {
            return Result.Fail<Item>(ErrorMessages.SplitTargetItemRequired);
        }

        if (string.IsNullOrWhiteSpace(selectedText))
        {
            return Result.Fail<Item>(ErrorMessages.SplitTextRequired);
        }

        if (string.IsNullOrWhiteSpace(ownerUserId))
        {
            return Result.Fail<Item>(ErrorMessages.OwnerUserRequired);
        }

        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync(cancellationToken);

        Item? originalItem = await context.Items
            .FirstOrDefaultAsync(i => i.Id == originalItemId, cancellationToken);

        if (originalItem is null)
        {
            return Result.Fail<Item>(ErrorMessages.SplitItemNotFound);
        }

        if (originalItem.OwnerId != ownerUserId)
        {
            return Result.Fail<Item>(ErrorMessages.NotAuthorizedToSplit);
        }

        (int index, string textToReplace) = FindSelectedText(originalItem.Content, selectedText);
        if (index < 0)
        {
            return Result.Fail<Item>(ErrorMessages.SplitSelectedTextNotInContent);
        }

        await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            Item newItem = await CreateSplitItemAsync(context, originalItem, textToReplace, index, cancellationToken);
            originalItem.UpdatedDate = DateTime.UtcNow;
            _ = await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            _notificationService.NotifyNotificationsChanged();
            LogSplitDirectly(_logger, originalItemId, newItem.Id, ownerUserId);

            return Result.Ok(newItem);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<Result<Item>> ApproveSplitAsync(
        int splitRequestId,
        string ownerUserId,
        CancellationToken cancellationToken = default)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync(cancellationToken);

        ItemSplitRequest? request = await context.ItemSplitRequests
            .Include(r => r.OriginalItem)
            .FirstOrDefaultAsync(r => r.Id == splitRequestId, cancellationToken);

        if (request is null)
        {
            return Result.Fail<Item>(ErrorMessages.RequestNotFound);
        }

        if (request.OwnerUserId != ownerUserId)
        {
            return Result.Fail<Item>(ErrorMessages.NotAuthorizedToApprove);
        }

        if (request.Status != TradeStatus.Proposed)
        {
            return Result.Fail<Item>(ErrorMessages.FormatSplitRequestNotProposedForApprove(request.Status));
        }

        Item originalItem = request.OriginalItem;
        if (originalItem is null)
        {
            return Result.Fail<Item>(ErrorMessages.SplitOriginalItemNotFound);
        }

        (int index, string requestTextToReplace) = FindSelectedText(originalItem.Content, request.SelectedText);
        if (index < 0)
        {
            return Result.Fail<Item>(ErrorMessages.SplitOriginalItemContentChanged);
        }

        await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            Item newItem = await CreateSplitItemAsync(context, originalItem, requestTextToReplace, index, cancellationToken);

            // 元アイテム本文の該当テキストを /ItemDetail/{newItem.Id} リンクに置換
            originalItem.UpdatedDate = DateTime.UtcNow;

            // リクエスト状態更新
            request.Status = TradeStatus.Executed;
            request.CreatedItemId = newItem.Id;
            request.UpdatedDate = DateTime.UtcNow;

            // 引用アイテムの移行: リクエスト者が同時に投稿した引用Itemがあれば、
            // そのタグ・被引用情報を新しく作成されたアイテムへ引き継ぎ、引用Item自体を削除する
            int? quoteItemId = request.RequesterQuoteItemId;
            if (quoteItemId.HasValue)
            {
                request.RequesterQuoteItemId = null;
                await MigrateRequesterQuoteItemAsync(context, quoteItemId.Value, newItem, cancellationToken);
            }

            _ = await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            _notificationService.NotifyNotificationsChanged();

            LogSplitApproved(_logger, splitRequestId, request.OriginalItemId, newItem.Id, ownerUserId, quoteItemId);

            // 分割リクエストの申請者に承認を通知
            await _notificationService.NotifyUserAsync(
                request.RequesterUserId,
                "分割リクエストが承認されました",
                "あなたの分割リクエストが承認され、新しいアイテムが作成されました。",
                "/notifications", cancellationToken);

            return Result.Ok(newItem);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    ///     分割リクエスト申請者が同時に投稿した引用アイテムを、
    ///     承認によって新しく作成されたアイテムへ移行する。
    ///     引用アイテムに付いていた TagRelation（タグ）、TagWeightLedger（監査履歴ログ）、
    ///     被引用（QuotedByItems）、リプライツリー（ParentItem/RootItem）、バウンティ（TaggingRequest）、
    ///     通報（ContentReport）、オプトアウト（ConversationOptOut）、通知受取人、タイムラインなど、
    ///     ApplicationDbContextModelSnapshot に定義されたすべてのエンティティリレーションを漏れなく新アイテムへ引き継ぎ、
    ///     引用アイテム自体を削除する。
    /// </summary>
    private static async Task MigrateRequesterQuoteItemAsync(
        ApplicationDbContext context,
        int quoteItemId,
        Item targetItem,
        CancellationToken cancellationToken)
    {
        Item? quoteItem = await context.Items
            .Include(i => i.TagRelations)
                .ThenInclude(tr => tr.CommentItem)
            .FirstOrDefaultAsync(i => i.Id == quoteItemId, cancellationToken);

        if (quoteItem is null)
        {
            return;
        }

        // 1. TagRelation（タグ付け情報）を新アイテムへ付け替える
        //    新アイテムに同一タグが既にある場合は Weight を加算、なければ新規作成
        var existingRelationsOnTarget = await context.TagRelations
            .Include(tr => tr.CommentItem)
            .Where(x => x.ItemId == targetItem.Id)
            .ToListAsync(cancellationToken);

        // 古い TagRelationId -> 新しい TagRelation（またはマージ先）のマッピング
        Dictionary<int, TagRelation> relationMapping = [];

        foreach (TagRelation tr in quoteItem.TagRelations.ToList())
        {
            TagRelation? existing = existingRelationsOnTarget.FirstOrDefault(x => x.TagId == tr.TagId && x.OwnerId == tr.OwnerId);
            if (existing is not null)
            {
                existing.Weight += tr.Weight;
                existing.CommentItemId ??= tr.CommentItemId;
                existing.UpdatedDate = DateTime.UtcNow;
                relationMapping[tr.Id] = existing;
            }
            else
            {
                TagRelation newRelation = new()
                {
                    TagId = tr.TagId,
                    Weight = tr.Weight,
                    OwnerId = tr.OwnerId,
                    ItemId = targetItem.Id,
                    CommentItemId = tr.CommentItemId,
                    CreatedDate = tr.CreatedDate,
                    UpdatedDate = DateTime.UtcNow
                };
                _ = context.TagRelations.Add(newRelation);
                existingRelationsOnTarget.Add(newRelation);
                relationMapping[tr.Id] = newRelation;
            }

            // TagRelation に付随する CommentItem の ItemKindJson も targetItem.Id に更新
            if (tr.CommentItem is not null)
            {
                tr.CommentItem.ItemKindJson = JsonSerializer.Serialize(new TagCommentItem(targetItem.Id, tr.TagId));
                tr.CommentItem.UpdatedDate = DateTime.UtcNow;
            }
        }

        // 2. TagWeightLedger（タグ監査・履歴ログ）の ItemId, CommentItemId, SourceId を更新
        List<TagWeightLedger> itemLedgers = await context.TagWeightLedgers
            .Where(l => l.ItemId == quoteItemId)
            .ToListAsync(cancellationToken);

        foreach (TagWeightLedger ledger in itemLedgers)
        {
            ledger.ItemId = targetItem.Id;
            if (ledger.SourceId.HasValue && relationMapping.TryGetValue(ledger.SourceId.Value, out TagRelation? targetRelation))
            {
                ledger.TagRelation = targetRelation;
            }
            ledger.UpdatedDate = DateTime.UtcNow;
        }

        List<TagWeightLedger> commentLedgers = await context.TagWeightLedgers
            .Where(l => l.CommentItemId == quoteItemId)
            .ToListAsync(cancellationToken);

        foreach (TagWeightLedger ledger in commentLedgers)
        {
            ledger.CommentItemId = targetItem.Id;
            ledger.UpdatedDate = DateTime.UtcNow;
        }

        // 3. 外部の TagRelation で quoteItemId を CommentItemId として参照しているものを新アイテムへ付け替える
        List<TagRelation> externalTagRelations = await context.TagRelations
            .Where(tr => tr.CommentItemId == quoteItemId)
            .ToListAsync(cancellationToken);

        foreach (TagRelation tr in externalTagRelations)
        {
            tr.CommentItemId = targetItem.Id;
            tr.UpdatedDate = DateTime.UtcNow;
        }

        // 4. 引用アイテムを引用していたItem（被引用）の QuotedItemId, ItemKindJson, 本文リンクを新アイテムへ付け替える
        List<Item> quotedByItems = await context.Items
            .Where(i => i.QuotedItemId == quoteItemId)
            .ToListAsync(cancellationToken);

        string oldServerLink = $"/ItemDetail/{quoteItemId}";
        string newServerLink = $"/ItemDetail/{targetItem.Id}";
        string oldWasmLink = $"/Item/WasmDetail/{quoteItemId}";
        string newWasmLink = $"/Item/WasmDetail/{targetItem.Id}";

        foreach (Item quotingItem in quotedByItems)
        {
            quotingItem.QuotedItemId = targetItem.Id;
            quotingItem.ItemKindJson = JsonSerializer.Serialize(new QuoteItem(targetItem.Id));
            if (!string.IsNullOrEmpty(quotingItem.Content))
            {
                if (quotingItem.Content.Contains(oldServerLink, StringComparison.Ordinal))
                {
                    quotingItem.Content = quotingItem.Content.Replace(oldServerLink, newServerLink, StringComparison.Ordinal);
                }
                if (quotingItem.Content.Contains(oldWasmLink, StringComparison.Ordinal))
                {
                    quotingItem.Content = quotingItem.Content.Replace(oldWasmLink, newWasmLink, StringComparison.Ordinal);
                }
            }
            quotingItem.UpdatedDate = DateTime.UtcNow;
        }

        // 他アイテムの本文内に含まれる引用アイテムへのリンク（/ItemDetail/{quoteItemId} および /Item/WasmDetail/{quoteItemId}）を新アイテムへ置換してデッドリンクを防止
        List<Item> linkReferencingItems = await context.Items
            .Where(i => (i.Content.Contains(oldServerLink) || i.Content.Contains(oldWasmLink)) && i.Id != quoteItemId)
            .ToListAsync(cancellationToken);

        foreach (Item linkItem in linkReferencingItems)
        {
            if (linkItem.Content.Contains(oldServerLink, StringComparison.Ordinal))
            {
                linkItem.Content = linkItem.Content.Replace(oldServerLink, newServerLink, StringComparison.Ordinal);
            }
            if (linkItem.Content.Contains(oldWasmLink, StringComparison.Ordinal))
            {
                linkItem.Content = linkItem.Content.Replace(oldWasmLink, newWasmLink, StringComparison.Ordinal);
            }
            linkItem.UpdatedDate = DateTime.UtcNow;
        }

        // 5. 引用アイテムに対するリプライがあれば新アイテムへ付け替える
        List<Item> replyItems = await context.Items
            .Where(i => i.ParentItemId == quoteItemId)
            .ToListAsync(cancellationToken);

        int newRootId = targetItem.RootItemId ?? targetItem.Id;
        foreach (Item replyItem in replyItems)
        {
            replyItem.ParentItemId = targetItem.Id;
            if (replyItem.RootItemId == quoteItemId)
            {
                replyItem.RootItemId = newRootId;
            }
            replyItem.UpdatedDate = DateTime.UtcNow;
        }

        // 引用アイテムを RootItemId として参照している全子孫アイテムの RootItemId を新アイテムへ付け替える
        List<Item> rootReferencingItems = await context.Items
            .Where(i => i.RootItemId == quoteItemId && i.Id != quoteItemId)
            .ToListAsync(cancellationToken);

        foreach (Item rootItem in rootReferencingItems)
        {
            rootItem.RootItemId = newRootId;
            rootItem.UpdatedDate = DateTime.UtcNow;
        }

        // 6. TaggingRequestEntity（バウンティ依頼）の RequestItemId や TargetId、成果物リレーションを新アイテムへ付け替える
        List<TaggingRequestEntity> taggingRequests = await context.TaggingRequestEntities
            .Where(t => t.RequestItemId == quoteItemId)
            .ToListAsync(cancellationToken);

        foreach (TaggingRequestEntity taggingReq in taggingRequests)
        {
            taggingReq.RequestItemId = targetItem.Id;
            taggingReq.UpdatedDate = DateTime.UtcNow;
        }

        if (quoteItem.TagTargetId > 0 && targetItem.TagTargetId > 0)
        {
            List<TaggingRequestEntity> targetBounties = await context.TaggingRequestEntities
                .Where(t => t.TargetId == quoteItem.TagTargetId)
                .ToListAsync(cancellationToken);

            foreach (TaggingRequestEntity b in targetBounties)
            {
                b.TargetId = targetItem.TagTargetId;
                b.UpdatedDate = DateTime.UtcNow;
            }
        }

        if (quoteItem.TaggingRequestEntityId.HasValue && !targetItem.TaggingRequestEntityId.HasValue)
        {
            targetItem.TaggingRequestEntityId = quoteItem.TaggingRequestEntityId;
        }

        // 7. ContentReport（通報）の ItemId を新アイテムへ付け替える
        List<ContentReport> contentReports = await context.ContentReports
            .Where(r => r.ItemId == quoteItemId)
            .ToListAsync(cancellationToken);

        foreach (ContentReport report in contentReports)
        {
            report.ItemId = targetItem.Id;
            report.UpdatedDate = DateTime.UtcNow;
        }

        // 8. ConversationOptOut（会話オプトアウト）の RootItemId を付け替える
        List<ConversationOptOut> optOuts = await context.ConversationOptOuts
            .Where(o => o.RootItemId == quoteItemId)
            .ToListAsync(cancellationToken);

        if (optOuts.Count > 0)
        {
            List<string> existingOptOutUsersOnNewRoot = await context.ConversationOptOuts
                .Where(o => o.RootItemId == newRootId)
                .Select(o => o.UserId)
                .ToListAsync(cancellationToken);

            foreach (ConversationOptOut optOut in optOuts)
            {
                if (existingOptOutUsersOnNewRoot.Contains(optOut.UserId))
                {
                    _ = context.ConversationOptOuts.Remove(optOut);
                }
                else
                {
                    optOut.RootItemId = newRootId;
                    existingOptOutUsersOnNewRoot.Add(optOut.UserId);
                }
            }
        }

        // 9. ItemReplyNotificationRecipient（リプライ通知受取人）の ReplyItemId を付け替える
        List<ItemReplyNotificationRecipient> notificationRecipients = await context.ItemReplyNotificationRecipients
            .Where(r => r.ReplyItemId == quoteItemId)
            .ToListAsync(cancellationToken);

        if (notificationRecipients.Count > 0)
        {
            List<string> existingRecipientsOnTarget = await context.ItemReplyNotificationRecipients
                .Where(r => r.ReplyItemId == targetItem.Id)
                .Select(r => r.RecipientUserId)
                .ToListAsync(cancellationToken);

            foreach (ItemReplyNotificationRecipient recipient in notificationRecipients)
            {
                if (existingRecipientsOnTarget.Contains(recipient.RecipientUserId))
                {
                    _ = context.ItemReplyNotificationRecipients.Remove(recipient);
                }
                else
                {
                    recipient.ReplyItemId = targetItem.Id;
                    existingRecipientsOnTarget.Add(recipient.RecipientUserId);
                }
            }
        }

        // 10. 他の ItemSplitRequest で引用アイテムが参照されていた場合、新アイテムへ付け替える
        List<ItemSplitRequest> otherRequests = await context.ItemSplitRequests
            .Where(r => r.CreatedItemId == quoteItemId || r.OriginalItemId == quoteItemId || r.RequesterQuoteItemId == quoteItemId)
            .ToListAsync(cancellationToken);

        foreach (ItemSplitRequest req in otherRequests)
        {
            if (req.CreatedItemId == quoteItemId)
            {
                req.CreatedItemId = targetItem.Id;
            }
            if (req.OriginalItemId == quoteItemId)
            {
                req.OriginalItemId = targetItem.Id;
            }
            if (req.RequesterQuoteItemId == quoteItemId)
            {
                req.RequesterQuoteItemId = targetItem.Id;
            }
            req.UpdatedDate = DateTime.UtcNow;
        }

        // 11. TimelineEvent の Target が ItemTarget(quoteItemId) の場合、新アイテムへ付け替える
        string quoteItemTargetKey = $"\"TargetItemId\":{quoteItemId}";
        List<TimelineEvent> timelineEvents = await context.TimelineEvents
            .Where(t => t.TimelineTargetJson.Contains(quoteItemTargetKey))
            .ToListAsync(cancellationToken);

        foreach (TimelineEvent te in timelineEvents)
        {
            switch (te.Target)
            {
                case ItemTarget it when it.TargetItemId == quoteItemId:
                    te.Target = new ItemTarget(targetItem.Id);
                    te.UpdatedDate = DateTime.UtcNow;
                    break;
                default:
                    break;
            }
        }

        // 12. 引用アイテム自体を削除する（引用アイテムの TagRelations は Cascade で削除）
        _ = context.Items.Remove(quoteItem);
    }

    private static (int Index, string TextToReplace) FindSelectedText(string content, string selectedText)
    {
        string crlf = selectedText.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal);
        string lf = selectedText.Replace("\r\n", "\n", StringComparison.Ordinal);

        ReadOnlySpan<string> candidates = [selectedText, crlf, lf];
        foreach (string candidate in candidates)
        {
            int index = content.IndexOf(candidate, StringComparison.Ordinal);
            if (index >= 0)
            {
                return (index, candidate);
            }
        }

        return (-1, selectedText);
    }

    private static async Task<Item> CreateSplitItemAsync(
        ApplicationDbContext context,
        Item originalItem,
        string selectedText,
        int selectedTextIndex,
        CancellationToken cancellationToken)
    {
        // 所有者・公開範囲・引用元の扱いを直接分割と承認分割で統一する。
        string originalContent = originalItem.Content;
        Item newItem = new()
        {
            Content = selectedText,
            OwnerId = originalItem.OwnerId,
            IsPrivate = originalItem.IsPrivate,
            TargetUserGroupId = originalItem.TargetUserGroupId,
            QuotedItemId = originalItem.Id,
            ItemKindJson = JsonSerializer.Serialize(new QuoteItem(originalItem.Id)),
            CreatedDate = DateTime.UtcNow,
            UpdatedDate = DateTime.UtcNow
        };

        _ = context.Items.Add(newItem);
        _ = await context.SaveChangesAsync(cancellationToken);
        originalItem.Content = originalContent.Remove(selectedTextIndex, selectedText.Length)
            .Insert(selectedTextIndex, $"/ItemDetail/{newItem.Id}");

        return newItem;
    }

    /// <inheritdoc />
    public async Task<Result<bool>> RejectSplitAsync(
        int splitRequestId,
        string ownerUserId,
        string? rejectReason,
        CancellationToken cancellationToken = default)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync(cancellationToken);

        ItemSplitRequest? request = await context.ItemSplitRequests
            .FirstOrDefaultAsync(r => r.Id == splitRequestId, cancellationToken);

        if (request is null)
        {
            return Result.Fail(ErrorMessages.RequestNotFound);
        }

        if (request.OwnerUserId != ownerUserId)
        {
            return Result.Fail(ErrorMessages.NotAuthorizedToReject);
        }

        if (request.Status != TradeStatus.Proposed)
        {
            return Result.Fail(ErrorMessages.FormatSplitRequestNotProposedForReject(request.Status));
        }

        request.Status = TradeStatus.Rejected;
        request.RejectReason = rejectReason;
        request.UpdatedDate = DateTime.UtcNow;

        _ = await context.SaveChangesAsync(cancellationToken);

        _notificationService.NotifyNotificationsChanged();

        LogSplitRejected(_logger, splitRequestId, ownerUserId, rejectReason);

        // 分割リクエストの申請者に却下を通知
        await _notificationService.NotifyUserAsync(
            request.RequesterUserId,
            "分割リクエストが却下されました",
            "あなたの分割リクエストが却下されました。",
            "/notifications", cancellationToken);

        return Result.Ok();
    }

    /// <inheritdoc />
    public async Task<Result<bool>> CancelSplitAsync(
        int splitRequestId,
        string requesterUserId,
        CancellationToken cancellationToken = default)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync(cancellationToken);

        ItemSplitRequest? request = await context.ItemSplitRequests
            .FirstOrDefaultAsync(r => r.Id == splitRequestId, cancellationToken);

        if (request is null)
        {
            return Result.Fail(ErrorMessages.RequestNotFound);
        }

        if (request.RequesterUserId != requesterUserId)
        {
            return Result.Fail(ErrorMessages.NotAuthorizedToCancel);
        }

        if (request.Status != TradeStatus.Proposed)
        {
            return Result.Fail(ErrorMessages.FormatSplitRequestNotProposedForCancel(request.Status));
        }

        request.Status = TradeStatus.Canceled;
        request.UpdatedDate = DateTime.UtcNow;

        _ = await context.SaveChangesAsync(cancellationToken);

        _notificationService.NotifyNotificationsChanged();

        LogSplitCanceled(_logger, splitRequestId, requesterUserId);

        // アイテムオーナーに取り下げを通知
        await _notificationService.NotifyUserAsync(
            request.OwnerUserId,
            "分割リクエストが取り下げられました",
            "分割リクエストが申請者によって取り下げられました。",
            "/notifications", cancellationToken);

        return Result.Ok();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ItemSplitRequest>> GetPendingSplitRequestsForOriginalItemAsync(
        int itemId,
        CancellationToken cancellationToken = default)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync(cancellationToken);

        return await context.ItemSplitRequests
            .AsNoTracking()
            .Include(r => r.RequesterUser)
            .Include(r => r.OwnerUser)
            .Where(r => r.OriginalItemId == itemId && r.Status == TradeStatus.Proposed)
            .OrderByDescending(r => r.CreatedDate)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ItemSplitRequest?> GetSplitRequestByIdAsync(
        int splitRequestId,
        CancellationToken cancellationToken = default)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync(cancellationToken);

        return await context.ItemSplitRequests
            .AsNoTracking()
            .Include(r => r.OriginalItem)
            .Include(r => r.RequesterUser)
            .Include(r => r.OwnerUser)
            .Include(r => r.CreatedItem)
            .FirstOrDefaultAsync(r => r.Id == splitRequestId, cancellationToken);
    }
}