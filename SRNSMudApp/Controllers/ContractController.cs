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
public sealed class ContractController(
    IContractManagementDataProvider contractDataProvider,
    ITaggingContractService taggingContractService) : ControllerBase
{
    private readonly IContractManagementDataProvider _contractDataProvider =
        contractDataProvider ?? throw new ArgumentNullException(nameof(contractDataProvider));
    private readonly ITaggingContractService _taggingContractService =
        taggingContractService ?? throw new ArgumentNullException(nameof(taggingContractService));

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
        switch (result)
        {
            case Success<string>:
                return Ok();
            case Failure failure:
                return BadRequest(failure.ErrorMessage);
            default:
                return BadRequest("コントラクトの承認に失敗しました。");
        }
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
        switch (result)
        {
            case Success<string>:
                return Ok();
            case Failure failure:
                return BadRequest(failure.ErrorMessage);
            default:
                return BadRequest("コントラクトの拒否に失敗しました。");
        }
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
        switch (result)
        {
            case Success<string>:
                return Ok();
            case Failure failure:
                return BadRequest(failure.ErrorMessage);
            default:
                return BadRequest("コントラクトの取り下げに失敗しました。");
        }
    }

    private static ContractDto MapToDto(TaggingRequestEntity contract, string currentUserId)
    {
        return new ContractDto(
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