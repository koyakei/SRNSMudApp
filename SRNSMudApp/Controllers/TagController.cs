namespace SRNSMudApp.Controllers;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;

using SRNSMudApp.Client.Models.Api;
using SRNSMudApp.Components.UI;
using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Services;

/// <summary>
/// タグの作成・更新・削除・投票および親タグ関連付けに関する API を提供するコントローラー。
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class TagController(
    ITagCommandService tagCommandService,
    ITagCardDataProvider tagCardDataProvider,
    IHomeDataProvider homeDataProvider,
    ITagTreeDataProvider? tagTreeDataProvider = null,
    ITagDetailDataProvider? tagDetailDataProvider = null,
    ITagSearchQueryService? tagSearchQueryService = null,
    ITagSuggestionService? tagSuggestionService = null) : ControllerBase
{
    private readonly ITagCommandService _tagCommandService =
        tagCommandService ?? throw new ArgumentNullException(nameof(tagCommandService));
    private readonly ITagCardDataProvider _tagCardDataProvider =
        tagCardDataProvider ?? throw new ArgumentNullException(nameof(tagCardDataProvider));
    private readonly IHomeDataProvider _homeDataProvider =
        homeDataProvider ?? throw new ArgumentNullException(nameof(homeDataProvider));
    private readonly ITagTreeDataProvider? _tagTreeDataProvider = tagTreeDataProvider;
    private readonly ITagDetailDataProvider? _tagDetailDataProvider = tagDetailDataProvider;
    private readonly ITagSearchQueryService? _tagSearchQueryService = tagSearchQueryService;
    private readonly ITagSuggestionService? _tagSuggestionService = tagSuggestionService;

    /// <summary>
    /// 新規タグを作成します。
    /// </summary>
    [HttpPost("create")]
    public async Task<ActionResult<TagDto>> CreateTag(
        [FromBody] CreateTagRequest request,
        CancellationToken cancellationToken)
    {
        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized("タグを作成するにはログインが必要です。");
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("タグ名は必須です。");
        }

        var isAdmin = User?.IsInRole("Admin") ?? false;
        var newTag = new Tag
        {
            Name = request.Name.Trim(),
            Content = request.Content?.Trim() ?? string.Empty,
            IsPrivate = request.IsPrivate,
            TargetUserGroupId = request.TargetUserGroupId,
            OwnerId = currentUserId,
            CreatedDate = DateTime.UtcNow,
            UpdatedDate = DateTime.UtcNow
        };

        await _tagCommandService.CreateTagAsync(newTag, isAdmin);

        if (request.ParentTagIds is { Count: > 0 })
        {
            foreach (var parentId in request.ParentTagIds)
            {
                _ = await _tagCardDataProvider.AddTagToTagAsync(newTag.Id, parentId, currentUserId);
            }
        }

        return Ok(MapToTagDto(newTag));
    }

    /// <summary>
    /// タグ情報を更新します。
    /// </summary>
    [HttpPut("{id:int}")]
    public async Task<ActionResult> UpdateTag(
        int id,
        [FromBody] UpdateTagRequest request,
        CancellationToken cancellationToken)
    {
        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized("タグを更新するにはログインが必要です。");
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("タグ名は必須です。");
        }

        var isAdmin = User?.IsInRole("Admin") ?? false;
        bool success = await _tagCommandService.UpdateTagAsync(
            tagId: id,
            name: request.Name.Trim(),
            content: request.Content?.Trim(),
            isAdmin: isAdmin,
            isPrivate: request.IsPrivate,
            targetUserGroupId: request.TargetUserGroupId);

        if (!success)
        {
            return BadRequest("タグの更新に失敗しました。");
        }

        return Ok();
    }

    /// <summary>
    /// タグを削除します。
    /// </summary>
    [HttpDelete("{id:int}")]
    public async Task<ActionResult> DeleteTag(
        int id,
        CancellationToken cancellationToken)
    {
        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized("タグを削除するにはログインが必要です。");
        }

        var isAdmin = User?.IsInRole("Admin") ?? false;
        TagDeleteOperationResult result = await _tagCommandService.DeleteTagAsync(id, currentUserId, isAdmin);

        return result switch
        {
            TagDeleteOperationResult.Success => Ok(),
            TagDeleteOperationResult.NotFound => NotFound("タグが見つかりません。"),
            TagDeleteOperationResult.Unauthorized => Forbid(),
            TagDeleteOperationResult.SystemTag => BadRequest("システムタグは削除できません。"),
            TagDeleteOperationResult.Locked => BadRequest("ロックされているタグは削除できません。"),
            _ => BadRequest("タグの削除に失敗しました。")
        };
    }

    /// <summary>
    /// タグへの投票（アップボート / ダウンボート）を切り替えます。
    /// </summary>
    [HttpPost("{id:int}/vote")]
    public async Task<ActionResult> VoteTag(
        int id,
        [FromBody] TagVoteRequest request,
        CancellationToken cancellationToken)
    {
        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized("投票を行うにはログインが必要です。");
        }

        SystemTagsResult systemTags = await _homeDataProvider
            .EnsureSystemTagsAsync(currentUserId, cancellationToken);

        if (!systemTags.GoodTagId.HasValue || !systemTags.BadTagId.HasValue)
        {
            return BadRequest("システムタグの取得に失敗しました。");
        }

        int targetSystemTagId = request.IsUpvote ? systemTags.GoodTagId.Value : systemTags.BadTagId.Value;
        int oppositeSystemTagId = request.IsUpvote ? systemTags.BadTagId.Value : systemTags.GoodTagId.Value;

        await _tagCardDataProvider.ToggleTagVoteAsync(id, currentUserId, targetSystemTagId, oppositeSystemTagId);
        return Ok();
    }

    /// <summary>
    /// 親タグとの関連付け（TagRelationToTag）を追加します。
    /// </summary>
    [HttpPost("{id:int}/parents")]
    public async Task<ActionResult> AddParentTag(
        int id,
        [FromBody] AddParentTagRequest request,
        CancellationToken cancellationToken)
    {
        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized("親タグを追加するにはログインが必要です。");
        }

        TagCardOperationResult result = await _tagCardDataProvider
            .AddTagToTagAsync(id, request.ParentTagId, currentUserId);

        return result switch
        {
            TagCardOperationResult.Success => Ok(),
            TagCardOperationResult.AlreadyExists => BadRequest("すでに関連付けられています。"),
            TagCardOperationResult.NotFound => NotFound("タグが見つかりません。"),
            TagCardOperationResult.NotOwner => Forbid(),
            _ => BadRequest("親タグの追加に失敗しました。")
        };
    }

    /// <summary>
    /// 親タグとの関連付けを削除します。
    /// </summary>
    [HttpDelete("{id:int}/parents/{relationId:int}")]
    public async Task<ActionResult> RemoveParentTag(
        int id,
        int relationId,
        CancellationToken cancellationToken)
    {
        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized("関連付けを削除するにはログインが必要です。");
        }

        TagCardOperationResult result = await _tagCardDataProvider
            .RemoveRelationAsync(relationId, currentUserId);

        return result switch
        {
            TagCardOperationResult.Success => Ok(),
            TagCardOperationResult.AlreadyExists => BadRequest("すでに関連付けられています。"),
            TagCardOperationResult.NotFound => NotFound("関連付けが見つかりません。"),
            TagCardOperationResult.NotOwner => Forbid(),
            _ => BadRequest("関連付けの削除に失敗しました。")
        };
    }

    /// <summary>
    /// タグの階層構造（ツリー構造）を取得します。
    /// </summary>
    [HttpGet("tree")]
    public async Task<ActionResult<IReadOnlyList<TagTreeNodeDto>>> GetTagTree(CancellationToken cancellationToken)
    {
        if (_tagTreeDataProvider == null)
        {
            return Ok(Array.Empty<TagTreeNodeDto>());
        }

        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        bool isAdmin = User?.IsInRole("Admin") ?? false;

        List<Tag> tags = await _tagTreeDataProvider.LoadTagsAsync(currentUserId, isAdmin);

        var lookup = tags.ToLookup(t => t.ParentTagId);

        IReadOnlyList<TagTreeNodeDto> BuildNodes(int? parentId)
        {
            return lookup[parentId].Select(t => new TagTreeNodeDto(
                Id: t.Id,
                Name: t.Name,
                Content: t.Content,
                ParentTagId: t.ParentTagId,
                Score: TagCardViewModel.GetTagScore(t),
                OwnerUserName: t.Owner?.UserName,
                Children: BuildNodes(t.Id)
            )).ToList();
        }

        IReadOnlyList<TagTreeNodeDto> tree = BuildNodes(null);
        return Ok(tree);
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

    /// <summary>
    /// タグの詳細情報を取得します。
    /// </summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<TagDetailDto>> GetTagDetail(
        int id,
        CancellationToken cancellationToken)
    {
        if (_tagDetailDataProvider == null)
        {
            return StatusCode(Microsoft.AspNetCore.Http.StatusCodes.Status501NotImplemented);
        }

        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        bool isAdmin = User?.IsInRole("Admin") ?? false;

        TagDetailPageData? pageData = await _tagDetailDataProvider.GetTagDetailAsync(id, currentUserId, isAdmin);
        if (pageData?.Tag == null)
        {
            return NotFound("タグが見つかりません。");
        }

        Tag tag = pageData.Tag;

        var relatedItems = pageData.RelatedItems.Select(i => new ItemDto(
            Id: i.Id,
            Content: i.Content,
            OwnerUserName: i.Owner?.UserName,
            UpdatedDate: i.UpdatedDate,
            IsPrivate: i.IsPrivate,
            TagRelations: i.TagRelations.Select(tr => new TagRelationDto(
                TagId: tr.TagId,
                TagName: tr.Tag?.Name ?? string.Empty,
                Weight: tr.Weight,
                OwnerUserName: tr.Owner?.UserName ?? tr.Tag?.Owner?.UserName
            )).ToList(),
            ItemKindJson: i.ItemKindJson,
            ParentItemId: i.ParentItemId,
            RootItemId: i.RootItemId,
            QuotedItemId: i.QuotedItemId,
            OwnerUserId: i.OwnerId,
            Score: ItemCardViewModel.GetItemScore(i.TagRelations)
        )).ToList();

        var relatedTags = pageData.RelatedTags.Select(MapToTagDto).ToList();

        var pendingRequests = pageData.PendingRequests.Select(r => new TaggingRequestDto(
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

        var dto = new TagDetailDto(
            Id: tag.Id,
            Name: tag.Name,
            Content: tag.Content,
            OwnerUserId: tag.OwnerId,
            OwnerUserName: tag.Owner?.UserName,
            Score: TagCardViewModel.GetTagScore(tag),
            IsPrivate: tag.IsPrivate,
            TargetUserGroupName: tag.TargetUserGroup?.Name,
            IsFollowing: pageData.IsFollowing,
            AutoAcceptIncomingTaggingRequests: tag.AutoAcceptIncomingTaggingRequests,
            IsLocked: tag.IsLocked,
            RelatedItems: relatedItems,
            RelatedTags: relatedTags,
            PendingRequests: pendingRequests
        );

        return Ok(dto);
    }

    /// <summary>
    /// タグのフォロー状態を切り替えます。
    /// </summary>
    [HttpPost("{id:int}/follow")]
    public async Task<ActionResult<ToggleFollowResponse>> ToggleFollow(
        int id,
        CancellationToken cancellationToken)
    {
        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized("フォロー操作を行うにはログインが必要です。");
        }

        if (_tagDetailDataProvider == null)
        {
            return StatusCode(Microsoft.AspNetCore.Http.StatusCodes.Status501NotImplemented);
        }

        bool isFollowing = await _tagDetailDataProvider.ToggleFollowAsync(id, currentUserId);
        return Ok(new ToggleFollowResponse(isFollowing));
    }

    /// <summary>
    /// タグ一覧（検索クエリによる絞り込み可）を取得します。
    /// </summary>
    [HttpGet("list")]
    public async Task<ActionResult<IReadOnlyList<TagDto>>> GetTagList(
        [FromQuery] string? q,
        CancellationToken cancellationToken)
    {
        if (_tagSearchQueryService == null)
        {
            return StatusCode(Microsoft.AspNetCore.Http.StatusCodes.Status501NotImplemented);
        }

        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        bool isAdmin = User?.IsInRole("Admin") ?? false;

        List<Tag> tags = string.IsNullOrWhiteSpace(q)
            ? await _tagSearchQueryService.GetTagsWithDetailsAsync(currentUserId, isAdmin)
            : await _tagSearchQueryService.SearchTagsAsync(q.Trim(), currentUserId, isAdmin);

        var dtos = tags.Select(MapToTagDto).ToList();
        return Ok(dtos);
    }

    /// <summary>
    /// 入力テキストから関連タグを推論・提案します。
    /// </summary>
    [HttpPost("suggestions")]
    public async Task<ActionResult<IReadOnlyList<SuggestedTagDto>>> SuggestTags(
        [FromBody] TagSuggestionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tagSuggestionService == null)
        {
            return StatusCode(Microsoft.AspNetCore.Http.StatusCodes.Status501NotImplemented);
        }

        if (string.IsNullOrWhiteSpace(request.Content))
        {
            return Ok(Array.Empty<SuggestedTagDto>());
        }

        float minScore = request.MinScore ?? SRNSMudApp.Models.SuggestedTag.DefaultCandidateThreshold;
        IReadOnlyList<SRNSMudApp.Models.SuggestedTag> suggestions =
            await _tagSuggestionService.SuggestTagsAsync(request.Content.Trim(), minScore, cancellationToken);

        var dtos = suggestions.Select(s => new SuggestedTagDto(s.TagId, s.TagName, s.Score)).ToList();
        return Ok(dtos);
    }

    /// <summary>
    /// 指定されたタグの重み変動台帳（TagWeightLedger）履歴を取得します。
    /// </summary>
    [HttpGet("{id:int}/ledger")]
    public async Task<ActionResult<IReadOnlyList<TagWeightLedgerDto>>> GetTagLedger(
        int id,
        CancellationToken cancellationToken)
    {
        if (_tagDetailDataProvider == null)
        {
            return StatusCode(Microsoft.AspNetCore.Http.StatusCodes.Status501NotImplemented);
        }

        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        bool isAdmin = User?.IsInRole("Admin") ?? false;

        TagDetailPageData? pageData = await _tagDetailDataProvider.GetTagDetailAsync(id, currentUserId, isAdmin);
        if (pageData?.Tag == null)
        {
            return NotFound("タグが見つかりません。");
        }

        var dtos = pageData.WeightLedgers.Select(l => new TagWeightLedgerDto(
            Id: l.Id,
            TagId: l.TagId,
            TagName: l.Tag?.Name ?? (!string.IsNullOrEmpty(l.TagNameSnapshot) ? l.TagNameSnapshot : pageData.Tag.Name),
            ItemId: l.ItemId,
            OwnerUserId: l.OwnerId,
            OwnerUserName: l.Owner?.UserName,
            Reason: l.Reason,
            SourceType: l.SourceType,
            PreviousWeight: l.PreviousWeight,
            Delta: l.Delta,
            NewWeight: l.NewWeight,
            CreatedDate: l.CreatedDate
        )).ToList();

        return Ok(dtos);
    }
}