namespace SRNSMudApp.Controllers;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;

using SRNSMudApp.Client.Models.Api;
using SRNSMudApp.Components.UI;
using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Services;

/// <summary>
/// タグダイアグラムのノード・エッジ・関連アイテムデータを配信するコントローラー。
/// </summary>
[ApiController]
[Route("api/tag-diagram")]
public sealed class TagDiagramController(ITagDiagramDataProvider diagramDataProvider) : ControllerBase
{
    private readonly ITagDiagramDataProvider _diagramDataProvider =
        diagramDataProvider ?? throw new ArgumentNullException(nameof(diagramDataProvider));

    /// <summary>
    /// タグダイアグラム用のノード（タグ）、エッジ、およびコンテキストアイテムを取得します。
    /// </summary>
    [HttpGet("data")]
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
}