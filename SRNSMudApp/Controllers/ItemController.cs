namespace SRNSMudApp.Controllers;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using SRNSMudApp.Client.Models;
using SRNSMudApp.Client.Models.Api;
using SRNSMudApp.Components.Item;
using SRNSMudApp.Components.UI;
using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;
using SRNSMudApp.Services.Commands;

using ReactionTagNames = SRNSMudApp.Models.ReactionTagNames;

/// <summary>
/// ItemList の一覧表示やタグ検索に関する API を提供するコントローラー。
/// </summary>
[ApiController]
[Route("api/[controller]")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1508:Avoid dead code", Justification = "Result<T> union type pattern matching")]
public sealed class ItemController(
    IItemListDataProvider itemListDataProvider,
    IItemListExportService itemListExportService,
    IHomeDataProvider homeDataProvider,
    IItemReactionService itemReactionService,
    IItemCardDataProvider itemCardDataProvider,
    IItemReplyService itemReplyService,
    IItemQuoteService itemQuoteService,
    IItemTagService itemTagService,
    IUserGroupDataProvider userGroupDataProvider,
    IAdminDataProvider adminDataProvider,
    ICommandHandler<ApproveTaggingRequestCommand, Result<string>>? approveCommandHandler = null,
    ITagRequestRejectionService? tagRequestRejectionService = null,
    IItemDetailDataProvider? itemDetailDataProvider = null) : ControllerBase
{
    private readonly IItemListDataProvider _itemListDataProvider =
        itemListDataProvider ?? throw new ArgumentNullException(nameof(itemListDataProvider));
    private readonly IItemListExportService _itemListExportService =
        itemListExportService ?? throw new ArgumentNullException(nameof(itemListExportService));
    private readonly IHomeDataProvider _homeDataProvider =
        homeDataProvider ?? throw new ArgumentNullException(nameof(homeDataProvider));
    private readonly IItemReactionService _itemReactionService =
        itemReactionService ?? throw new ArgumentNullException(nameof(itemReactionService));
    private readonly IItemCardDataProvider _itemCardDataProvider =
        itemCardDataProvider ?? throw new ArgumentNullException(nameof(itemCardDataProvider));
    private readonly IItemReplyService _itemReplyService =
        itemReplyService ?? throw new ArgumentNullException(nameof(itemReplyService));
    private readonly IItemQuoteService _itemQuoteService =
        itemQuoteService ?? throw new ArgumentNullException(nameof(itemQuoteService));
    private readonly IItemTagService _itemTagService =
        itemTagService ?? throw new ArgumentNullException(nameof(itemTagService));
    private readonly IUserGroupDataProvider _userGroupDataProvider =
        userGroupDataProvider ?? throw new ArgumentNullException(nameof(userGroupDataProvider));
    private readonly IAdminDataProvider _adminDataProvider =
        adminDataProvider ?? throw new ArgumentNullException(nameof(adminDataProvider));
    private readonly ICommandHandler<ApproveTaggingRequestCommand, Result<string>>? _approveCommandHandler = approveCommandHandler;
    private readonly ITagRequestRejectionService? _tagRequestRejectionService = tagRequestRejectionService;
    private readonly IItemDetailDataProvider? _itemDetailDataProvider = itemDetailDataProvider;

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
            return Ok((IReadOnlyList<TagSuggestion>)[]);
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
        ItemListQueryRequest req = request ?? new();

        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var isAdmin = User?.IsInRole("Admin") ?? false;

        // リクエスト DTO から宣言的 LINQ とパターンマッチングによりドメインフィルタリストを構築
        var domainFilters = (req.Filters ?? [])
            .Select<ItemListFilterDto, ItemListFilter?>(f => f switch
            {
                { TagId: { } tagId } => new TagIdFilter(tagId, f.UserName),
                { TagName: { Length: > 0 } tagName } when !string.IsNullOrWhiteSpace(tagName) => new TagNameFilter(tagName, f.UserName),
                _ => null
            })
            .OfType<ItemListFilter>()
            .ToList();

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

    /// <summary>
    /// アイテムの詳細情報を取得します。
    /// </summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<ItemDto>> GetItemById(int id, CancellationToken cancellationToken)
    {
        Item? item = await _itemCardDataProvider.GetItemByIdAsync(id);
        if (item == null)
        {
            return NotFound("アイテムが見つかりません。");
        }

        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var isAdmin = User?.IsInRole("Admin") ?? false;

        // 非公開アイテムの認可判定 (Issue 3-9: Blazor Server と同一の可視性拡張ロジックを適用)
        if (item.IsPrivate && item.OwnerId != currentUserId && !isAdmin)
        {
            if (_itemDetailDataProvider != null)
            {
                ItemDetailPageData? detail = await _itemDetailDataProvider.GetItemDetailAsync(id, currentUserId, isAdmin, cancellationToken);
                if (detail == null)
                {
                    return Forbid();
                }
            }
            else
            {
                return Forbid();
            }
        }

        int replyCount = await _itemReplyService.GetItemReplyCountAsync(id);
        int quoteCount = await _itemQuoteService.GetQuoteCountAsync(id, cancellationToken);

        return Ok(MapToItemDto(item, replyCount, quoteCount));
    }

    private static ItemDto MapToItemDto(Item item, int replyCount = 0, int quoteCount = 0)
    {
        var relations = item.TagRelations
            .Select(tr => new TagRelationDto(
                TagId: tr.TagId,
                TagName: tr.Tag?.Name ?? string.Empty,
                Weight: tr.Weight,
                OwnerUserName: tr.Owner?.UserName ?? tr.Tag?.Owner?.UserName,
                RelationId: tr.Id
            ))
            .ToList();

        return new(
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

        return new(
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

    /// <summary>
    /// 指定されたアイテム一覧を JSON 形式でエクスポートします。
    /// </summary>
    [HttpPost("export")]
    public async Task<ActionResult<string>> ExportItems(
        [FromBody] ItemListExportRequest? request,
        CancellationToken cancellationToken)
    {
        if (request?.ItemIds is null || request.ItemIds.Count == 0)
        {
            return Ok("[]");
        }

        ItemListExportData exportData = await _itemListDataProvider
            .LoadExportDataAsync(request.ItemIds);

        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var isAdmin = User?.IsInRole("Admin") ?? false;
        ItemListPageData pageData = await _itemListDataProvider
            .LoadItemsAndTagsAsync([], [], currentUserId, isAdmin);

        var targetItems = pageData.Items.Where(i => request.ItemIds.Contains(i.Id)).ToList();
        IReadOnlyList<ExportItemDto> exportList = await _itemListExportService
            .BuildExportAsync(exportData, targetItems);

        string json = ItemListExportService.Serialize(exportList);
        return Ok(json);
    }

    /// <summary>
    /// アイテムへの投票（アップボート / ダウンボート）を切り替えます。
    /// </summary>
    [HttpPost("{id:int}/vote")]
    public async Task<ActionResult<ItemVoteResult>> VoteItem(
        int id,
        [FromBody] ItemVoteRequest request,
        CancellationToken cancellationToken)
    {
        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized("投票を行うにはログインが必要です。");
        }

        SystemTagsResult systemTags = await _homeDataProvider
            .EnsureSystemTagsAsync(currentUserId, cancellationToken);

        if (!systemTags.GoodTagId.HasValue)
        {
            return BadRequest("システムタグの取得に失敗しました。");
        }

        var targetWeight = request.IsUpvote ? 1 : -1;
        ItemVoteResult result = await _itemReactionService
            .ToggleItemVoteAsync(id, currentUserId, systemTags.GoodTagId.Value, targetWeight);

        return Ok(result);
    }

    /// <summary>
    /// アイテムへのリアクション（真実・善・美）を切り替えます。
    /// </summary>
    [HttpPost("{id:int}/reaction")]
    public async Task<ActionResult<ItemVoteResult>> ReactItem(
        int id,
        [FromBody] ItemReactionRequest request,
        CancellationToken cancellationToken)
    {
        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized("リアクションを行うにはログインが必要です。");
        }

        if (string.IsNullOrWhiteSpace(request.ReactionTagName))
        {
            return BadRequest("ReactionTagName は必須です。");
        }

        Tag reactionTag = await _itemReactionService
            .EnsureReactionTagAsync(currentUserId, request.ReactionTagName);

        var targetWeight = request.Weight != 0 ? request.Weight : 1;
        ItemVoteResult result = await _itemReactionService
            .ToggleItemReactionAsync(id, currentUserId, reactionTag.Id, targetWeight);

        return Ok(result);
    }

    /// <summary>
    /// 新規アイテムを作成します。
    /// </summary>
    [HttpPost("create")]
    public async Task<ActionResult<ItemDto>> CreateItem(
        [FromBody] CreateItemRequest request,
        CancellationToken cancellationToken)
    {
        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized("アイテムを作成するにはログインが必要です。");
        }

        if (string.IsNullOrWhiteSpace(request.Content))
        {
            return BadRequest("Content は必須です。");
        }

        if (request.Content.Length > 1000)
        {
            return BadRequest("Content は1000文字以内で入力してください。");
        }

        var item = new Item
        {
            Content = request.Content.Trim(),
            OwnerId = currentUserId,
            IsPrivate = request.IsPrivate,
            QuotedItemId = request.QuotedItemId,
            TargetUserGroupId = request.TargetUserGroupId,
            CreatedDate = DateTime.UtcNow,
            UpdatedDate = DateTime.UtcNow
        };

        await _itemCardDataProvider.CreateItemAsync(item, request.InitialTagIds);

        Item? createdItem = await _itemCardDataProvider.GetItemByIdAsync(item.Id);
        return Ok(MapToItemDto(createdItem ?? item, 0, 0));
    }

    /// <summary>
    /// アイテムのリプライ一覧を取得します。
    /// </summary>
    [HttpGet("{id:int}/replies")]
    public async Task<ActionResult<IReadOnlyList<ItemDto>>> GetReplies(
        int id,
        CancellationToken cancellationToken)
    {
        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var isAdmin = User?.IsInRole("Admin") ?? false;
        IReadOnlyList<Item> replies = await _itemReplyService.GetItemRepliesAsync(id, currentUserId, isAdmin);
        var replyDtos = (replies ?? []).Select(r => MapToItemDto(r, 0, 0)).ToList();
        return Ok(replyDtos);
    }

    /// <summary>
    /// アイテムにリプライを投稿します。
    /// </summary>
    [HttpPost("{id:int}/replies")]
    public async Task<ActionResult<ItemDto>> AddReply(
        int id,
        [FromBody] CreateReplyRequest request,
        CancellationToken cancellationToken)
    {
        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized("リプライを投稿するにはログインが必要です。");
        }

        if (string.IsNullOrWhiteSpace(request.Content))
        {
            return BadRequest("Content は必須です。");
        }

        if (request.Content.Length > 1000)
        {
            return BadRequest("Content は1000文字以内で入力してください。");
        }

        Item? parentItem = await _itemCardDataProvider.GetItemByIdAsync(id);
        if (parentItem == null)
        {
            return NotFound("親アイテムが見つかりません。");
        }

        bool isPrivate = request.IsPrivate;
        int? targetGroupId = isPrivate ? parentItem.TargetUserGroupId : null;

        Item? reply = await _itemReplyService.AddItemReplyAsync(
            id,
            request.Content.Trim(),
            currentUserId,
            request.TargetUserIds,
            isPrivate,
            targetGroupId);

        if (reply == null)
        {
            return BadRequest("リプライの作成に失敗しました。");
        }

        return Ok(MapToItemDto(reply, 0, 0));
    }

    /// <summary>
    /// アイテムを引用しているアイテム一覧を取得します。
    /// </summary>
    [HttpGet("{id:int}/quotes")]
    public async Task<ActionResult<IReadOnlyList<ItemDto>>> GetQuotes(
        int id,
        CancellationToken cancellationToken)
    {
        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var isAdmin = User?.IsInRole("Admin") ?? false;
        IReadOnlyList<Item> quotes = await _itemQuoteService.GetQuotedByItemsAsync(id, currentUserId, isAdmin, cancellationToken);
        var quoteDtos = (quotes ?? []).Select(q => MapToItemDto(q, 0, 0)).ToList();
        return Ok(quoteDtos);
    }

    /// <summary>
    /// アイテム本文を更新します。
    /// </summary>
    [HttpPut("{id:int}")]
    public async Task<ActionResult> UpdateItem(
        int id,
        [FromBody] UpdateItemRequest request,
        CancellationToken cancellationToken)
    {
        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized("アイテムを更新するにはログインが必要です。");
        }

        if (string.IsNullOrWhiteSpace(request.Content))
        {
            return BadRequest("Content は必須です。");
        }

        if (request.Content.Length > 1000)
        {
            return BadRequest("Content は1000文字以内で入力してください。");
        }

        Item? item = await _itemCardDataProvider.GetItemByIdAsync(id);
        if (item == null)
        {
            return NotFound("アイテムが見つかりません。");
        }

        var isAdmin = User?.IsInRole("Admin") ?? false;
        if (item.OwnerId != currentUserId && !isAdmin)
        {
            return Forbid();
        }

        bool success = await _itemCardDataProvider.UpdateItemContentAsync(id, request.Content.Trim());
        if (!success)
        {
            return BadRequest("アイテムの更新に失敗しました。");
        }

        return Ok();
    }

    /// <summary>
    /// アイテムを削除します。
    /// </summary>
    [HttpDelete("{id:int}")]
    public async Task<ActionResult> DeleteItem(
        int id,
        CancellationToken cancellationToken)
    {
        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized("アイテムを削除するにはログインが必要です。");
        }

        Item? item = await _itemCardDataProvider.GetItemByIdAsync(id);
        if (item == null)
        {
            return NotFound("アイテムが見つかりません。");
        }

        var isAdmin = User?.IsInRole("Admin") ?? false;
        if (item.OwnerId != currentUserId && !isAdmin)
        {
            return Forbid();
        }

        if (isAdmin && item.OwnerId != currentUserId)
        {
            await _itemCardDataProvider.DeleteItemByAdminAsync(id, currentUserId);
        }
        else
        {
            await _itemCardDataProvider.DeleteItemAsync(id);
        }

        return Ok();
    }

    /// <summary>
    /// アイテムにタグを追加します。
    /// </summary>
    [HttpPost("{id:int}/tags")]
    public async Task<ActionResult> AddTag(
        int id,
        [FromBody] AddTagRelationRequest request,
        CancellationToken cancellationToken)
    {
        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized("タグを追加するにはログインが必要です。");
        }

        string? error = await _itemTagService.AddTagToItemAsync(id, request.TagId, currentUserId, request.Comment);
        if (error != null)
        {
            return BadRequest(error);
        }

        return Ok();
    }

    /// <summary>
    /// アイテムからタグ関連付けを削除します。
    /// </summary>
    [HttpDelete("{id:int}/tags/{relationId:int}")]
    public async Task<ActionResult> RemoveTag(
        int id,
        int relationId,
        CancellationToken cancellationToken)
    {
        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized("タグを削除するにはログインが必要です。");
        }

        string? error = await _itemTagService.RemoveTagRelationAsync(relationId, currentUserId);
        if (error != null)
        {
            return BadRequest(error);
        }

        return Ok();
    }

    /// <summary>
    /// アイテムに紐づくタグ関連付けのタグを変更します。
    /// </summary>
    [HttpPut("{id:int}/tags/{relationId:int}/change")]
    public async Task<ActionResult> ChangeItemTag(
        int id,
        int relationId,
        [FromBody] ChangeItemTagRequest request,
        CancellationToken cancellationToken)
    {
        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized("タグを変更するにはログインが必要です。");
        }

        string? error = await _itemTagService.ChangeItemTagAsync(relationId, request.NewTagId, id, currentUserId);
        if (error != null)
        {
            return BadRequest(error);
        }

        return Ok();
    }

    /// <summary>
    /// ログインユーザーが所属・管理するグループ一覧を取得します。
    /// </summary>
    [HttpGet("user-groups")]
    public async Task<ActionResult<IReadOnlyList<UserGroupDto>>> GetUserGroups(
        CancellationToken cancellationToken)
    {
        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Ok((IReadOnlyList<UserGroupDto>)[]);
        }

        IReadOnlyList<UserGroup> groups = await _userGroupDataProvider.GetUserGroupsForUserAsync(currentUserId, cancellationToken);
        var dtos = groups.Select(g => new UserGroupDto(g.Id, g.Name, g.Description)).ToList();
        return Ok(dtos);
    }

    /// <summary>
    /// アイテムに紐づくタグのウェイト（重み）を変更します。
    /// </summary>
    [HttpPut("{id:int}/tags/{relationId:int}/weight")]
    public async Task<ActionResult> UpdateTagWeight(
        int id,
        int relationId,
        [FromBody] UpdateTagWeightRequest request,
        CancellationToken cancellationToken)
    {
        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized("ウェイトを変更するにはログインが必要です。");
        }

        UpdateWeightResult result = await _itemTagService.UpdateTagWeightAsync(relationId, request.Delta, currentUserId);
        return result switch
        {
            UpdateWeightResult.Success => Ok(),
            UpdateWeightResult.NotFound => NotFound("タグ関連付けが見つかりません。"),
            UpdateWeightResult.NoPermission => Forbid(),
            _ => BadRequest("ウェイトの変更に失敗しました。")
        };
    }

    /// <summary>
    /// 指定アイテムを引用しているアイテム一覧を取得します。
    /// </summary>
    [HttpGet("{id:int}/quoted-by")]
    public async Task<ActionResult<IReadOnlyList<ItemDto>>> GetQuotedByItems(
        int id,
        CancellationToken cancellationToken)
    {
        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var isAdmin = User?.IsInRole("Admin") ?? false;
        IReadOnlyList<Item> items = await _itemQuoteService.GetQuotedByItemsAsync(id, currentUserId, isAdmin, cancellationToken);
        var dtos = (items ?? []).Select(i => MapToItemDto(i)).ToList();
        return Ok(dtos);
    }

    /// <summary>
    /// CSV 形式のアイテムおよびタグデータをインポートします。
    /// </summary>
    [HttpPost("import")]
    public async Task<ActionResult<ImportCsvResponse>> ImportCsv(
        [FromBody] ImportCsvRequest request,
        CancellationToken cancellationToken)
    {
        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized("インポートを行うにはログインが必要です。");
        }

        if (string.IsNullOrWhiteSpace(request.CsvContent))
        {
            return BadRequest("インポートするデータが空です。");
        }

        using MemoryStream stream = new(System.Text.Encoding.UTF8.GetBytes(request.CsvContent));
        var linesToProcess = await ItemImportViewModel.ParseCsvStreamAsync(stream, cancellationToken);
        if (linesToProcess.Count == 0)
        {
            return Ok(new ImportCsvResponse(0, "処理するデータがありませんでした。"));
        }

        int importedCount = await _adminDataProvider.ImportItemsWithTagsAsync(currentUserId, linesToProcess);
        return Ok(new ImportCsvResponse(importedCount, $"{importedCount}件のアイテムをインポートしました。"));
    }

    /// <summary>
    /// アイテムに紐づく保留中のタグ付けリクエスト一覧を取得します。
    /// </summary>
    [HttpGet("{id:int}/tag-requests")]
    public async Task<ActionResult<IReadOnlyList<TaggingRequestDto>>> GetTaggingRequests(
        int id,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<TaggingRequestEntity> requests = await _itemTagService.GetTaggingRequestsForItemAsync(id);
        var dtos = requests.Select(r => new TaggingRequestDto(
            r.Id,
            r.TargetItemId,
            r.RequestedTagId,
            r.RequestedTag?.Name ?? string.Empty,
            r.RequesterUserId,
            r.Owner?.UserName,
            r.TagOwnerUserId,
            r.ProposedWeight,
            r.Status.ToString(),
            r.RequestType.ToString()
        )).ToList();
        return Ok(dtos);
    }

    /// <summary>
    /// タグ付けリクエストを承認します。
    /// </summary>
    [HttpPost("tag-requests/{requestId:int}/approve")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Quality", "CA1508:Avoid dead code", Justification = "Result<string> pattern matching false positive")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1508:Avoid dead code", Justification = "Result<string> pattern matching false positive")]
    public async Task<ActionResult> ApproveTaggingRequest(
        int requestId,
        CancellationToken cancellationToken)
    {
        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized("リクエストを承認するにはログインが必要です。");
        }

        if (_approveCommandHandler == null)
        {
            return StatusCode(StatusCodes.Status501NotImplemented);
        }

        Result<string> result = await _approveCommandHandler.HandleAsync(
            new ApproveTaggingRequestCommand(requestId, currentUserId), cancellationToken);

        return result switch
        {
            Success<string> => Ok(),
            Failure failure => BadRequest(failure.ErrorMessage),
            _ => BadRequest("リクエストの承認に失敗しました。")
        };
    }

    /// <summary>
    /// タグ付けリクエストを却下します。
    /// </summary>
    [HttpPost("tag-requests/{requestId:int}/reject")]
    public async Task<ActionResult> RejectTaggingRequest(
        int requestId,
        [FromBody] RejectTaggingRequest request,
        CancellationToken cancellationToken)
    {
        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized("リクエストを却下するにはログインが必要です。");
        }

        if (_tagRequestRejectionService == null)
        {
            return StatusCode(StatusCodes.Status501NotImplemented);
        }

        await _tagRequestRejectionService.RejectRequestAsync(requestId, currentUserId, request.Reason);
        return Ok();
    }

    /// <summary>
    /// 指定されたアイテムのタグ重み変動台帳（TagWeightLedger）履歴を取得します。
    /// </summary>
    [HttpGet("{id:int}/tag-ledger")]
    public async Task<ActionResult<IReadOnlyList<TagWeightLedgerDto>>> GetItemTagLedger(
        int id,
        CancellationToken cancellationToken)
    {
        if (_itemDetailDataProvider == null)
        {
            return StatusCode(StatusCodes.Status501NotImplemented);
        }

        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        bool isAdmin = User?.IsInRole("Admin") ?? false;

        ItemDetailPageData? pageData = await _itemDetailDataProvider.GetItemDetailAsync(id, currentUserId, isAdmin, cancellationToken);
        if (pageData?.Item == null)
        {
            return NotFound("アイテムが見つかりません。");
        }

        var dtos = pageData.Ledgers.Select(l => new TagWeightLedgerDto(
            Id: l.Id,
            TagId: l.TagId,
            TagName: l.Tag?.Name ?? (!string.IsNullOrEmpty(l.TagNameSnapshot) ? l.TagNameSnapshot : string.Empty),
            ItemId: l.ItemId,
            OwnerUserId: l.OwnerId,
            OwnerUserName: l.Owner?.UserName,
            Reason: l.Reason,
            SourceType: l.SourceType,
            PreviousWeight: l.PreviousWeight,
            Delta: l.Delta,
            NewWeight: l.NewWeight,
            CreatedDate: l.CreatedDate,
            CommentItemId: l.EffectiveCommentItem?.Id,
            Comment: l.EffectiveCommentItem?.Content
        )).ToList();

        return Ok(dtos);
    }
}