namespace SRNSMudApp.Controllers;

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SRNSMudApp.Client.Models.Api;
using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;
using SRNSMudApp.Services.Commands;

/// <summary>
/// バウンティ（タグ付け依頼掲示板）に関する API を提供するコントローラー。
/// </summary>
[ApiController]
[Route("api/[controller]")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1508:Avoid dead code", Justification = "Result<T> union type pattern matching")]
public sealed class BountyController(
    IBountyDataProvider bountyDataProvider,
    ITaggingContractService taggingContractService,
    ICommandHandler<CreateBountyCommand, Result<bool>> createBountyHandler,
    ITagDetailDataProvider tagDetailDataProvider,
    IItemCardDataProvider itemCardDataProvider) : ControllerBase
{
    private readonly IBountyDataProvider _bountyDataProvider =
        bountyDataProvider ?? throw new ArgumentNullException(nameof(bountyDataProvider));
    private readonly ITaggingContractService _taggingContractService =
        taggingContractService ?? throw new ArgumentNullException(nameof(taggingContractService));
    private readonly ICommandHandler<CreateBountyCommand, Result<bool>> _createBountyHandler =
        createBountyHandler ?? throw new ArgumentNullException(nameof(createBountyHandler));
    private readonly ITagDetailDataProvider _tagDetailDataProvider =
        tagDetailDataProvider ?? throw new ArgumentNullException(nameof(tagDetailDataProvider));
    private readonly IItemCardDataProvider _itemCardDataProvider =
        itemCardDataProvider ?? throw new ArgumentNullException(nameof(itemCardDataProvider));

    /// <summary>
    /// 募集中（アクティブ）のバウンティ一覧を取得します。
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Quality", "CA1508:Avoid dead code", Justification = "Payload can be other ContractPayload subtypes")]
    public async Task<ActionResult<BountyBoardResponse>> GetActiveBounties(CancellationToken cancellationToken)
    {
        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        BountyBoardData boardData = await _bountyDataProvider.GetActiveBountiesAsync();

        // 宣言的 LINQ によるバウンティ DTO プロジェクション
        var dtos = boardData.Bounties.Select(bounty =>
        {
            int? rewardAssetId = null;
            string? rewardTagName = null;

            if (bounty.Payload is BountyPayload { OfferedRewardAssetId: not 0 } bp)
            {
                rewardAssetId = bp.OfferedRewardAssetId;
                if (boardData.RewardAssets.TryGetValue(bp.OfferedRewardAssetId, out RightAsset? asset))
                {
                    rewardTagName = asset.TargetTag?.Name;
                }
            }

            bool isMyBounty = !string.IsNullOrEmpty(currentUserId) && bounty.RequesterUserId == currentUserId;
            bool canFulfill = !string.IsNullOrEmpty(currentUserId) && !isMyBounty;

            return new BountyDto(
                Id: bounty.Id,
                RequesterUserId: bounty.RequesterUserId,
                RequesterUserName: bounty.Owner?.UserName,
                RequestedTagId: bounty.RequestedTagId,
                RequestedTagName: bounty.RequestedTag?.Name ?? string.Empty,
                TargetItemId: bounty.TargetItemId,
                TargetItemContent: bounty.TargetItem?.Content ?? string.Empty,
                RewardTagName: rewardTagName,
                RewardAssetId: rewardAssetId,
                CreatedDate: bounty.CreatedDate,
                IsMyBounty: isMyBounty,
                CanFulfill: canFulfill
            );
        }).ToList();

        return Ok(new BountyBoardResponse(dtos, dtos.Count));
    }

    /// <summary>
    /// 新規バウンティ（タグ付け依頼）を作成します。
    /// </summary>
    [HttpPost]
    public async Task<ActionResult> CreateBounty(
        [FromBody] CreateBountyRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized("バウンティを作成するにはログインが必要です。");
        }

        TagDetailPageData? tagData = await _tagDetailDataProvider.GetTagDetailAsync(request.RequestedTagId, currentUserId);
        if (tagData?.Tag == null)
        {
            return BadRequest("指定されたタグが存在しません。");
        }

        Item? item = await _itemCardDataProvider.GetItemByIdAsync(request.TargetItemId);
        if (item == null)
        {
            return BadRequest("指定されたアイテムが存在しません。");
        }

        TaggingRequestEntity bounty = new()
        {
            ContractType = "Bounty",
            OwnerId = currentUserId,
            RequesterUserId = currentUserId,
            TagOwnerUserId = tagData.Tag.OwnerId,
            TargetItemId = request.TargetItemId,
            RequestedTagId = request.RequestedTagId,
            Payload = new BountyPayload(request.OfferedRewardAssetId ?? 0)
        };

        Result<bool> result = await _createBountyHandler.HandleAsync(new CreateBountyCommand(bounty), cancellationToken);
        // switch 式による Result<bool> のハンドリング
        return result switch
        {
            Success<bool> => Ok(),
            Failure failure => BadRequest(failure.ErrorMessage),
            _ => BadRequest("バウンティの作成に失敗しました。")
        };
    }

    /// <summary>
    /// バウンティ（タグ付け依頼）を完了・受諾します。
    /// </summary>
    [HttpPost("{id:int}/fulfill")]
    public async Task<ActionResult> FulfillBounty(
        int id,
        [FromBody] FulfillBountyRequest? request,
        CancellationToken cancellationToken)
    {
        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized("バウンティを完了するにはログインが必要です。");
        }

        Result<string> result = await _taggingContractService.AcceptContractAsync(
            id,
            currentUserId,
            request?.AssetIdToConsume);

        // switch 式による Result<string> のハンドリング
        return result switch
        {
            Success<string> => Ok(),
            Failure failure => BadRequest(failure.ErrorMessage),
            _ => BadRequest("バウンティの完了に失敗しました。")
        };
    }
}