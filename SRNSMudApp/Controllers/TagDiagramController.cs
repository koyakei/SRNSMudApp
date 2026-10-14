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
public sealed class TagDiagramController(
    ITagDiagramDataProvider diagramDataProvider,
    ITagEdgeService? tagEdgeService = null) : ControllerBase
{
    private readonly ITagDiagramDataProvider _diagramDataProvider =
        diagramDataProvider ?? throw new ArgumentNullException(nameof(diagramDataProvider));
    private readonly ITagEdgeService? _tagEdgeService = tagEdgeService;

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
            var parsedIds = itemIds
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => int.TryParse(s, out var id) ? (int?)id : null)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToList();

            if (parsedIds.Count > 0)
            {
                contextItems = await _diagramDataProvider.GetContextItemsAsync(parsedIds);
            }
        }

        var nodeDtos = allTags.Select(MapToTagDto).ToList();
        var edgeDtos = allEdges.Select(e => new TagDiagramEdgeDto(
            Id: e.Id,
            SourceTagId: e.SourceTagId,
            TargetTagId: e.TargetTagId,
            OwnerUserName: e.Owner?.UserName
        )).ToList();

        var itemDtos = contextItems.Select(MapToItemDto).ToList();

        return Ok(new TagDiagramResponse(nodeDtos, edgeDtos, itemDtos));
    }

    private static TagDto MapToTagDto(Tag tag)
    {
        return new TagDto(
            Id: tag.Id,
            Name: tag.Name,
            Content: tag.Content,
            OwnerUserName: tag.Owner?.UserName,
            OwnerUserId: tag.OwnerId,
            Score: TagCardViewModel.GetTagScore(tag),
            IsPrivate: tag.IsPrivate,
            TargetUserGroupName: tag.TargetUserGroup?.Name
        );
    }

    private static ItemDto MapToItemDto(Item item)
    {
        var relations = item.TagRelations
            .Select(tr => new TagRelationDto(
                TagId: tr.TagId,
                TagName: tr.Tag?.Name ?? string.Empty,
                Weight: tr.Weight,
                OwnerUserName: tr.Owner?.UserName ?? tr.Tag?.Owner?.UserName
            ))
            .ToList();

        return new ItemDto(
            Id: item.Id,
            Content: item.Content,
            OwnerUserName: item.Owner?.UserName,
            UpdatedDate: item.UpdatedDate,
            IsPrivate: item.IsPrivate,
            TagRelations: relations,
            ItemKindJson: item.ItemKindJson,
            ParentItemId: item.ParentItemId,
            RootItemId: item.RootItemId,
            QuotedItemId: item.QuotedItemId,
            OwnerUserId: item.OwnerId,
            Score: ItemCardViewModel.GetItemScore(item.TagRelations),
            ShinjiScore: ItemCardViewModel.GetReactionScore(item.TagRelations, ReactionTagNames.Shinji),
            ZenScore: ItemCardViewModel.GetReactionScore(item.TagRelations, ReactionTagNames.Zen),
            BiScore: ItemCardViewModel.GetReactionScore(item.TagRelations, ReactionTagNames.Bi),
            ReplyCount: 0,
            QuoteCount: 0,
            QuotedItemContent: item.QuotedItem?.Content,
            QuotedItemOwnerUserName: item.QuotedItem?.Owner?.UserName,
            TargetUserGroupName: item.TargetUserGroup?.Name
        );
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
        switch (result)
        {
            case Success<TagEdge> success:
                TagEdge edge = success.Value;
                return Ok(new TagDiagramEdgeDto(
                    Id: edge.Id,
                    SourceTagId: edge.SourceTagId,
                    TargetTagId: edge.TargetTagId,
                    OwnerUserName: edge.Owner?.UserName
                ));
            case Failure failure:
                return BadRequest(failure.ErrorMessage);
            default:
                return StatusCode(500, "エッジ作成中に予期しないエラーが発生しました。");
        }
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
        switch (result)
        {
            case Success<bool>:
                return NoContent();
            case Failure failure:
                return BadRequest(failure.ErrorMessage);
            default:
                return StatusCode(500, "エッジ削除中に予期しないエラーが発生しました。");
        }
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
        var dtos = attachments.Select(MapToAttachmentDto).ToList();
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

        switch (result)
        {
            case Success<TagEdgeTagAttachment> success:
                return Ok(MapToAttachmentDto(success.Value));
            case Failure failure:
                return BadRequest(failure.ErrorMessage);
            default:
                return StatusCode(500, "タグ紐付け中に予期しないエラーが発生しました。");
        }
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
        switch (result)
        {
            case Success<bool>:
                return NoContent();
            case Failure failure:
                return BadRequest(failure.ErrorMessage);
            default:
                return StatusCode(500, "紐付け解除中に予期しないエラーが発生しました。");
        }
    }

    private static TagEdgeAttachmentDto MapToAttachmentDto(TagEdgeTagAttachment att)
    {
        return new TagEdgeAttachmentDto(
            Id: att.Id,
            EdgeId: att.TagEdgeId,
            TagId: att.TagId,
            TagName: att.Tag?.Name ?? string.Empty,
            OwnerUserName: att.Owner?.UserName,
            Weight: att.Weight,
            CreatedDate: att.CreatedDate
        );
    }
}