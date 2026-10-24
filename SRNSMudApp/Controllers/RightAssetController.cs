namespace SRNSMudApp.Controllers;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SRNSMudApp.Client.Models.Api;
using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Services;

/// <summary>
/// RightAsset の状況・保有データに関する API を提供するコントローラー。
/// </summary>
[ApiController]
[Route("api/right-asset")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1508:Avoid dead code", Justification = "Result<T> union type pattern matching")]
public sealed class RightAssetController(
    IRightAssetDataProvider rightAssetDataProvider,
    IRightAssetPurchaseService? rightAssetPurchaseService = null) : ControllerBase
{
    private readonly IRightAssetDataProvider _rightAssetDataProvider =
        rightAssetDataProvider ?? throw new ArgumentNullException(nameof(rightAssetDataProvider));
    private readonly IRightAssetPurchaseService? _rightAssetPurchaseService = rightAssetPurchaseService;

    /// <summary>
    /// RightAsset が発行されている代表的なタグ一覧を取得します。
    /// </summary>
    /// <param name="count">取得最大件数（既定値: 10）。</param>
    /// <param name="cancellationToken">キャンセレーショントークン。</param>
    /// <returns>代表タグ概要の DTO 一覧。</returns>
    [HttpGet("top-tags")]
    [AllowAnonymous]
    public async Task<ActionResult<IReadOnlyList<TagRightAssetSummaryDto>>> GetTopTags(
        [FromQuery] int count = 10,
        CancellationToken cancellationToken = default)
    {
        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        bool isAdmin = User?.IsInRole("Admin") ?? false;

        // 非公開タグは閲覧権限のあるユーザーにのみ返却する (Issue 2-8)
        IReadOnlyList<TagRightAssetSummary> summaries = string.IsNullOrEmpty(currentUserId) && !isAdmin
            ? await _rightAssetDataProvider.GetTopTagsWithRightAssetsAsync(count, cancellationToken)
            : await _rightAssetDataProvider.GetTopTagsWithRightAssetsAsync(count, currentUserId, isAdmin, cancellationToken);

        var dtos = summaries.Select(s => new TagRightAssetSummaryDto(
            TagId: s.TagId,
            TagName: s.TagName,
            TagContent: s.TagContent,
            TotalAmount: s.TotalAmount,
            HolderCount: s.HolderCount
        )).ToList();

        return Ok(dtos);
    }

    /// <summary>
    /// 指定されたタグの RightAsset 保有状況および詳細アセット一覧を取得します。
    /// </summary>
    /// <param name="tagId">対象タグID。</param>
    /// <param name="cancellationToken">キャンセレーショントークン。</param>
    /// <returns>RightAsset 概要レスポンス DTO。タグが存在しないか権限がない場合は NotFound。</returns>
    [HttpGet("overview/{tagId:int}")]
    [AllowAnonymous]
    public async Task<ActionResult<RightAssetOverviewResponse>> GetOverview(
        int tagId,
        CancellationToken cancellationToken = default)
    {
        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        bool isAdmin = User?.IsInRole("Admin") ?? false;

        // 非公開タグの認可チェック: 権限がない場合は 404 NotFound を返却し存在漏洩を防ぐ (Issue 2-8)
        RightAssetOverviewData? overview = string.IsNullOrEmpty(currentUserId) && !isAdmin
            ? await _rightAssetDataProvider.GetRightAssetOverviewByTagIdAsync(tagId, cancellationToken)
            : await _rightAssetDataProvider.GetRightAssetOverviewByTagIdAsync(tagId, currentUserId, isAdmin, cancellationToken);

        if (overview is null)
        {
            return NotFound();
        }

        var holders = overview.Holders.Select(h => new RightAssetHolderDto(
            UserId: h.UserId,
            UserName: h.UserName,
            TotalAmount: h.TotalAmount,
            ActiveAssetCount: h.ActiveAssetCount,
            BurnedAmount: h.BurnedAmount,
            BurnedAssetCount: h.BurnedAssetCount,
            LastUpdated: h.LastUpdated
        )).ToList();

        var assets = overview.Assets.Select(a => new RightAssetItemDto(
            Id: a.Id,
            OwnerId: a.OwnerId,
            OwnerName: a.OwnerName,
            Amount: a.Amount,
            IsBurned: a.IsBurned,
            StatusSummary: a.StatusSummary,
            CreatedDate: a.CreatedDate,
            UpdatedDate: a.UpdatedDate
        )).ToList();

        RightAssetOverviewResponse response = new(
            TagId: overview.Tag.Id,
            TagName: overview.Tag.Name,
            TagContent: overview.Tag.Content,
            TagWeight: overview.Tag.CachedWeight,
            IsSystem: overview.Tag.IsSystem,
            TotalHoldersCount: overview.TotalHoldersCount,
            TotalActiveAmount: overview.TotalActiveAmount,
            TotalActiveAssetsCount: overview.TotalActiveAssetsCount,
            TotalBurnedAmount: overview.TotalBurnedAmount,
            Holders: holders,
            Assets: assets
        );

        return Ok(response);
    }

    /// <summary>
    /// サポートされている JPYC ネットワーク一覧を取得します。
    /// </summary>
    [HttpGet("networks")]
    [AllowAnonymous]
    public ActionResult<IReadOnlyList<JpycNetworkDto>> GetNetworks()
    {
        if (_rightAssetPurchaseService == null)
        {
            return StatusCode(Microsoft.AspNetCore.Http.StatusCodes.Status501NotImplemented);
        }

        IReadOnlyList<JpycNetworkInfo> networks = _rightAssetPurchaseService.GetSupportedNetworks();
        var dtos = networks.Select(n => new JpycNetworkDto(
            Name: n.Name,
            DisplayName: n.DisplayName,
            ChainId: n.ChainId,
            ContractAddress: n.ContractAddress,
            FaucetUrl: n.FaucetUrl,
            ExplorerUrl: n.ExplorerUrl,
            IsTestnet: n.IsTestnet,
            IsRecommended: n.IsRecommended
        )).ToList();

        return Ok(dtos);
    }

    /// <summary>
    /// ユーザー専用の JPYC 受取ウォレットを取得または新規発行します。
    /// </summary>
    [HttpGet("deposit-wallet")]
    [Authorize]
    public async Task<ActionResult<DepositWalletDto>> GetDepositWallet(
        [FromQuery] string networkName = "polygon-amoy",
        CancellationToken cancellationToken = default)
    {
        string? currentUserId = User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized("ウォレットを取得するにはログインが必要です。");
        }

        if (_rightAssetPurchaseService == null)
        {
            return StatusCode(Microsoft.AspNetCore.Http.StatusCodes.Status501NotImplemented);
        }

        UserDepositWalletDto wallet = await _rightAssetPurchaseService.GetOrCreateUserDepositWalletAsync(
            currentUserId, networkName, cancellationToken);

        return Ok(new DepositWalletDto(
            UserId: wallet.UserId,
            NetworkName: wallet.NetworkName,
            DepositAddress: wallet.DepositAddress,
            CreatedAt: wallet.CreatedAt
        ));
    }

    /// <summary>
    /// テストネット用にユーザー専用受取アドレスへの JPYC 送金をシミュレートします。
    /// </summary>
    [HttpPost("simulate-deposit")]
    [Authorize]
    public async Task<ActionResult<string>> SimulateDeposit(
        [FromBody] SimulateDepositRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string? currentUserId = User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized("送金シミュレーションにはログインが必要です。");
        }

        if (_rightAssetPurchaseService == null)
        {
            return StatusCode(Microsoft.AspNetCore.Http.StatusCodes.Status501NotImplemented);
        }

        string txHash = await _rightAssetPurchaseService.SimulateDepositAsync(
            currentUserId, request.NetworkName, request.AmountJpyc, cancellationToken);

        return Ok(txHash);
    }

    /// <summary>
    /// JPYC トランザクションを検証し、RightAsset を購入・発行します。
    /// </summary>
    [HttpPost("purchase")]
    [Authorize]
    public async Task<ActionResult<PurchaseRightAssetResultDto>> PurchaseRightAsset(
        [FromBody] PurchaseRightAssetRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string? currentUserId = User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized("RightAsset を購入するにはログインが必要です。");
        }

        if (_rightAssetPurchaseService == null)
        {
            return StatusCode(Microsoft.AspNetCore.Http.StatusCodes.Status501NotImplemented);
        }

        JpycPurchaseRequestDto jpycRequest = new(
            RequestedTagId: request.RequestedTagId,
            Amount: request.Amount,
            UnitPriceJpyc: request.UnitPriceJpyc,
            NetworkName: request.NetworkName,
            TransactionHash: request.TransactionHash,
            SenderWalletAddress: request.SenderWalletAddress
        );

        SRNSMudApp.Models.Unions.Result<RightAsset> result =
            await _rightAssetPurchaseService.PurchaseRightAssetWithJpycAsync(
                currentUserId, jpycRequest, cancellationToken);

        // switch 式による Result<RightAsset> のハンドリング
        return result switch
        {
            SRNSMudApp.Models.Unions.Success<RightAsset> success => Ok(new PurchaseRightAssetResultDto(
                RightAssetId: success.Value.Id,
                TagId: success.Value.TargetTagId,
                TagName: success.Value.TargetTag?.Name ?? string.Empty,
                Amount: success.Value.Amount,
                TotalJpycAmount: jpycRequest.TotalJpycAmount,
                NetworkName: jpycRequest.NetworkName,
                TransactionHash: jpycRequest.TransactionHash ?? string.Empty,
                DepositAddress: jpycRequest.SenderWalletAddress ?? string.Empty,
                PurchasedAt: DateTime.UtcNow
            )),
            SRNSMudApp.Models.Unions.Failure failure => BadRequest(failure.ErrorMessage),
            _ => StatusCode(500, "RightAsset の購入中に予期しないエラーが発生しました。")
        };
    }
}