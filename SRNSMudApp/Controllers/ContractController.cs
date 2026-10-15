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

/// <summary>
/// コントラクト（契約提案・インボックス）管理に関する API を提供するコントローラー。
/// </summary>
[ApiController]
[Route("api/[controller]")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1508:Avoid dead code", Justification = "Result<T> union type pattern matching")]
public sealed class ContractController(
    IContractManagementDataProvider contractDataProvider,
    ITaggingContractService taggingContractService,
    IContractLookupDataProvider? contractLookupDataProvider = null,
    ITagDetailDataProvider? tagDetailDataProvider = null) : ControllerBase
{
    private readonly IContractManagementDataProvider _contractDataProvider =
        contractDataProvider ?? throw new ArgumentNullException(nameof(contractDataProvider));
    private readonly ITaggingContractService _taggingContractService =
        taggingContractService ?? throw new ArgumentNullException(nameof(taggingContractService));
    private readonly IContractLookupDataProvider? _contractLookupDataProvider = contractLookupDataProvider;
    private readonly ITagDetailDataProvider? _tagDetailDataProvider = tagDetailDataProvider;

    /// <summary>
    /// ログインユーザーのコントラクト一覧（受信・送信）を取得します。
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ContractPageResponse>> GetContracts(CancellationToken cancellationToken)
    {
        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized("コントラクト一覧を取得するにはログインが必要です。");
        }

        ContractManagementPageData data = await _contractDataProvider.GetContractsAsync(currentUserId);

        var incomingDtos = data.IncomingContracts.Select(c => MapToDto(c, currentUserId)).ToList();
        var outgoingDtos = data.OutgoingContracts.Select(c => MapToDto(c, currentUserId)).ToList();

        return Ok(new ContractPageResponse(incomingDtos, outgoingDtos));
    }

    /// <summary>
    /// コントラクトを承認します。
    /// </summary>
    [HttpPost("{id:int}/accept")]
    public async Task<ActionResult> AcceptContract(
        int id,
        CancellationToken cancellationToken)
    {
        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized("コントラクトを承認するにはログインが必要です。");
        }

        Result<string> result = await _taggingContractService.AcceptContractAsync(id, currentUserId);
        return result switch
        {
            Success<string> => Ok(),
            Failure failure => BadRequest(failure.ErrorMessage),
            _ => BadRequest("コントラクトの承認に失敗しました。")
        };
    }

    /// <summary>
    /// コントラクトを拒否します。
    /// </summary>
    [HttpPost("{id:int}/reject")]
    public async Task<ActionResult> RejectContract(
        int id,
        CancellationToken cancellationToken)
    {
        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized("コントラクトを拒否するにはログインが必要です。");
        }

        Result<string> result = await _taggingContractService.CancelContractAsync(id, currentUserId);
        return result switch
        {
            Success<string> => Ok(),
            Failure failure => BadRequest(failure.ErrorMessage),
            _ => BadRequest("コントラクトの拒否に失敗しました。")
        };
    }

    /// <summary>
    /// 自分が送信したコントラクトを取り下げ（キャンセル）します。
    /// </summary>
    [HttpPost("{id:int}/cancel")]
    public async Task<ActionResult> CancelContract(
        int id,
        CancellationToken cancellationToken)
    {
        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized("コントラクトを取り下げるにはログインが必要です。");
        }

        Result<string> result = await _taggingContractService.CancelContractAsync(id, currentUserId);
        return result switch
        {
            Success<string> => Ok(),
            Failure failure => BadRequest(failure.ErrorMessage),
            _ => BadRequest("コントラクトの取り下げに失敗しました。")
        };
    }

    /// <summary>
    /// ログインユーザーの利用可能ライトアセット一覧を取得します。
    /// </summary>
    [HttpGet("available-assets")]
    public async Task<ActionResult<IReadOnlyList<RightAssetDto>>> GetAvailableAssets(CancellationToken cancellationToken)
    {
        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized("利用可能なアセットを取得するにはログインが必要です。");
        }

        if (_contractLookupDataProvider == null)
        {
            return StatusCode(Microsoft.AspNetCore.Http.StatusCodes.Status501NotImplemented);
        }

        List<RightAsset> assets = await _contractLookupDataProvider.GetAvailableRightAssetsAsync(currentUserId);
        var dtos = assets.Select(a => new RightAssetDto(
            Id: a.Id,
            TargetTagId: a.TargetTagId,
            TargetTagName: a.TargetTag?.Name ?? string.Empty,
            Amount: a.Amount,
            IsBurned: a.IsBurned
        )).ToList();

        return Ok(dtos);
    }

    /// <summary>
    /// Gratis（無償）コントラクト提案を送信します。
    /// </summary>
    [HttpPost("gratis")]
    public async Task<ActionResult<ContractDto>> ProposeGratis(
        [FromBody] ProposeGratisContractRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized("コントラクトを提案するにはログインが必要です。");
        }

        if (request.TargetItemId <= 0 || request.RequestedTagId <= 0)
        {
            return BadRequest("対象アイテムおよび対象タグを指定してください。");
        }

        string tagOwnerUserId = string.Empty;
        if (_tagDetailDataProvider != null)
        {
            TagDetailPageData? tagDetail = await _tagDetailDataProvider.GetTagDetailAsync(request.RequestedTagId, currentUserId);
            tagOwnerUserId = tagDetail?.Tag?.OwnerId ?? string.Empty;
        }

        if (string.IsNullOrEmpty(tagOwnerUserId))
        {
            return BadRequest("指定されたタグが存在しないか、所有者が不明です。");
        }

        if (!Enum.TryParse(request.RequestType, true, out TaggingRequestType requestType))
        {
            requestType = TaggingRequestType.Add;
        }

        Result<TaggingRequestEntity> result = await _taggingContractService.ProposeGratisContractAsync(
            requesterUserId: currentUserId,
            tagOwnerUserId: tagOwnerUserId,
            targetItemId: request.TargetItemId,
            requestedTagId: request.RequestedTagId,
            requestType: requestType,
            proposedWeight: request.ProposedWeight,
            message: request.Message);

        return result switch
        {
            Success<TaggingRequestEntity> success => Ok(MapToDto(success.Value, currentUserId)),
            Failure failure => BadRequest(failure.ErrorMessage),
            _ => StatusCode(500, "コントラクト提案の送信中に予期しないエラーが発生しました。")
        };
    }

    /// <summary>
    /// Mutual（相互）コントラクト提案を送信します。
    /// </summary>
    [HttpPost("mutual")]
    public async Task<ActionResult<ContractDto>> ProposeMutual(
        [FromBody] ProposeMutualContractRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized("相互コントラクトを提案するにはログインが必要です。");
        }

        if (request.TargetItemId <= 0 || request.RequestedTagId <= 0 ||
            request.OfferedTargetItemId <= 0 || request.OfferedTagId <= 0 ||
            request.ConsumedRightAssetId <= 0)
        {
            return BadRequest("相互タグ付けに必要な項目（アイテム、タグ、アセット）を正しく入力してください。");
        }

        string tagOwnerUserId = string.Empty;
        if (_tagDetailDataProvider != null)
        {
            TagDetailPageData? tagDetail = await _tagDetailDataProvider.GetTagDetailAsync(request.RequestedTagId, currentUserId);
            tagOwnerUserId = tagDetail?.Tag?.OwnerId ?? string.Empty;
        }

        if (string.IsNullOrEmpty(tagOwnerUserId))
        {
            return BadRequest("指定されたタグが存在しないか、所有者が不明です。");
        }

        if (!Enum.TryParse(request.RequestType, true, out TaggingRequestType requestType))
        {
            requestType = TaggingRequestType.Add;
        }

        Result<TaggingRequestEntity> result = await _taggingContractService.ProposeMutualContractAsync(
            requesterUserId: currentUserId,
            tagOwnerUserId: tagOwnerUserId,
            targetItemId: request.TargetItemId,
            requestedTagId: request.RequestedTagId,
            offeredTargetItemId: request.OfferedTargetItemId,
            offeredTagId: request.OfferedTagId,
            consumedRightAssetId: request.ConsumedRightAssetId,
            requestType: requestType,
            proposedWeight: request.ProposedWeight);

        return result switch
        {
            Success<TaggingRequestEntity> success => Ok(MapToDto(success.Value, currentUserId)),
            Failure failure => BadRequest(failure.ErrorMessage),
            _ => StatusCode(500, "相互コントラクト提案の送信中に予期しないエラーが発生しました。")
        };
    }

    private static ContractDto MapToDto(TaggingRequestEntity contract, string currentUserId)
    {
        return new(
            Id: contract.Id,
            ContractType: contract.ContractType,
            Status: contract.Status.ToString(),
            RequestedTagId: contract.RequestedTagId,
            RequestedTagName: contract.RequestedTag?.Name ?? string.Empty,
            TargetItemId: contract.TargetItemId,
            TargetItemContent: contract.TargetItem?.Content ?? string.Empty,
            RequesterUserId: contract.RequesterUserId,
            RequesterUserName: contract.Owner?.UserName,
            TagOwnerUserId: contract.TagOwnerUserId,
            TagOwnerUserName: contract.RequestedTag?.Owner?.UserName,
            CreatedDate: contract.CreatedDate,
            CanCancel: contract.Status == TradeStatus.Proposed && contract.RequesterUserId == currentUserId
        );
    }
}