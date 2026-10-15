namespace SRNSMudApp.Services;

using System;
using System.Linq;

using SRNSMudApp.Client.Models.Api;
using SRNSMudApp.Components.UI;
using SRNSMudApp.Data;
using SRNSMudApp.Models;

/// <summary>
/// <see cref="ITagDiagramDtoMapper"/> の既定実装ファクトリクラス。
/// </summary>
public sealed class TagDiagramDtoMapper : ITagDiagramDtoMapper
{
    /// <summary>
    /// シングルトン共有インスタンス。
    /// </summary>
    public static readonly TagDiagramDtoMapper Default = new();

    /// <inheritdoc />
    public TagDto MapToTagDto(Tag tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
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

    /// <inheritdoc />
    public ItemDto MapToItemDto(Item item)
    {
        ArgumentNullException.ThrowIfNull(item);
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

    /// <inheritdoc />
    public TagEdgeAttachmentDto MapToAttachmentDto(TagEdgeTagAttachment att)
    {
        ArgumentNullException.ThrowIfNull(att);
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