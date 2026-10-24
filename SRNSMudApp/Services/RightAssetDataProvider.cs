#pragma warning disable CA1848, CA1873

#region

using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Resources;

#endregion

namespace SRNSMudApp.Services;

/// <summary>
///     RightAsset の状況閲覧・集計データを提供する DataProvider インターフェース。
///     Blazor コンポーネントからの直接的な DbContext 依存を分離する。
/// </summary>
public interface IRightAssetDataProvider
{
    /// <summary>
    ///     指定されたタグにおける RightAsset の保有状況（誰がどれだけ持っているか等）を取得する。
    /// </summary>
    /// <param name="tagId">対象タグID。</param>
    /// <param name="cancellationToken">キャンセラレーショントークン。</param>
    /// <returns>RightAsset の概要データ。タグが存在しない場合は null。</returns>
    Task<RightAssetOverviewData?> GetRightAssetOverviewByTagIdAsync(int tagId, CancellationToken cancellationToken = default) =>
        GetRightAssetOverviewByTagIdAsync(tagId, null, false, cancellationToken);

    /// <summary>
    ///     指定されたタグにおける RightAsset の保有状況をユーザー可視性・権限を考慮して取得する。
    /// </summary>
    /// <param name="tagId">対象タグID。</param>
    /// <param name="currentUserId">現在のユーザーID。</param>
    /// <param name="isAdmin">管理者フラグ。</param>
    /// <param name="cancellationToken">キャンセレーショントークン。</param>
    /// <returns>RightAsset の概要データ。タグが存在しないか権限がない場合は null。</returns>
    Task<RightAssetOverviewData?> GetRightAssetOverviewByTagIdAsync(
        int tagId,
        string? currentUserId,
        bool isAdmin = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     RightAsset が発行されている代表的なタグ一覧を取得する（タグ未選択時のクイック選択用）。
    /// </summary>
    /// <param name="count">取得最大件数。</param>
    /// <param name="cancellationToken">キャンセラレーショントークン。</param>
    /// <returns>タグごとの RightAsset 概要一覧。</returns>
    Task<IReadOnlyList<TagRightAssetSummary>> GetTopTagsWithRightAssetsAsync(int count = 10, CancellationToken cancellationToken = default) =>
        GetTopTagsWithRightAssetsAsync(count, null, false, cancellationToken);

    /// <summary>
    ///     RightAsset が発行されている代表的なタグ一覧をユーザー可視性・権限を考慮して取得する。
    /// </summary>
    /// <param name="count">取得最大件数。</param>
    /// <param name="currentUserId">現在のユーザーID。</param>
    /// <param name="isAdmin">管理者フラグ。</param>
    /// <param name="cancellationToken">キャンセレーショントークン。</param>
    /// <returns>タグごとの RightAsset 概要一覧。</returns>
    Task<IReadOnlyList<TagRightAssetSummary>> GetTopTagsWithRightAssetsAsync(
        int count,
        string? currentUserId,
        bool isAdmin = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     ログインユーザーが所持する有効な RightAsset 一覧（対価アセット選択用）を取得する。
    /// </summary>
    Task<IReadOnlyList<UserAvailableRightAssetDto>> GetAvailableRightAssetsForUserAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     タグの操作権限（RightAsset）のリクエストを送信する。
    /// </summary>
    Task<Result<bool>> SubmitPermissionRequestAsync(string requesterUserId, TagPermissionRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>
    ///     タグ操作権限リクエストを承認し、RightAsset を移転する。
    /// </summary>
    Task<Result<bool>> ApprovePermissionRequestAsync(int itemId, string currentUserId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     タグ操作権限リクエストを却下する。
    /// </summary>
    Task<Result<bool>> RejectPermissionRequestAsync(int itemId, string currentUserId, string? comment = null, CancellationToken cancellationToken = default);
}

/// <summary>
///     RightAsset の保有状況データを集計・取得する DataProvider 実装。
/// </summary>
public class RightAssetDataProvider(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    INotificationService? notificationService = null,
    ILogger<RightAssetDataProvider>? logger = null) : IRightAssetDataProvider
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
    private readonly INotificationService? _notificationService = notificationService;
    private readonly ILogger<RightAssetDataProvider>? _logger = logger;

    /// <inheritdoc />
    public Task<RightAssetOverviewData?> GetRightAssetOverviewByTagIdAsync(int tagId, CancellationToken cancellationToken = default) =>
        GetRightAssetOverviewByTagIdAsync(tagId, null, false, cancellationToken);

    /// <inheritdoc />
    public async Task<RightAssetOverviewData?> GetRightAssetOverviewByTagIdAsync(
        int tagId,
        string? currentUserId,
        bool isAdmin = false,
        CancellationToken cancellationToken = default)
    {
        await using ApplicationDbContext dbContext = await _dbFactory.CreateDbContextAsync(cancellationToken);

        Tag? tag = await dbContext.Tags
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tagId, cancellationToken);

        if (tag is null)
        {
            return null;
        }

        // 非公開タグの認可チェック: 閲覧権限のないユーザーには RightAsset 保有者情報・概要を秘匿する (Issue 2-8)
        if (!await tag.IsTagVisibleToUserAsync(dbContext, currentUserId, isAdmin))
        {
            return null;
        }

        List<RightAsset> assets = await dbContext.RightAssets
            .AsNoTracking()
            .Include(a => a.Owner)
            .Where(a => a.TargetTagId == tagId)
            .OrderByDescending(a => a.CreatedDate)
            .ToListAsync(cancellationToken);

        // ユーザーごとの保有状況集計
        var holders = assets
            .GroupBy(a => a.OwnerId)
            .Select(group =>
            {
                var activeAssets = group.Where(a => !a.IsBurned && a.Amount > 0).ToList();
                var burnedAssets = group.Where(a => a.IsBurned).ToList();
                var owner = group.FirstOrDefault()?.Owner;
                var userName = owner?.UserName;
                var latestUpdate = group.Max(a => (DateTime?)a.UpdatedDate);

                return new RightAssetHolderSummary(
                    UserId: group.Key,
                    UserName: userName,
                    TotalAmount: activeAssets.Sum(a => a.Amount),
                    ActiveAssetCount: activeAssets.Count,
                    BurnedAmount: burnedAssets.Sum(a => a.Amount),
                    BurnedAssetCount: burnedAssets.Count,
                    LastUpdated: latestUpdate
                );
            })
            .OrderByDescending(h => h.TotalAmount)
            .ThenByDescending(h => h.ActiveAssetCount)
            .ToList();

        // 個別アセット明細 DTO
        var assetDtos = assets.Select(a =>
        {
            var status = a.IsBurned
                ? "燃焼済 (Burned)"
                : a.Amount > 0
                    ? "有効"
                    : "残高0";

            return new RightAssetDetailDto(
                Id: a.Id,
                OwnerId: a.OwnerId,
                OwnerName: a.Owner?.UserName,
                Amount: a.Amount,
                IsBurned: a.IsBurned,
                StatusSummary: status,
                CreatedDate: a.CreatedDate,
                UpdatedDate: a.UpdatedDate
            );
        }).ToList();

        var totalHoldersCount = holders.Count(h => h.TotalAmount > 0);
        var totalActiveAmount = assets.Where(a => !a.IsBurned && a.Amount > 0).Sum(a => a.Amount);
        var totalActiveAssetsCount = assets.Count(a => !a.IsBurned && a.Amount > 0);
        var totalBurnedAmount = assets.Where(a => a.IsBurned).Sum(a => a.Amount);

        return new RightAssetOverviewData(
            Tag: tag,
            TotalHoldersCount: totalHoldersCount,
            TotalActiveAmount: totalActiveAmount,
            TotalActiveAssetsCount: totalActiveAssetsCount,
            TotalBurnedAmount: totalBurnedAmount,
            Holders: holders,
            Assets: assetDtos
        );
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<TagRightAssetSummary>> GetTopTagsWithRightAssetsAsync(int count = 10, CancellationToken cancellationToken = default) =>
        GetTopTagsWithRightAssetsAsync(count, null, false, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<TagRightAssetSummary>> GetTopTagsWithRightAssetsAsync(
        int count,
        string? currentUserId,
        bool isAdmin = false,
        CancellationToken cancellationToken = default)
    {
        await using ApplicationDbContext dbContext = await _dbFactory.CreateDbContextAsync(cancellationToken);

        // 有効な RightAsset を持つタグをグループ化して集計
        var topTagIds = await dbContext.RightAssets
            .AsNoTracking()
            .Where(a => !a.IsBurned && a.Amount > 0)
            .GroupBy(a => a.TargetTagId)
            .Select(g => new
            {
                TagId = g.Key,
                TotalAmount = g.Sum(a => a.Amount),
                HolderCount = g.Select(a => a.OwnerId).Distinct().Count()
            })
            .OrderByDescending(x => x.TotalAmount)
            .Take(count)
            .ToListAsync(cancellationToken);

        if (topTagIds.Count == 0)
        {
            // 有効なアセットがない場合は、全 RightAssets から取得（燃焼済み含む）
            var fallbackTagIds = await dbContext.RightAssets
                .AsNoTracking()
                .GroupBy(a => a.TargetTagId)
                .Select(g => new
                {
                    TagId = g.Key,
                    TotalAmount = g.Sum(a => a.Amount),
                    HolderCount = g.Select(a => a.OwnerId).Distinct().Count()
                })
                .OrderByDescending(x => x.HolderCount)
                .Take(count)
                .ToListAsync(cancellationToken);

            return fallbackTagIds.Count == 0
                ? []
                : await MapToTagRightAssetSummariesAsync(
                dbContext,
                fallbackTagIds.Select(x => (x.TagId, x.TotalAmount, x.HolderCount)).ToList(),
                currentUserId,
                isAdmin,
                cancellationToken);
        }

        return await MapToTagRightAssetSummariesAsync(
            dbContext,
            topTagIds.Select(x => (x.TagId, x.TotalAmount, x.HolderCount)).ToList(),
            currentUserId,
            isAdmin,
            cancellationToken);
    }

    private static async Task<IReadOnlyList<TagRightAssetSummary>> MapToTagRightAssetSummariesAsync(
        ApplicationDbContext dbContext,
        List<(int TagId, int TotalAmount, int HolderCount)> aggregatedTags,
        string? currentUserId,
        bool isAdmin,
        CancellationToken cancellationToken)
    {
        var ids = aggregatedTags.Select(x => x.TagId).ToList();
        var tags = await dbContext.Tags
            .AsNoTracking()
            .Where(t => ids.Contains(t.Id))
            .WhereVisibleToUser(dbContext, currentUserId, isAdmin)
            .ToDictionaryAsync(t => t.Id, cancellationToken);

        return aggregatedTags
            .Where(x => tags.ContainsKey(x.TagId))
            .Select(x => new TagRightAssetSummary(
                TagId: x.TagId,
                TagName: tags[x.TagId].Name,
                TagContent: tags[x.TagId].Content,
                TotalAmount: x.TotalAmount,
                HolderCount: x.HolderCount
            ))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserAvailableRightAssetDto>> GetAvailableRightAssetsForUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return [];
        }

        await using ApplicationDbContext dbContext = await _dbFactory.CreateDbContextAsync(cancellationToken);

        return await dbContext.RightAssets
            .AsNoTracking()
            .Include(a => a.TargetTag)
            .Where(a => a.OwnerId == userId && !a.IsBurned && a.Amount > 0)
            .OrderBy(a => a.TargetTag.Name)
            .Select(a => new UserAvailableRightAssetDto(
                a.Id,
                a.TargetTagId,
                a.TargetTag.Name,
                a.Amount))
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result<bool>> SubmitPermissionRequestAsync(string requesterUserId, TagPermissionRequestDto request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(requesterUserId))
        {
            return Result.Fail<bool>("リクエスト送信ユーザーが指定されていません。");
        }

        if (string.IsNullOrWhiteSpace(request.TargetUserId))
        {
            return Result.Fail<bool>("リクエスト対象ユーザーが指定されていません。");
        }

        if (string.Equals(requesterUserId, request.TargetUserId, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Fail<bool>("自分自身に操作権限をリクエストすることはできません。");
        }

        if (request.RequestedAmount <= 0)
        {
            return Result.Fail<bool>("リクエスト数量は1以上を指定してください。");
        }

        await using ApplicationDbContext dbContext = await _dbFactory.CreateDbContextAsync(cancellationToken);

        Tag? tag = await dbContext.Tags
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == request.RequestedTagId, cancellationToken);
        if (tag is null)
        {
            return Result.Fail<bool>("対象のタグが見つかりません。");
        }

        ApplicationUser? targetUser = await dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == request.TargetUserId, cancellationToken);
        if (targetUser is null)
        {
            return Result.Fail<bool>("対象ユーザーが見つかりません。");
        }

        RightAsset? offeredAsset = null;
        if (request.OfferedRightAssetId.HasValue)
        {
            if (request.OfferedAmount <= 0)
            {
                return Result.Fail<bool>("提供する対価の数量は1以上を指定してください。");
            }

            offeredAsset = await dbContext.RightAssets
                .AsNoTracking()
                .Include(a => a.TargetTag)
                .FirstOrDefaultAsync(a => a.Id == request.OfferedRightAssetId.Value && a.OwnerId == requesterUserId && !a.IsBurned, cancellationToken);

            if (offeredAsset is null)
            {
                return Result.Fail<bool>("対価として指定されたアセットが存在しないか、すでに消費されています。");
            }

            if (offeredAsset.Amount < request.OfferedAmount)
            {
                return Result.Fail<bool>($"提供数量 ({request.OfferedAmount}) がアセットの保有残高 ({offeredAsset.Amount}) を超えています。");
            }
        }

        var offeredSummary = offeredAsset != null
            ? $"（対価: {offeredAsset.TargetTag.Name} x{request.OfferedAmount}）"
            : "（無償リクエスト）";

        var itemContent = $"【タグ操作権限リクエスト】\n" +
                          $"タグ「{tag.Name}」の操作権限 {request.RequestedAmount} をリクエストしました。{offeredSummary}" +
                          (string.IsNullOrWhiteSpace(request.Message) ? "" : $"\n\nメッセージ:\n{request.Message.Trim()}");

        var payload = new TagPermissionRequestPayload(
            RequestedTagId: request.RequestedTagId,
            RequestedTagName: tag.Name,
            RequestedAmount: request.RequestedAmount,
            OfferedRightAssetId: request.OfferedRightAssetId,
            OfferedTagName: offeredAsset?.TargetTag?.Name,
            OfferedAmount: request.OfferedAmount,
            Message: request.Message,
            Status: TradeStatus.Proposed);

        var requestItem = new Item
        {
            OwnerId = requesterUserId,
            Content = itemContent,
            ItemKindJson = JsonSerializer.Serialize(payload),
            NotificationRecipients =
            [
                new ItemReplyNotificationRecipient
                {
                    RecipientUserId = request.TargetUserId
                }
            ]
        };

        dbContext.Items.Add(requestItem);
        await dbContext.SaveChangesAsync(cancellationToken);

        _logger?.LogInformation(
            "User {RequesterUserId} submitted permission request for Tag {TagId} ({TagName}) to User {TargetUserId} with amount {Amount}.",
            requesterUserId, request.RequestedTagId, tag.Name, request.TargetUserId, request.RequestedAmount);

        _notificationService?.NotifyNotificationsChanged();

        return Result.Ok(true);
    }

    /// <inheritdoc />
    public async Task<Result<bool>> ApprovePermissionRequestAsync(int itemId, string currentUserId, CancellationToken cancellationToken = default)
    {
        await using ApplicationDbContext dbContext = await _dbFactory.CreateDbContextAsync(cancellationToken);

        Item? item = await dbContext.Items
            .Include(i => i.NotificationRecipients)
            .Include(i => i.Owner)
            .FirstOrDefaultAsync(i => i.Id == itemId, cancellationToken);

        if (item is null)
        {
            return Result.Fail<bool>(ErrorMessages.RequestNotFound);
        }

        bool isTargetUser = item.NotificationRecipients.Any(r =>
            string.Equals(r.RecipientUserId, currentUserId, StringComparison.OrdinalIgnoreCase));
        if (!isTargetUser)
        {
            return Result.Fail<bool>(ErrorMessages.NotAuthorizedToApprove);
        }

        TagPermissionRequestPayload? payload = ParsePermissionPayload(item);
        if (payload is null)
        {
            return Result.Fail<bool>("リクエストデータの解析に失敗しました。");
        }

        if (payload.Status != TradeStatus.Proposed)
        {
            return Result.Fail<bool>(ErrorMessages.RequestAlreadyProcessed);
        }

        if (payload.RequestedTagId <= 0)
        {
            return Result.Fail<bool>(ErrorMessages.TargetTagNotFound);
        }

        // TagId のみで対象タグを取得する
        Tag? tag = await dbContext.Tags.FirstOrDefaultAsync(t => t.Id == payload.RequestedTagId, cancellationToken);
        if (tag is null)
        {
            return Result.Fail<bool>(ErrorMessages.TargetTagNotFound);
        }

        int targetTagId = tag.Id;

        // 承認者（currentUserId）の要求タグ保有残高を確認
        List<RightAsset> approverAssets = await dbContext.RightAssets
            .Where(a => a.OwnerId == currentUserId && a.TargetTagId == targetTagId && !a.IsBurned && a.Amount > 0)
            .OrderBy(a => a.Amount)
            .ToListAsync(cancellationToken);

        int totalApproverAmount = approverAssets.Sum(a => a.Amount);
        if (totalApproverAmount < payload.RequestedAmount)
        {
            // 自分のタグ（タグオーナー自身）である場合、不足分の操作権限を新規発行して承認する
            if (string.Equals(tag.OwnerId, currentUserId, StringComparison.OrdinalIgnoreCase))
            {
                int neededAmount = payload.RequestedAmount - totalApproverAmount;
                var mintedAsset = new RightAsset
                {
                    OwnerId = currentUserId,
                    TargetTagId = targetTagId,
                    Amount = neededAmount,
                    IsBurned = false,
                    Status = new NotBurned()
                };
                dbContext.RightAssets.Add(mintedAsset);
                approverAssets.Add(mintedAsset);
                totalApproverAmount += neededAmount;

                _logger?.LogInformation(
                    "Minted {MintedAmount} additional RightAsset for Tag {TagId} to owner {OwnerId}.",
                    neededAmount, targetTagId, currentUserId);
            }
            else
            {
                return Result.Fail<bool>(ErrorMessages.FormatInsufficientPermissionBalance(payload.RequestedAmount, totalApproverAmount));
            }
        }

        // 対価アセットの確認（指定されている場合）
        RightAsset? offeredAsset = null;
        if (payload.OfferedRightAssetId.HasValue && payload.OfferedAmount > 0)
        {
            offeredAsset = await dbContext.RightAssets
                .FirstOrDefaultAsync(a => a.Id == payload.OfferedRightAssetId.Value && a.OwnerId == item.OwnerId && !a.IsBurned, cancellationToken);

            if (offeredAsset is null || offeredAsset.Amount < payload.OfferedAmount)
            {
                return Result.Fail<bool>("相手の対価アセットが不足しているか無効なため、承認できません。");
            }
        }

        // 1. 承認者から要求量を減算し、リクエスト送信者に要求量を付与
        TransferRequestedRightAsset(dbContext, approverAssets, payload.RequestedAmount, item.OwnerId, targetTagId);

        // 2. 対価アセットの移転（存在する場合）
        TransferOfferedRightAsset(dbContext, offeredAsset, payload.OfferedAmount, currentUserId);

        // 3. ステータス更新
        var updatedPayload = payload with { Status = TradeStatus.Executed, RequestedTagId = targetTagId };
        item.ItemKindJson = JsonSerializer.Serialize(updatedPayload);
        item.UpdatedDate = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        _logger?.LogInformation(
            "User {ApproverUserId} approved permission request {ItemId} for Tag {TagId} from User {RequesterUserId}.",
            currentUserId, itemId, targetTagId, item.OwnerId);

        if (_notificationService != null)
        {
            await _notificationService.NotifyUserAsync(
                item.OwnerId,
                "タグ操作権限リクエスト承認",
                $"タグ「{payload.RequestedTagName}」の操作権限リクエストが承認されました。",
                targetTagId > 0 ? $"/TagDetail/{targetTagId}" : "/notifications",
                cancellationToken);
            _notificationService.NotifyNotificationsChanged();
        }

        return Result.Ok(true);
    }

    /// <inheritdoc />
    public async Task<Result<bool>> RejectPermissionRequestAsync(int itemId, string currentUserId, string? comment = null, CancellationToken cancellationToken = default)
    {
        await using ApplicationDbContext dbContext = await _dbFactory.CreateDbContextAsync(cancellationToken);

        Item? item = await dbContext.Items
            .Include(i => i.NotificationRecipients)
            .FirstOrDefaultAsync(i => i.Id == itemId, cancellationToken);

        if (item is null)
        {
            return Result.Fail<bool>(ErrorMessages.RequestNotFound);
        }

        bool isTargetUser = item.NotificationRecipients.Any(r =>
            string.Equals(r.RecipientUserId, currentUserId, StringComparison.OrdinalIgnoreCase));
        if (!isTargetUser)
        {
            return Result.Fail<bool>(ErrorMessages.NotAuthorizedToReject);
        }

        TagPermissionRequestPayload? payload = ParsePermissionPayload(item);
        if (payload is null)
        {
            return Result.Fail<bool>("リクエストデータの解析に失敗しました。");
        }

        if (payload.Status != TradeStatus.Proposed)
        {
            return Result.Fail<bool>(ErrorMessages.RequestAlreadyProcessed);
        }

        int targetTagId = payload.RequestedTagId;
        var updatedPayload = payload with { Status = TradeStatus.Rejected, RejectReason = comment, RequestedTagId = targetTagId };
        item.ItemKindJson = JsonSerializer.Serialize(updatedPayload);
        item.UpdatedDate = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        _logger?.LogInformation(
            "User {CurrentUserId} rejected permission request {ItemId} for Tag {TagId}. Comment: {Comment}",
            currentUserId, itemId, targetTagId, comment);

        if (_notificationService != null)
        {
            var reasonSuffix = string.IsNullOrWhiteSpace(comment) ? "" : $"\n理由: {comment}";
            await _notificationService.NotifyUserAsync(
                item.OwnerId,
                "タグ操作権限リクエスト却下",
                $"タグ「{payload.RequestedTagName}」の操作権限リクエストが却下されました。{reasonSuffix}",
                "/notifications",
                cancellationToken);
            _notificationService.NotifyNotificationsChanged();
        }

        return Result.Ok(true);
    }

    private static void TransferRequestedRightAsset(
        ApplicationDbContext dbContext,
        List<RightAsset> approverAssets,
        int requestedAmount,
        string requesterUserId,
        int targetTagId)
    {
        int remainingToDeduct = requestedAmount;
        foreach (var asset in approverAssets)
        {
            if (remainingToDeduct <= 0) break;
            if (asset.Amount <= remainingToDeduct)
            {
                remainingToDeduct -= asset.Amount;
                asset.Amount = 0;
                asset.IsBurned = true;
                asset.Status = new Burned(DateTime.UtcNow);
            }
            else
            {
                asset.Amount -= remainingToDeduct;
                remainingToDeduct = 0;
            }
        }

        var newAssetForRequester = new RightAsset
        {
            OwnerId = requesterUserId,
            TargetTagId = targetTagId,
            Amount = requestedAmount,
            IsBurned = false,
            Status = new NotBurned()
        };
        dbContext.RightAssets.Add(newAssetForRequester);
    }

    private static void TransferOfferedRightAsset(
        ApplicationDbContext dbContext,
        RightAsset? offeredAsset,
        int offeredAmount,
        string approverUserId)
    {
        if (offeredAsset is null || offeredAmount <= 0) return;

        if (offeredAsset.Amount <= offeredAmount)
        {
            offeredAsset.Amount = 0;
            offeredAsset.IsBurned = true;
            offeredAsset.Status = new Burned(DateTime.UtcNow);
        }
        else
        {
            offeredAsset.Amount -= offeredAmount;
        }

        var newAssetForApprover = new RightAsset
        {
            OwnerId = approverUserId,
            TargetTagId = offeredAsset.TargetTagId,
            Amount = offeredAmount,
            IsBurned = false,
            Status = new NotBurned()
        };
        dbContext.RightAssets.Add(newAssetForApprover);
    }

    /// <summary>
    ///     通知アイテムから権限リクエストのメタデータを解析する。
    ///     ItemKindJson による構造化メタデータを優先し、未設定の場合は Content テキストから解析する。
    /// </summary>
    /// <param name="item">解析対象のアイテムエンティティ。</param>
    /// <returns>解析されたリクエストペイロード。権限リクエストに該当しない場合は null。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="item"/> が null の場合にスローされます。</exception>
    public static TagPermissionRequestPayload? ParsePermissionPayload(Item item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (!string.IsNullOrWhiteSpace(item.ItemKindJson))
        {
            try
            {
                var payload = JsonSerializer.Deserialize<TagPermissionRequestPayload>(item.ItemKindJson);
                if (payload != null && (payload.RequestedTagId > 0 || !string.IsNullOrWhiteSpace(payload.RequestedTagName)))
                {
                    return payload;
                }
            }
            catch (JsonException)
            {
                // Fallback to content parsing
            }
        }

        if (!string.IsNullOrWhiteSpace(item.Content) && item.Content.Contains("【タグ操作権限リクエスト】", StringComparison.Ordinal))
        {
            var match = System.Text.RegularExpressions.Regex.Match(item.Content, @"タグ「(.+?)」の操作権限\s+(\d+)\s+をリクエストしました。");
            if (match.Success)
            {
                var tagName = match.Groups[1].Value;
                _ = int.TryParse(match.Groups[2].Value, out var requestedAmount);
                return new TagPermissionRequestPayload(
                    RequestedTagId: 0,
                    RequestedTagName: tagName,
                    RequestedAmount: requestedAmount > 0 ? requestedAmount : 1,
                    Status: TradeStatus.Proposed);
            }
        }

        return null;
    }
}