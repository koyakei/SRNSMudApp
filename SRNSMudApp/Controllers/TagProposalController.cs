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
/// 他ユーザーのタグに対する名称（Name）および説明文（Content）の変更提案を処理する Web API コントローラー。
/// </summary>
[ApiController]
[Route("api/tag-proposal")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1508:Avoid dead code", Justification = "Result<T> union type pattern matching")]
public sealed class TagProposalController(
    ITagNameProposalService nameProposalService,
    ITagContentProposalService contentProposalService) : ControllerBase
{
    private readonly ITagNameProposalService _nameProposalService =
        nameProposalService ?? throw new ArgumentNullException(nameof(nameProposalService));
    private readonly ITagContentProposalService _contentProposalService =
        contentProposalService ?? throw new ArgumentNullException(nameof(contentProposalService));

    /// <summary>
    /// タグ名変更の提案を申請します。
    /// </summary>
    [HttpPost("name")]
    [Authorize]
    public async Task<ActionResult<TagNameProposalDto>> ProposeTagName(
        [FromBody] ProposeTagNameRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized("提案を申請するにはログインが必要です。");
        }

        if (string.IsNullOrWhiteSpace(request.ProposedName))
        {
            return BadRequest("提案するタグ名は必須です。");
        }

        Result<TagNameProposal> result = await _nameProposalService.ProposeNameAsync(
            request.TagId,
            request.ProposedName.Trim(),
            request.Reason?.Trim(),
            currentUserId,
            cancellationToken);

        // switch 式による Result<TagNameProposal> のハンドリング
        return result switch
        {
            Success<TagNameProposal> success => Ok(MapToNameDto(success.Value)),
            Failure failure => BadRequest(failure.ErrorMessage),
            _ => StatusCode(500, "提案の申請中に予期しないエラーが発生しました。")
        };
    }

    /// <summary>
    /// タグ説明文（Content）変更の提案を申請します。
    /// </summary>
    [HttpPost("content")]
    [Authorize]
    public async Task<ActionResult<TagContentProposalDto>> ProposeTagContent(
        [FromBody] ProposeTagContentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized("提案を申請するにはログインが必要です。");
        }

        Result<TagContentProposal> result = await _contentProposalService.ProposeContentAsync(
            request.TagId,
            request.ProposedContent.Trim(),
            request.Reason?.Trim(),
            currentUserId,
            cancellationToken);

        // switch 式による Result<TagContentProposal> のハンドリング
        return result switch
        {
            Success<TagContentProposal> success => Ok(MapToContentDto(success.Value)),
            Failure failure => BadRequest(failure.ErrorMessage),
            _ => StatusCode(500, "提案の申請中に予期しないエラーが発生しました。")
        };
    }

    /// <summary>
    /// 指定されたタグに対する保留中（Pending）の提案一覧を取得します。
    /// </summary>
    [HttpGet("tag/{tagId:int}/pending")]
    public async Task<ActionResult<TagPendingProposalsDto>> GetPendingProposals(
        int tagId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<TagNameProposal> nameProposals =
            await _nameProposalService.GetPendingProposalsForTagAsync(tagId, cancellationToken);
        IReadOnlyList<TagContentProposal> contentProposals =
            await _contentProposalService.GetPendingProposalsForTagAsync(tagId, cancellationToken);

        var nameDtos = nameProposals.Select(MapToNameDto).ToList();
        var contentDtos = contentProposals.Select(MapToContentDto).ToList();

        return Ok(new TagPendingProposalsDto(nameDtos, contentDtos));
    }

    /// <summary>
    /// タグ名変更提案を承認します（タグ所有者のみ）。
    /// </summary>
    [HttpPost("name/{proposalId:int}/approve")]
    [Authorize]
    public async Task<ActionResult<bool>> ApproveNameProposal(
        int proposalId,
        CancellationToken cancellationToken)
    {
        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized();
        }

        Result<Tag> result = await _nameProposalService.ApproveProposalAsync(proposalId, currentUserId, cancellationToken);
        // switch 式による Result<Tag> のハンドリング
        return result switch
        {
            Success<Tag> => Ok(true),
            Failure failure => BadRequest(failure.ErrorMessage),
            _ => StatusCode(500, "承認処理中に予期しないエラーが発生しました。")
        };
    }

    /// <summary>
    /// タグ名変更提案を却下します（タグ所有者のみ）。
    /// </summary>
    [HttpPost("name/{proposalId:int}/reject")]
    [Authorize]
    public async Task<ActionResult<bool>> RejectNameProposal(
        int proposalId,
        [FromBody] RejectProposalRequest? request,
        CancellationToken cancellationToken)
    {
        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized();
        }

        Result<bool> result = await _nameProposalService.RejectProposalAsync(
            proposalId,
            currentUserId,
            request?.RejectReason?.Trim(),
            cancellationToken);

        // switch 式による Result<bool> のハンドリング
        return result switch
        {
            Success<bool> => Ok(true),
            Failure failure => BadRequest(failure.ErrorMessage),
            _ => StatusCode(500, "却下処理中に予期しないエラーが発生しました。")
        };
    }

    /// <summary>
    /// タグ内容変更提案を承認します（タグ所有者のみ）。
    /// </summary>
    [HttpPost("content/{proposalId:int}/approve")]
    [Authorize]
    public async Task<ActionResult<bool>> ApproveContentProposal(
        int proposalId,
        CancellationToken cancellationToken)
    {
        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized();
        }

        Result<Tag> result = await _contentProposalService.ApproveProposalAsync(proposalId, currentUserId, cancellationToken);
        // switch 式による Result<Tag> のハンドリング
        return result switch
        {
            Success<Tag> => Ok(true),
            Failure failure => BadRequest(failure.ErrorMessage),
            _ => StatusCode(500, "承認処理中に予期しないエラーが発生しました。")
        };
    }

    /// <summary>
    /// タグ内容変更提案を却下します（タグ所有者のみ）。
    /// </summary>
    [HttpPost("content/{proposalId:int}/reject")]
    [Authorize]
    public async Task<ActionResult<bool>> RejectContentProposal(
        int proposalId,
        [FromBody] RejectProposalRequest? request,
        CancellationToken cancellationToken)
    {
        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized();
        }

        Result<bool> result = await _contentProposalService.RejectProposalAsync(
            proposalId,
            currentUserId,
            request?.RejectReason?.Trim(),
            cancellationToken);

        // switch 式による Result<bool> のハンドリング
        return result switch
        {
            Success<bool> => Ok(true),
            Failure failure => BadRequest(failure.ErrorMessage),
            _ => StatusCode(500, "却下処理中に予期しないエラーが発生しました。")
        };
    }

    /// <summary>
    /// 申請したタグ名変更提案を取り下げます（申請者本人のみ）。
    /// </summary>
    [HttpPost("name/{proposalId:int}/cancel")]
    [Authorize]
    public async Task<ActionResult<bool>> CancelNameProposal(
        int proposalId,
        CancellationToken cancellationToken)
    {
        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized();
        }

        Result<bool> result = await _nameProposalService.CancelProposalAsync(proposalId, currentUserId, cancellationToken);
        // switch 式による Result<bool> のハンドリング
        return result switch
        {
            Success<bool> => Ok(true),
            Failure failure => BadRequest(failure.ErrorMessage),
            _ => StatusCode(500, "取り下げ処理中に予期しないエラーが発生しました。")
        };
    }

    /// <summary>
    /// 申請したタグ内容変更提案を取り下げます（申請者本人のみ）。
    /// </summary>
    [HttpPost("content/{proposalId:int}/cancel")]
    [Authorize]
    public async Task<ActionResult<bool>> CancelContentProposal(
        int proposalId,
        CancellationToken cancellationToken)
    {
        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized();
        }

        Result<bool> result = await _contentProposalService.CancelProposalAsync(proposalId, currentUserId, cancellationToken);
        // switch 式による Result<bool> のハンドリング
        return result switch
        {
            Success<bool> => Ok(true),
            Failure failure => BadRequest(failure.ErrorMessage),
            _ => StatusCode(500, "取り下げ処理中に予期しないエラーが発生しました。")
        };
    }

    private static TagNameProposalDto MapToNameDto(TagNameProposal entity) => new(
        Id: entity.Id,
        TagId: entity.TagId,
        TargetTagName: entity.Tag?.Name ?? string.Empty,
        ProposedName: entity.ProposedName,
        RequesterUserId: entity.RequesterUserId,
        RequesterUserName: entity.RequesterUser?.UserName,
        OwnerUserId: entity.OwnerUserId,
        Reason: entity.Reason,
        Status: entity.Status.ToString(),
        RejectReason: entity.RejectReason,
        CreatedDate: entity.CreatedDate
    );

    private static TagContentProposalDto MapToContentDto(TagContentProposal entity) => new(
        Id: entity.Id,
        TagId: entity.TagId,
        TargetTagName: entity.Tag?.Name ?? string.Empty,
        ProposedContent: entity.ProposedContent,
        RequesterUserId: entity.RequesterUserId,
        RequesterUserName: entity.RequesterUser?.UserName,
        OwnerUserId: entity.OwnerUserId,
        Reason: entity.Reason,
        Status: entity.Status.ToString(),
        RejectReason: entity.RejectReason,
        CreatedDate: entity.CreatedDate
    );
}