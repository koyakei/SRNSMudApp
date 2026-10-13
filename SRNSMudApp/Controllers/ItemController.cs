namespace SRNSMudApp.Controllers;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;

using SRNSMudApp.Client.Models;
using SRNSMudApp.Client.Models.Api;
using SRNSMudApp.Components.UI;
using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Services;

/// <summary>
/// ItemList の一覧表示やタグ検索に関する API を提供するコントローラー。
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class ItemController(
    IItemListDataProvider itemListDataProvider) : ControllerBase
{
    private readonly IItemListDataProvider _itemListDataProvider =
        itemListDataProvider ?? throw new ArgumentNullException(nameof(itemListDataProvider));

    /// <summary>
    /// タグ名のサジェスト候補を検索します。
    /// </summary>
    [HttpGet("search-tags")]
    public async Task<ActionResult<IReadOnlyList<TagSuggestion>>> SearchTags(
        [FromQuery] string? q,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return Ok(Array.Empty<TagSuggestion>());
        }

        IReadOnlyList<TagSuggestion> results = await _itemListDataProvider
            .SearchTagNameSuggestionsAsync(q, cancellationToken);
        return Ok(results);
    }

    /// <summary>
    /// 特定タグに関連付けられたユーザー名一覧を検索します。
    /// </summary>
    [HttpGet("tag-users")]
    public async Task<ActionResult<IReadOnlyList<string>>> SearchTagUsers(
        [FromQuery] string tagName,
        [FromQuery] string? userSearch,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tagName))
        {
            return BadRequest("tagName は必須です。");
        }

        IReadOnlyList<string> users = await _itemListDataProvider
            .SearchTagUserNamesAsync(tagName, userSearch ?? string.Empty, cancellationToken);
        return Ok(users);
    }

    /// <summary>
    /// フィルタおよびソート条件に合致するアイテムとタグの一覧を取得します。
    /// </summary>
    [HttpPost("query")]
    public async Task<ActionResult<ItemListQueryResponse>> QueryItems(
        [FromBody] ItemListQueryRequest? request,
        CancellationToken cancellationToken)
    {
        var req = request ?? new ItemListQueryRequest();

        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var isAdmin = User?.IsInRole("Admin") ?? false;

        // リクエスト DTO からドメインのフィルタリストを作成
        var domainFilters = new List<ItemListFilter>();
        if (req.Filters is not null)
        {
            foreach (ItemListFilterDto f in req.Filters)
            {
                if (f.TagId.HasValue)
                {
                    domainFilters.Add(new TagIdFilter(f.TagId.Value, f.UserName));
                }
                else if (!string.IsNullOrWhiteSpace(f.TagName))
                {
                    domainFilters.Add(new TagNameFilter(f.TagName, f.UserName));
                }
            }
        }

        // ソートリストを作成
        var domainSorts = req.Sorts?.Select(s => new ItemListSort(s.TagId, s.Ascending)).ToList()
            ?? [];

        ItemListPageData pageData = await _itemListDataProvider
            .LoadItemsAndTagsAsync(domainFilters, domainSorts, currentUserId, isAdmin);

        int totalCount = pageData.Items.Count;

        // ページネーション適用
        int skip = Math.Max(0, req.Skip);
        int take = req.Take > 0 ? req.Take : 50;

        List<Item> rawPagedItems = pageData.Items
            .Skip(skip)
            .Take(take)
            .ToList();

        var itemIds = rawPagedItems.Select(i => i.Id).ToList();
        var (replyCounts, quoteCounts) = await _itemListDataProvider
            .GetReplyAndQuoteCountsAsync(itemIds, cancellationToken);

        List<ItemDto> pagedItems = rawPagedItems
            .Select(i => MapToItemDto(i, replyCounts.GetValueOrDefault(i.Id), quoteCounts.GetValueOrDefault(i.Id)))
            .ToList();

        List<TagDto> tagDtos = pageData.Tags
            .Select(MapToTagDto)
            .ToList();

        var response = new ItemListQueryResponse(
            Items: pagedItems,
            Tags: tagDtos,
            TotalCount: totalCount
        );

        return Ok(response);
    }

    private static ItemDto MapToItemDto(Item item, int replyCount = 0, int quoteCount = 0)
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
            ReplyCount: replyCount,
            QuoteCount: quoteCount,
            QuotedItemContent: item.QuotedItem?.Content,
            QuotedItemOwnerUserName: item.QuotedItem?.Owner?.UserName,
            TargetUserGroupName: item.TargetUserGroup?.Name
        );
    }

    private static TagDto MapToTagDto(Tag tag)
    {
        var targetRelations = tag.TargetTagRelations?
            .Where(tr => tr.Tag is { IsSystem: false })
            .Select(tr => new TagRelationDto(
                TagId: tr.TagId,
                TagName: tr.Tag?.Name ?? string.Empty,
                Weight: tr.Weight,
                OwnerUserName: tr.Owner?.UserName ?? tr.Tag?.Owner?.UserName
            ))
            .ToList();

        return new TagDto(
            Id: tag.Id,
            Name: tag.Name,
            Content: tag.Content,
            OwnerUserName: tag.Owner?.UserName,
            OwnerUserId: tag.OwnerId,
            Score: TagCardViewModel.GetTagScore(tag),
            IsPrivate: tag.IsPrivate,
            TargetUserGroupName: tag.TargetUserGroup?.Name,
            TargetTagRelations: targetRelations
        );
    }
}