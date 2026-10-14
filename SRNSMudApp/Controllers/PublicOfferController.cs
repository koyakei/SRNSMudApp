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
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;
using SRNSMudApp.Services.Commands;

/// <summary>
/// 公開オファー掲示板に関する API を提供するコントローラー。
/// </summary>
[ApiController]
[Route("api/public-offer")]
public sealed class PublicOfferController(
    IPublicOfferDataProvider publicOfferDataProvider,
    ITaggingContractService taggingContractService,
    ICommandHandler<CreatePublicOfferCommand, Result<bool>> createPublicOfferHandler,
    ICommandHandler<CreateTriggerContractCommand, Result<bool>> createTriggerContractHandler,
    ITagDetailDataProvider tagDetailDataProvider,
    IItemCardDataProvider itemCardDataProvider) : ControllerBase
{
    private readonly IPublicOfferDataProvider _publicOfferDataProvider =
        publicOfferDataProvider ?? throw new ArgumentNullException(nameof(publicOfferDataProvider));
    private readonly ITaggingContractService _taggingContractService =
        taggingContractService ?? throw new ArgumentNullException(nameof(taggingContractService));
    private readonly ICommandHandler<CreatePublicOfferCommand, Result<bool>> _createPublicOfferHandler =
        createPublicOfferHandler ?? throw new ArgumentNullException(nameof(createPublicOfferHandler));
    private readonly ICommandHandler<CreateTriggerContractCommand, Result<bool>> _createTriggerContractHandler =
        createTriggerContractHandler ?? throw new ArgumentNullException(nameof(createTriggerContractHandler));
    private readonly ITagDetailDataProvider _tagDetailDataProvider =
        tagDetailDataProvider ?? throw new ArgumentNullException(nameof(tagDetailDataProvider));
    private readonly IItemCardDataProvider _itemCardDataProvider =
        itemCardDataProvider ?? throw new ArgumentNullException(nameof(itemCardDataProvider));

    /// <summary>
    /// アクティブな公開オファー一覧を取得します。
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<IReadOnlyList<PublicOfferDto>>> GetActiveOffers(CancellationToken cancellationToken)
    {
        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        List<PublicTradeOffer> offers = await _publicOfferDataProvider.GetActivePublicOffersAsync();

        var dtos = offers.Select(o => new PublicOfferDto(
            Id: o.Id,
            OfferedTagId: o.OfferedTagId,
            OfferedTagName: o.OfferedTag?.Name ?? string.Empty,
            RequiredAssetAmount: o.RequiredAssetAmount,
            OwnerUserId: o.OwnerId,
            OwnerUserName: o.Owner?.UserName,
            CreatedDate: o.CreatedDate,
            IsOwner: !string.IsNullOrEmpty(currentUserId) && o.OwnerId == currentUserId
        )).ToList();

        return Ok(dtos);
    }

    /// <summary>
    /// 新しい公開オファーを出品します。
    /// </summary>
    [HttpPost]
    public async Task<ActionResult> CreateOffer(
        [FromBody] CreatePublicOfferRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized("オファーを作成するにはログインが必要です。");
        }

        if (request.RequiredAssetAmount < 0)
        {
            return BadRequest("要求するアセット量は0以上を指定してください。");
        }

        TagDetailPageData? tagData = await _tagDetailDataProvider.GetTagDetailAsync(request.OfferedTagId, currentUserId);
        if (tagData?.Tag == null)
        {
            return BadRequest("指定されたタグが存在しません。");
        }

        var offer = new PublicTradeOffer
        {
            OfferedTagId = request.OfferedTagId,
            RequiredAssetAmount = request.RequiredAssetAmount,
            OwnerId = currentUserId,
            IsActive = true,
            CreatedDate = DateTime.UtcNow
        };

        Result<bool> result = await _createPublicOfferHandler.HandleAsync(new CreatePublicOfferCommand(offer), cancellationToken);
        switch (result)
        {
            case Success<bool>:
                return Ok();
            case Failure failure:
                return BadRequest(failure.ErrorMessage);
            default:
                return BadRequest("オファーの作成に失敗しました。");
        }
    }

    /// <summary>
    /// 自分の公開オファーを取り下げます。
    /// </summary>
    [HttpDelete("{id:int}")]
    public async Task<ActionResult> DeactivateOffer(
        int id,
        CancellationToken cancellationToken)
    {
        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized("オファーを取り下げるにはログインが必要です。");
        }

        bool success = await _publicOfferDataProvider.DeactivatePublicOfferAsync(id, currentUserId);
        if (!success)
        {
            return BadRequest("オファーの取り下げに失敗しました。所有者権限を確認してください。");
        }

        return Ok();
    }

    /// <summary>
    /// 公開オファーを承諾（トリガー実行）し、対象アイテムにタグ付けを行います。
    /// </summary>
    [HttpPost("{id:int}/trigger")]
    public async Task<ActionResult> TriggerOffer(
        int id,
        [FromBody] TriggerPublicOfferRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized("オファーを承諾するにはログインが必要です。");
        }

        List<PublicTradeOffer> offers = await _publicOfferDataProvider.GetActivePublicOffersAsync();
        PublicTradeOffer? offer = offers.FirstOrDefault(o => o.Id == id);
        if (offer == null)
        {
            return NotFound("指定されたオファーが見つかりません。");
        }

        if (offer.OwnerId == currentUserId)
        {
            return BadRequest("自分のオファーを自分で承諾することはできません。");
        }

        Item? item = await _itemCardDataProvider.GetItemByIdAsync(request.TargetItemId);
        if (item == null)
        {
            return BadRequest("指定されたアイテムが見つかりません。");
        }

        var triggerContract = new TaggingRequestEntity
        {
            ContractType = "Trigger",
            OwnerId = currentUserId,
            RequesterUserId = currentUserId,
            TagOwnerUserId = offer.OwnerId,
            TargetItemId = request.TargetItemId,
            RequestedTagId = offer.OfferedTagId,
            Payload = new PublicOfferPayload(offer.Id),
            ConsumedRightAssetId = request.AssetIdToConsume
        };

        Result<bool> commandResult = await _createTriggerContractHandler.HandleAsync(
            new CreateTriggerContractCommand(triggerContract), cancellationToken);

        switch (commandResult)
        {
            case Success<bool>:
                break;
            case Failure failure:
                return BadRequest(failure.ErrorMessage);
            default:
                return BadRequest("トリガー契約の作成に失敗しました。");
        }

        Result<string> acceptResult = await _taggingContractService.AcceptContractAsync(
            triggerContract.Id,
            currentUserId,
            request.AssetIdToConsume);

        switch (acceptResult)
        {
            case Success<string>:
                return Ok();
            case Failure fail:
                return BadRequest(fail.ErrorMessage);
            default:
                return BadRequest("契約の完了に失敗しました。");
        }
    }
}