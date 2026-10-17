using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;

namespace SRNSMudApp.Services;

/// <summary>
///     他ユーザーの Item に対する「選択テキストを別アイテムに分割」リクエストの申請、承認、却下、取り下げを管理するドメインサービス実装。
/// </summary>
public class ItemSplitService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    INotificationService notificationService) : IItemSplitService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
    private readonly INotificationService _notificationService =
        notificationService ?? throw new ArgumentNullException(nameof(notificationService));

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
            return Result.Fail<ItemSplitRequest>("分割するテキストが指定されていません。");
        }

        if (string.IsNullOrWhiteSpace(requesterUserId))
        {
            return Result.Fail<ItemSplitRequest>("リクエスト送信ユーザーが指定されていません。");
        }

        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync(cancellationToken);

        Item? originalItem = await context.Items
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == originalItemId, cancellationToken);

        if (originalItem is null)
        {
            return Result.Fail<ItemSplitRequest>("対象のアイテムが見つかりません。");
        }

        if (originalItem.OwnerId == requesterUserId)
        {
            return Result.Fail<ItemSplitRequest>("自分自身のアイテムに対しては分割リクエストではなく直接分割を行ってください。");
        }

        string normalizedContent = (originalItem.Content ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal);
        string normalizedSelected = selectedText.Replace("\r\n", "\n", StringComparison.Ordinal);

        if (string.IsNullOrEmpty(originalItem.Content) || !normalizedContent.Contains(normalizedSelected, StringComparison.Ordinal))
        {
            return Result.Fail<ItemSplitRequest>("選択されたテキストが元アイテムの本文に含まれていません。");
        }

        // requesterQuoteItemId が指定された場合、その引用アイテムが requesterUserId 所有のものかを確認する
        if (requesterQuoteItemId.HasValue)
        {
            bool quoteItemValid = await context.Items.AnyAsync(
                i => i.Id == requesterQuoteItemId.Value && i.OwnerId == requesterUserId,
                cancellationToken);
            if (!quoteItemValid)
            {
                return Result.Fail<ItemSplitRequest>("指定された引用アイテムが見つかりません。");
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
            return Result.Fail<ItemSplitRequest>("このテキストに対する分割リクエストは既に申請中です。");
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
            return Result.Fail<Item>("分割対象のアイテムが指定されていません。");
        }

        if (string.IsNullOrWhiteSpace(selectedText))
        {
            return Result.Fail<Item>("分割するテキストが指定されていません。");
        }

        if (string.IsNullOrWhiteSpace(ownerUserId))
        {
            return Result.Fail<Item>("所有者ユーザーが指定されていません。");
        }

        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync(cancellationToken);

        Item? originalItem = await context.Items
            .FirstOrDefaultAsync(i => i.Id == originalItemId, cancellationToken);

        if (originalItem is null)
        {
            return Result.Fail<Item>("分割対象のアイテムが見つかりません。");
        }

        if (originalItem.OwnerId != ownerUserId)
        {
            return Result.Fail<Item>("このアイテムを分割する権限がありません。");
        }

        (int index, string textToReplace) = FindSelectedText(originalItem.Content, selectedText);
        if (index < 0)
        {
            return Result.Fail<Item>("選択されたテキストが元アイテムの本文に含まれていません。");
        }

        Item newItem = await CreateSplitItemAsync(context, originalItem, textToReplace, index, cancellationToken);
        originalItem.UpdatedDate = DateTime.UtcNow;
        _ = await context.SaveChangesAsync(cancellationToken);

        _notificationService.NotifyNotificationsChanged();

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
            return Result.Fail<Item>("分割リクエストが見つかりません。");
        }

        if (request.OwnerUserId != ownerUserId)
        {
            return Result.Fail<Item>("このリクエストを承認する権限がありません。");
        }

        if (request.Status != TradeStatus.Proposed)
        {
            return Result.Fail<Item>($"申請中ではないリクエストは承認できません（現在の状態: {request.Status}）。");
        }

        Item originalItem = request.OriginalItem;
        if (originalItem is null)
        {
            return Result.Fail<Item>("分割対象の元アイテムが見つかりません。");
        }

        (int index, string requestTextToReplace) = FindSelectedText(originalItem.Content, request.SelectedText);
        if (index < 0)
        {
            return Result.Fail<Item>("元アイテムの本文が更新されたため、指定されたテキストが見つかりません。");
        }

        Item newItem = await CreateSplitItemAsync(context, originalItem, requestTextToReplace, index, cancellationToken);

        // 元アイテム本文の該当テキストを /ItemDetail/{newItem.Id} リンクに置換
        originalItem.UpdatedDate = DateTime.UtcNow;

        // リクエスト状態更新
        request.Status = TradeStatus.Executed;
        request.CreatedItemId = newItem.Id;
        request.UpdatedDate = DateTime.UtcNow;

        // 引用アイテムの移行: リクエスト者が同時に投稿した引用Itemがあれば、
        // そのタグ・被引用情報を新しく作成されたアイテムへ引き継ぎ、引用Item自体を削除する
        if (request.RequesterQuoteItemId.HasValue)
        {
            var quoteItemId = request.RequesterQuoteItemId.Value;
            request.RequesterQuoteItemId = null;
            await MigrateRequesterQuoteItemAsync(context, quoteItemId, newItem, cancellationToken);
        }

        _ = await context.SaveChangesAsync(cancellationToken);

        _notificationService.NotifyNotificationsChanged();

        // 分割リクエストの申請者に承認を通知
        await _notificationService.NotifyUserAsync(
            request.RequesterUserId,
            "分割リクエストが承認されました",
            "あなたの分割リクエストが承認され、新しいアイテムが作成されました。",
            "/notifications", cancellationToken);

        return Result.Ok(newItem);
    }

    /// <summary>
    ///     分割リクエスト申請者が同時に投稿した引用アイテムを、
    ///     承認によって新しく作成されたアイテムへ移行する。
    ///     引用アイテムに付いていた TagRelation（タグ）および被引用（QuotedByItems）を新アイテムへ付け替え、
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
            .FirstOrDefaultAsync(i => i.Id == quoteItemId, cancellationToken);

        if (quoteItem is null)
        {
            return;
        }

        // 1. TagRelation（タグ付け情報）を新アイテムへ付け替える
        //    新アイテムに同一タグが既にある場合は Weight を加算、なければ新規作成
        var existingRelationsOnTarget = await context.TagRelations
            .Where(x => x.ItemId == targetItem.Id)
            .ToListAsync(cancellationToken);

        foreach (TagRelation tr in quoteItem.TagRelations.ToList())
        {
            TagRelation? existing = existingRelationsOnTarget.FirstOrDefault(x => x.TagId == tr.TagId);
            if (existing is not null)
            {
                existing.Weight += tr.Weight;
                if (existing.CommentItemId is null && tr.CommentItemId is not null)
                {
                    existing.CommentItemId = tr.CommentItemId;
                }
                existing.UpdatedDate = DateTime.UtcNow;
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
            }
        }

        // 2. 引用アイテムを引用していたItem（被引用）の QuotedItemId を新アイテムへ付け替える
        List<Item> quotedByItems = await context.Items
            .Where(i => i.QuotedItemId == quoteItemId)
            .ToListAsync(cancellationToken);

        foreach (Item quotingItem in quotedByItems)
        {
            quotingItem.QuotedItemId = targetItem.Id;
            quotingItem.UpdatedDate = DateTime.UtcNow;
        }

        // 3. 引用アイテムに対するリプライがあれば新アイテムへ付け替える
        List<Item> replyItems = await context.Items
            .Where(i => i.ParentItemId == quoteItemId)
            .ToListAsync(cancellationToken);

        foreach (Item replyItem in replyItems)
        {
            replyItem.ParentItemId = targetItem.Id;
            if (replyItem.RootItemId == quoteItemId)
            {
                replyItem.RootItemId = targetItem.RootItemId ?? targetItem.Id;
            }
            replyItem.UpdatedDate = DateTime.UtcNow;
        }

        // 4. 引用アイテム自体を削除する（引用アイテムの TagRelations は Cascade で削除）
        _ = context.Items.Remove(quoteItem);
    }

    private static (int Index, string TextToReplace) FindSelectedText(string content, string selectedText)
    {
        int index = content.IndexOf(selectedText, StringComparison.Ordinal);
        if (index >= 0)
        {
            return (index, selectedText);
        }

        string crlfSelected = selectedText.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal);
        int crlfIndex = content.IndexOf(crlfSelected, StringComparison.Ordinal);
        if (crlfIndex >= 0)
        {
            return (crlfIndex, crlfSelected);
        }

        string lfSelected = selectedText.Replace("\r\n", "\n", StringComparison.Ordinal);
        int lfIndex = content.IndexOf(lfSelected, StringComparison.Ordinal);
        return lfIndex >= 0 ? (lfIndex, lfSelected) : (-1, selectedText);
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
            return Result.Fail("分割リクエストが見つかりません。");
        }

        if (request.OwnerUserId != ownerUserId)
        {
            return Result.Fail("このリクエストを却下する権限がありません。");
        }

        if (request.Status != TradeStatus.Proposed)
        {
            return Result.Fail($"申請中ではないリクエストは却下できません（現在の状態: {request.Status}）。");
        }

        request.Status = TradeStatus.Rejected;
        request.RejectReason = rejectReason;
        request.UpdatedDate = DateTime.UtcNow;

        _ = await context.SaveChangesAsync(cancellationToken);

        _notificationService.NotifyNotificationsChanged();

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
            return Result.Fail("分割リクエストが見つかりません。");
        }

        if (request.RequesterUserId != requesterUserId)
        {
            return Result.Fail("このリクエストを取り下げる権限がありません。");
        }

        if (request.Status != TradeStatus.Proposed)
        {
            return Result.Fail($"申請中ではないリクエストは取り下げできません（現在の状態: {request.Status}）。");
        }

        request.Status = TradeStatus.Canceled;
        request.UpdatedDate = DateTime.UtcNow;

        _ = await context.SaveChangesAsync(cancellationToken);

        _notificationService.NotifyNotificationsChanged();

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