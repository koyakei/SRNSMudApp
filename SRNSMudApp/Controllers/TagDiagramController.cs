namespace SRNSMudApp.Controllers;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SRNSMudApp.Client.Models.Api;
using SRNSMudApp.Components.UI;
using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;

/// <summary>
/// タグダイアグラムのノード・エッジ・関連アイテムデータを配信およびエッジ編集を行うコントローラー。
/// </summary>
[ApiController]
[Route("api/tag-diagram")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1508:Avoid dead code", Justification = "Result<T> union type pattern matching")]
public sealed class TagDiagramController(
    ITagDiagramDataProvider diagramDataProvider,
    ITagDiagramDtoMapper? dtoMapper = null,
    ITagEdgeService? tagEdgeService = null) : ControllerBase
{
    private readonly ITagDiagramDataProvider _diagramDataProvider =
        diagramDataProvider ?? throw new ArgumentNullException(nameof(diagramDataProvider));
    private readonly ITagDiagramDtoMapper _dtoMapper = dtoMapper ?? TagDiagramDtoMapper.Default;
    private readonly ITagEdgeService? _tagEdgeService = tagEdgeService;

    /// <summary>
    /// テスト互換用コンストラクタ。
    /// </summary>
    public TagDiagramController(ITagDiagramDataProvider diagramDataProvider, ITagEdgeService? tagEdgeService)
        : this(diagramDataProvider, null, tagEdgeService)
    {
    }

    /// <summary>
    /// タグダイアグラム用のノード（タグ）、エッジ、およびコンテキストアイテムを取得します。
    /// </summary>
    [HttpGet("data")]
    [AllowAnonymous]
    public async Task<ActionResult<TagDiagramResponse>> GetDiagramData(
        [FromQuery] string? itemIds,
        CancellationToken cancellationToken)
    {
        List<Tag> allTags = await _diagramDataProvider.LoadAllTagsAsync();
        IReadOnlyList<TagEdge> allEdges = await _diagramDataProvider.LoadAllEdgesAsync();

        List<Item> contextItems = [];
        if (!string.IsNullOrWhiteSpace(itemIds))
        {
            // LINQ によるタプル射影を用いたアイテムID解析
            var parsedIds = itemIds
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => (Success: int.TryParse(s, out var id), Id: id))
                .Where(x => x.Success)
                .Select(x => x.Id)
                .ToList();

            if (parsedIds.Count > 0)
            {
                contextItems = await _diagramDataProvider.GetContextItemsAsync(parsedIds);
            }
        }

        var nodeDtos = allTags.Select(_dtoMapper.MapToTagDto).ToList();
        var edgeDtos = allEdges.Select(e => new TagDiagramEdgeDto(
            Id: e.Id,
            SourceTagId: e.SourceTagId,
            TargetTagId: e.TargetTagId,
            OwnerUserName: e.Owner?.UserName
        )).ToList();

        var itemDtos = contextItems.Select(_dtoMapper.MapToItemDto).ToList();

        return Ok(new TagDiagramResponse(nodeDtos, edgeDtos, itemDtos));
    }

    /// <summary>
    /// 新しいタグ間のエッジを作成します。
    /// </summary>
    [HttpPost("edges")]
    [Authorize]
    public async Task<ActionResult<TagDiagramEdgeDto>> CreateEdge([FromBody] CreateTagEdgeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        string? currentUserId = User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized("エッジの作成にはログインが必要です。");
        }

        if (_tagEdgeService == null)
        {
            return StatusCode(Microsoft.AspNetCore.Http.StatusCodes.Status501NotImplemented);
        }

        Result<TagEdge> result = await _tagEdgeService.CreateEdgeAsync(request.SourceTagId, request.TargetTagId, currentUserId);
        // switch 式による Result<TagEdge> のハンドリング
        return result switch
        {
            Success<TagEdge> success => Ok(new TagDiagramEdgeDto(
                Id: success.Value.Id,
                SourceTagId: success.Value.SourceTagId,
                TargetTagId: success.Value.TargetTagId,
                OwnerUserName: success.Value.Owner?.UserName
            )),
            Failure failure => BadRequest(failure.ErrorMessage),
            _ => StatusCode(500, "エッジ作成中に予期しないエラーが発生しました。")
        };
    }

    /// <summary>
    /// 指定されたエッジを削除します。
    /// </summary>
    [HttpDelete("edges/{edgeId:int}")]
    [Authorize]
    public async Task<IActionResult> DeleteEdge(int edgeId)
    {
        string? currentUserId = User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized("エッジの削除にはログインが必要です。");
        }

        if (_tagEdgeService == null)
        {
            return StatusCode(Microsoft.AspNetCore.Http.StatusCodes.Status501NotImplemented);
        }

        Result<bool> result = await _tagEdgeService.DeleteEdgeAsync(edgeId, currentUserId);
        // switch 式による Result<bool> のハンドリング
        return result switch
        {
            Success<bool> => NoContent(),
            Failure failure => BadRequest(failure.ErrorMessage),
            _ => StatusCode(500, "エッジ削除中に予期しないエラーが発生しました。")
        };
    }

    /// <summary>
    /// 指定されたエッジに紐付けられている意味付けタグ一覧を取得します。
    /// </summary>
    [HttpGet("edges/{edgeId:int}/attachments")]
    [AllowAnonymous]
    public async Task<ActionResult<IReadOnlyList<TagEdgeAttachmentDto>>> GetAttachmentsForEdge(int edgeId)
    {
        if (_tagEdgeService == null)
        {
            return StatusCode(Microsoft.AspNetCore.Http.StatusCodes.Status501NotImplemented);
        }

        IReadOnlyList<TagEdgeTagAttachment> attachments = await _tagEdgeService.GetAttachmentsForEdgeAsync(edgeId);
        var dtos = attachments.Select(_dtoMapper.MapToAttachmentDto).ToList();
        return Ok(dtos);
    }

    /// <summary>
    /// エッジに意味付けタグを紐付けます（RightAssetを消費）。
    /// </summary>
    [HttpPost("edges/{edgeId:int}/attachments")]
    [Authorize]
    public async Task<ActionResult<TagEdgeAttachmentDto>> AttachTagToEdge(
        int edgeId,
        [FromBody] AttachTagToEdgeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        string? currentUserId = User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized("タグの紐付けにはログインが必要です。");
        }

        if (_tagEdgeService == null)
        {
            return StatusCode(Microsoft.AspNetCore.Http.StatusCodes.Status501NotImplemented);
        }

        Result<TagEdgeTagAttachment> result = await _tagEdgeService.AttachTagToEdgeAsync(
            edgeId, request.TagId, request.RightAssetId, currentUserId, request.Weight);

        // switch 式による Result<TagEdgeTagAttachment> のハンドリング
        return result switch
        {
            Success<TagEdgeTagAttachment> success => Ok(_dtoMapper.MapToAttachmentDto(success.Value)),
            Failure failure => BadRequest(failure.ErrorMessage),
            _ => StatusCode(500, "タグ紐付け中に予期しないエラーが発生しました。")
        };
    }

    /// <summary>
    /// エッジから意味付けタグの紐付けを解除します。
    /// </summary>
    [HttpDelete("attachments/{attachmentId:int}")]
    [Authorize]
    public async Task<IActionResult> DetachTagFromEdge(int attachmentId)
    {
        string? currentUserId = User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized("紐付け解除にはログインが必要です。");
        }

        if (_tagEdgeService == null)
        {
            return StatusCode(Microsoft.AspNetCore.Http.StatusCodes.Status501NotImplemented);
        }

        Result<bool> result = await _tagEdgeService.DetachTagFromEdgeAsync(attachmentId, currentUserId);
        // switch 式による Result<bool> のハンドリング
        return result switch
        {
            Success<bool> => NoContent(),
            Failure failure => BadRequest(failure.ErrorMessage),
            _ => StatusCode(500, "紐付け解除中に予期しないエラーが発生しました。")
        };
    }
}