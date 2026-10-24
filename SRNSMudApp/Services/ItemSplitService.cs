// CA1508: union 型 (Result<T>) の網羅的パターンマッチにおける解析器の誤検知のため抑制する。
#pragma warning disable CA1508

using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using SRNSMudApp.Data;
using SRNSMudApp.Models;
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

    [LoggerMessage(EventId = 3006, Level = LogLevel.Warning, Message = "分割承認後の通知送信に失敗しました（アイテム分割自体はコミット完了しています）: {Message}")]
    private static partial void LogSplitNotificationFailed(ILogger logger, Exception ex, string message);

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

        Result<ItemSplitTextMatch> textMatchResult = ItemSplitTextPolicy.ValidateAndLocate(originalItem.Content ?? string.Empty, selectedText);
        if (textMatchResult is Failure f)
        {
            return f;
        }

        ItemSplitTextMatch match = textMatchResult switch
        {
            Success<ItemSplitTextMatch> s => s.Value,
            _ => throw new InvalidOperationException()
        };
        var (index, textToReplace) = match;

        // requesterQuoteItemId が指定された場合、その引用アイテムが requesterUserId 所有であり、
        // かつ元アイテムの引用であるかを確認する（他アイテムの誤削除や権限昇格を防ぐ）
        if (requesterQuoteItemId.HasValue)
        {
            bool quoteItemValid = await context.Items.AnyAsync(
                i => i.Id == requesterQuoteItemId.Value &&
                     i.OwnerId == requesterUserId &&
                     i.QuotedItemId == originalItemId,
                cancellationToken);
            if (!quoteItemValid)
            {
                return Result.Fail<ItemSplitRequest>(ErrorMessages.SplitQuoteItemNotFound);
            }
        }

        var (lf, crlf) = ItemSplitTextPolicy.NormalizeLineEndings(selectedText);

        // 同一アイテム・同一テキスト・同一送信者による重複リクエスト（申請中）を防止（改行コードの差を吸収）
        bool duplicateExists = await context.ItemSplitRequests
            .AnyAsync(r => r.OriginalItemId == originalItemId &&
                           r.RequesterUserId == requesterUserId &&
                           (r.SelectedText == selectedText || r.SelectedText == crlf || r.SelectedText == lf) &&
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

        Result<ItemSplitTextMatch> textMatchResult = ItemSplitTextPolicy.ValidateAndLocate(originalItem.Content, selectedText);
        if (textMatchResult is Failure f)
        {
            return f;
        }

        ItemSplitTextMatch match = textMatchResult switch
        {
            Success<ItemSplitTextMatch> s => s.Value,
            _ => throw new InvalidOperationException()
        };
        var (index, textToReplace) = match;

        await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        Item newItem;
        try
        {
            newItem = await CreateSplitItemAsync(context, originalItem, textToReplace, index, cancellationToken);
            originalItem.UpdatedDate = DateTime.UtcNow;
            _ = await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        _notificationService.NotifyNotificationsChanged();
        LogSplitDirectly(_logger, originalItemId, newItem.Id, ownerUserId);

        return Result.Ok(newItem);
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

        if (!request.CanBeApprovedBy(ownerUserId))
        {
            if (request.OwnerUserId != ownerUserId)
            {
                return Result.Fail<Item>(ErrorMessages.NotAuthorizedToApprove);
            }

            return Result.Fail<Item>(ErrorMessages.FormatSplitRequestNotProposedForApprove(request.Status));
        }

        Item originalItem = request.OriginalItem;
        if (originalItem is null)
        {
            return Result.Fail<Item>(ErrorMessages.SplitOriginalItemNotFound);
        }

        Result<ItemSplitTextMatch> textMatchResult = ItemSplitTextPolicy.ValidateAndLocateForApprove(originalItem.Content, request.SelectedText);
        if (textMatchResult is Failure f)
        {
            return f;
        }

        ItemSplitTextMatch match = textMatchResult switch
        {
            Success<ItemSplitTextMatch> s => s.Value,
            _ => throw new InvalidOperationException()
        };
        var (index, requestTextToReplace) = match;

        int? quoteItemId = request.RequesterQuoteItemId;
        Item newItem;

        await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            newItem = await CreateSplitItemAsync(context, originalItem, requestTextToReplace, index, cancellationToken);

            // 元アイテム本文の該当テキストを /ItemDetail/{newItem.Id} リンクに置換
            originalItem.UpdatedDate = DateTime.UtcNow;

            // リクエスト状態更新
            _ = request.Approve(newItem.Id);
            request.UpdatedDate = DateTime.UtcNow;

            // 引用アイテムの移行: リクエスト者が同時に投稿した引用Itemがあれば、
            // そのタグ・被引用情報を新しく作成されたアイテムへ引き継ぎ、引用Item自体を削除する
            if (quoteItemId.HasValue)
            {
                request.RequesterQuoteItemId = null;
                await MigrateRequesterQuoteItemAsync(context, quoteItemId.Value, newItem, cancellationToken);
            }

            _ = await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        _notificationService.NotifyNotificationsChanged();

        LogSplitApproved(_logger, splitRequestId, request.OriginalItemId, newItem.Id, ownerUserId, quoteItemId);

        // 分割リクエストの申請者に承認を通知 (自己承認時は通知を抑制: Issue 1-11)
        if (request.RequesterUserId != ownerUserId)
        {
            try
            {
                await _notificationService.NotifyUserAsync(
                    request.RequesterUserId,
                    "分割リクエストが承認されました",
                    "あなたの分割リクエストが承認され、新しいアイテムが作成されました。",
                    "/notifications", cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogSplitNotificationFailed(_logger, ex, ex.Message);
            }
        }

        return Result.Ok(newItem);
    }

    /// <summary>
    ///     リクエスト送信者が作成した引用アイテムに付随する各種リレーション（タグ、監査ログ、被引用、
    ///     リプライ、コントラクト、通報、オプトアウト等）を、分割により新設されたアイテムへ引き継ぎ、
    ///     不要となった引用アイテムを削除する。
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

        int targetItemId = targetItem.Id;
        int newRootId = targetItem.RootItemId ?? targetItemId;

        await MigrateTagRelationsAndLedgersAsync(context, quoteItem, targetItem, cancellationToken);
        await MigrateQuotingItemsAndLinksAsync(context, quoteItemId, targetItemId, cancellationToken);
        await MigrateRepliesAndDescendantsAsync(context, quoteItemId, targetItem, cancellationToken);
        await MigrateTaggingRequestsAndContractsAsync(context, quoteItem, targetItem, cancellationToken);
        await MigrateReportsAndOptOutsAsync(context, quoteItemId, newRootId, targetItemId, cancellationToken);
        await MigrateRecipientsAndTimelineEventsAsync(context, quoteItemId, targetItemId, cancellationToken);

        // 12. 引用アイテム自体を削除する（引用アイテムの TagRelations は Cascade で削除）
        _ = context.Items.Remove(quoteItem);
    }

    /// <summary>
    ///     引用アイテムのタグ関連付け（TagRelation）およびタグ監査ログ（TagWeightLedger）を新アイテムへ移行・マージする。
    /// </summary>
    private static async Task MigrateTagRelationsAndLedgersAsync(
        ApplicationDbContext context,
        Item quoteItem,
        Item targetItem,
        CancellationToken cancellationToken)
    {
        // 1. TagRelation（タグ付け情報）を新アイテムへ付け替える
        //    新アイテムに同一タグが既にある場合は Weight を加算、なければ新規作成
        List<TagRelation> existingRelationsOnTarget = await context.TagRelations
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
            .Where(l => l.ItemId == quoteItem.Id)
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
            .Where(l => l.CommentItemId == quoteItem.Id)
            .ToListAsync(cancellationToken);

        foreach (TagWeightLedger ledger in commentLedgers)
        {
            ledger.CommentItemId = targetItem.Id;
            ledger.UpdatedDate = DateTime.UtcNow;
        }

        // 3. 外部の TagRelation で quoteItem.Id を CommentItemId として参照しているものを新アイテムへ付け替える
        List<TagRelation> externalTagRelations = await context.TagRelations
            .Where(tr => tr.CommentItemId == quoteItem.Id)
            .ToListAsync(cancellationToken);

        foreach (TagRelation tr in externalTagRelations)
        {
            tr.CommentItemId = targetItem.Id;
            tr.UpdatedDate = DateTime.UtcNow;
        }
    }

    /// <summary>
    ///     引用アイテムを引用している他アイテム（被引用）および他アイテム本文内のリンクを新アイテムへ付け替える。
    /// </summary>
    private static async Task MigrateQuotingItemsAndLinksAsync(
        ApplicationDbContext context,
        int quoteItemId,
        int targetItemId,
        CancellationToken cancellationToken)
    {
        string oldServerLink = $"/ItemDetail/{quoteItemId}";
        string newServerLink = $"/ItemDetail/{targetItemId}";
        string oldWasmLink = $"/Item/WasmDetail/{quoteItemId}";
        string newWasmLink = $"/Item/WasmDetail/{targetItemId}";

        string ReplaceItemLinks(string content) =>
            content
                .Replace(oldServerLink, newServerLink, StringComparison.Ordinal)
                .Replace(oldWasmLink, newWasmLink, StringComparison.Ordinal);

        // 4. 引用アイテムを引用していたItem（被引用）の QuotedItemId, ItemKindJson, 本文リンクを新アイテムへ付け替える
        List<Item> quotedByItems = await context.Items
            .Where(i => i.QuotedItemId == quoteItemId)
            .ToListAsync(cancellationToken);

        foreach (Item quotingItem in quotedByItems)
        {
            quotingItem.QuotedItemId = targetItemId;
            quotingItem.ItemKindJson = JsonSerializer.Serialize(new QuoteItem(targetItemId));
            if (!string.IsNullOrEmpty(quotingItem.Content))
            {
                quotingItem.Content = ReplaceItemLinks(quotingItem.Content);
            }
            quotingItem.UpdatedDate = DateTime.UtcNow;
        }

        // 他アイテムの本文内に含まれる引用アイテムへのリンクを新アイテムへ置換してデッドリンクを防止
        List<Item> linkReferencingItems = await context.Items
            .Where(i => (i.Content.Contains(oldServerLink) || i.Content.Contains(oldWasmLink)) && i.Id != quoteItemId)
            .ToListAsync(cancellationToken);

        foreach (Item linkItem in linkReferencingItems)
        {
            if (!string.IsNullOrEmpty(linkItem.Content))
            {
                linkItem.Content = ReplaceItemLinks(linkItem.Content);
            }
            linkItem.UpdatedDate = DateTime.UtcNow;
        }
    }

    /// <summary>
    ///     引用アイテムに対するリプライおよび子孫ツリーの参照階層を新アイテムへ付け替える。
    /// </summary>
    private static async Task MigrateRepliesAndDescendantsAsync(
        ApplicationDbContext context,
        int quoteItemId,
        Item targetItem,
        CancellationToken cancellationToken)
    {
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
    }

    /// <summary>
    ///     引用アイテムを要求アイテムまたは対象アイテムとして参照するタグ付けリクエスト（TaggingRequestEntity）、
    ///     成果物リレーション、および相互タグ付けコントラクト（MutualPayload）の対価アイテム参照を新アイテムへ移行する。
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA1508:Avoid dead code", Justification = "Payload is deserialized from JSON and must be type-checked at runtime")]
    private static async Task MigrateTaggingRequestsAndContractsAsync(
        ApplicationDbContext context,
        Item quoteItem,
        Item targetItem,
        CancellationToken cancellationToken)
    {
        // 6. TaggingRequestEntity（バウンティ依頼）の RequestItemId や TargetId、成果物リレーションを新アイテムへ付け替える
        List<TaggingRequestEntity> taggingRequests = await context.TaggingRequestEntities
            .Where(t => t.RequestItemId == quoteItem.Id)
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

        // 相互タグ付けコントラクト (Mutual) で quoteItem が対価アイテム (OfferedTargetItemId) に指定されている場合、新アイテムへ移行する
        string quoteItemOfferedKey = $"\"OfferedTargetItemId\":{quoteItem.Id}";
        List<TaggingRequestEntity> mutualRequests = await context.TaggingRequestEntities
            .Where(t => t.ContractType == ContractTypes.Mutual && t.ContractPayloadJson.Contains(quoteItemOfferedKey))
            .ToListAsync(cancellationToken);

        foreach (TaggingRequestEntity mutualReq in mutualRequests)
        {
            if (mutualReq.Payload is MutualPayload mutual && mutual.OfferedTargetItemId == quoteItem.Id)
            {
                mutualReq.Payload = mutual with { OfferedTargetItemId = targetItem.Id };
                mutualReq.UpdatedDate = DateTime.UtcNow;
            }
        }
    }

    /// <summary>
    ///     引用アイテム宛ての通報（ContentReport）および会話オプトアウト（ConversationOptOut）を新アイテムへ移行する。
    /// </summary>
    private static async Task MigrateReportsAndOptOutsAsync(
        ApplicationDbContext context,
        int quoteItemId,
        int newRootId,
        int targetItemId,
        CancellationToken cancellationToken)
    {
        // 7. ContentReport（通報）の ItemId を新アイテムへ付け替える
        List<ContentReport> contentReports = await context.ContentReports
            .Where(r => r.ItemId == quoteItemId)
            .ToListAsync(cancellationToken);

        foreach (ContentReport report in contentReports)
        {
            report.ItemId = targetItemId;
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
    }

    /// <summary>
    ///     リプライ通知受取人（ItemReplyNotificationRecipient）、他分割リクエスト、およびタイムラインイベントの参照を新アイテムへ移行する。
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA1508:Avoid dead code", Justification = "TimelineTarget is deserialized from JSON and must be type-checked at runtime")]
    private static async Task MigrateRecipientsAndTimelineEventsAsync(
        ApplicationDbContext context,
        int quoteItemId,
        int targetItemId,
        CancellationToken cancellationToken)
    {
        // 9. ItemReplyNotificationRecipient（リプライ通知受取人）の ReplyItemId を付け替える
        List<ItemReplyNotificationRecipient> notificationRecipients = await context.ItemReplyNotificationRecipients
            .Where(r => r.ReplyItemId == quoteItemId)
            .ToListAsync(cancellationToken);

        if (notificationRecipients.Count > 0)
        {
            List<string> existingRecipientsOnTarget = await context.ItemReplyNotificationRecipients
                .Where(r => r.ReplyItemId == targetItemId)
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
                    recipient.ReplyItemId = targetItemId;
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
                req.CreatedItemId = targetItemId;
            }
            if (req.OriginalItemId == quoteItemId)
            {
                req.OriginalItemId = targetItemId;
            }
            if (req.RequesterQuoteItemId == quoteItemId)
            {
                req.RequesterQuoteItemId = targetItemId;
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
            if (te.Target is ItemTarget it && it.TargetItemId == quoteItemId)
            {
                te.Target = new ItemTarget(targetItemId);
                te.UpdatedDate = DateTime.UtcNow;
            }
        }
    }

    /// <summary>
    ///     却下または取り下げとなった分割リクエストに連動する引用アイテムと、
    ///     それに紐づく未決リクエストやリプライを先行クリーンアップして安全に削除する。
    /// </summary>
    /// <param name="context">データベースコンテキスト。</param>
    /// <param name="quoteItemId">削除対象の引用アイテム ID。</param>
    /// <param name="cancellationToken">キャンセレーショントークン。</param>
    private static async Task DeleteRequesterQuoteItemAsync(
        ApplicationDbContext context,
        int quoteItemId,
        CancellationToken cancellationToken)
    {
        Item? quoteItem = await context.Items
            .Include(i => i.TagTarget)
            .FirstOrDefaultAsync(i => i.Id == quoteItemId, cancellationToken);

        if (quoteItem is null)
        {
            return;
        }

        // 1. quoteItem をターゲットまたはリクエスト本体とする TaggingRequestEntity を削除
        if (quoteItem.TagTargetId > 0)
        {
            List<TaggingRequestEntity> relatedTaggingRequests = await context.TaggingRequestEntities
                .Where(t => t.TargetId == quoteItem.TagTargetId || t.RequestItemId == quoteItem.Id)
                .ToListAsync(cancellationToken);
            if (relatedTaggingRequests.Count > 0)
            {
                context.TaggingRequestEntities.RemoveRange(relatedTaggingRequests);
            }
        }

        // 2. quoteItem に対するリプライ（コメント等）を削除
        List<Item> replyItems = await context.Items
            .Where(i => i.ParentItemId == quoteItemId || i.RootItemId == quoteItemId)
            .ToListAsync(cancellationToken);
        if (replyItems.Count > 0)
        {
            context.Items.RemoveRange(replyItems);
        }

        // 3. quoteItem を引用している別アイテムがあれば引用先を解除
        List<Item> quotingItems = await context.Items
            .Where(i => i.QuotedItemId == quoteItemId && i.Id != quoteItemId)
            .ToListAsync(cancellationToken);
        foreach (Item q in quotingItems)
        {
            q.QuotedItemId = null;
        }

        // 4. quoteItem 自体を削除
        _ = context.Items.Remove(quoteItem);
    }

    /// <summary>
    ///     分割テキストを保持する新しいアイテムを作成し、元アイテム本文の該当部分を新規アイテムへのリンクに置換する。
    /// </summary>
    /// <param name="context">データベースコンテキスト。</param>
    /// <param name="originalItem">分割元アイテム。</param>
    /// <param name="selectedText">分割して新アイテムとするテキスト。</param>
    /// <param name="selectedTextIndex">元アイテム本文内での開始位置。</param>
    /// <param name="cancellationToken">キャンセレーショントークン。</param>
    /// <returns>作成された新しい分割アイテム。</returns>
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

        if (!request.CanBeRejectedBy(ownerUserId))
        {
            if (request.OwnerUserId != ownerUserId)
            {
                return Result.Fail(ErrorMessages.NotAuthorizedToReject);
            }

            return Result.Fail(ErrorMessages.FormatSplitRequestNotProposedForReject(request.Status));
        }

        _ = request.Reject(rejectReason);
        request.UpdatedDate = DateTime.UtcNow;

        // 却下時、連動して作成された引用アイテムを自動削除する（案A）
        if (request.RequesterQuoteItemId.HasValue)
        {
            await DeleteRequesterQuoteItemAsync(context, request.RequesterQuoteItemId.Value, cancellationToken);
            request.RequesterQuoteItemId = null;
        }

        _ = await context.SaveChangesAsync(cancellationToken);

        _notificationService.NotifyNotificationsChanged();

        LogSplitRejected(_logger, splitRequestId, ownerUserId, rejectReason);

        // 分割リクエストの申請者に却下を通知 (自己却下時は通知を抑制: Issue 1-11)
        if (request.RequesterUserId != ownerUserId)
        {
            try
            {
                await _notificationService.NotifyUserAsync(
                    request.RequesterUserId,
                    "分割リクエストが却下されました",
                    "あなたの分割リクエストが却下されました。",
                    "/notifications", cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogSplitNotificationFailed(_logger, ex, ex.Message);
            }
        }

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

        if (!request.CanBeCanceledBy(requesterUserId))
        {
            if (request.RequesterUserId != requesterUserId)
            {
                return Result.Fail(ErrorMessages.NotAuthorizedToCancel);
            }

            return Result.Fail(ErrorMessages.FormatSplitRequestNotProposedForCancel(request.Status));
        }

        _ = request.Cancel();
        request.UpdatedDate = DateTime.UtcNow;

        // 取り下げ時、連動して作成された引用アイテムを自動削除する（案A）
        if (request.RequesterQuoteItemId.HasValue)
        {
            await DeleteRequesterQuoteItemAsync(context, request.RequesterQuoteItemId.Value, cancellationToken);
            request.RequesterQuoteItemId = null;
        }

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