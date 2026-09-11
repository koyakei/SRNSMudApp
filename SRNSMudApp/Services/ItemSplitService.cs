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

        if (string.IsNullOrEmpty(originalItem.Content) || !originalItem.Content.Contains(selectedText, StringComparison.Ordinal))
        {
            return Result.Fail<ItemSplitRequest>("選択されたテキストが元アイテムの本文に含まれていません。");
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

        var request = new ItemSplitRequest
        {
            OriginalItemId = originalItemId,
            RequesterUserId = requesterUserId,
            OwnerUserId = originalItem.OwnerId,
            SelectedText = selectedText,
            Status = TradeStatus.Proposed,
            OwnerId = requesterUserId, // BaseEntity.OwnerId は作成者 (Requester)
            CreatedDate = DateTime.UtcNow,
            UpdatedDate = DateTime.UtcNow
        };

        _ = context.ItemSplitRequests.Add(request);
        _ = await context.SaveChangesAsync(cancellationToken);

        _notificationService.NotifyNotificationsChanged();

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

        int index = originalItem.Content.IndexOf(selectedText, StringComparison.Ordinal);
        if (index < 0)
        {
            return Result.Fail<Item>("選択されたテキストが元アイテムの本文に含まれていません。");
        }

        Item newItem = await CreateSplitItemAsync(context, originalItem, selectedText, index, cancellationToken);
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

        int index = originalItem.Content.IndexOf(request.SelectedText, StringComparison.Ordinal);
        if (index < 0)
        {
            return Result.Fail<Item>("元アイテムの本文が更新されたため、指定されたテキストが見つかりません。");
        }

        Item newItem = await CreateSplitItemAsync(context, originalItem, request.SelectedText, index, cancellationToken);

        // 元アイテム本文の該当テキストを /ItemDetail/{newItem.Id} リンクに置換
        originalItem.UpdatedDate = DateTime.UtcNow;

        // 3. リクエスト状態更新
        request.Status = TradeStatus.Executed;
        request.CreatedItemId = newItem.Id;
        request.UpdatedDate = DateTime.UtcNow;

        _ = await context.SaveChangesAsync(cancellationToken);

        _notificationService.NotifyNotificationsChanged();

        return Result.Ok(newItem);
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
        var newItem = new Item
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