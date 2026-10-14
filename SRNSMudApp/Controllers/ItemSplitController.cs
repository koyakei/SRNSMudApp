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
/// アイテムのテキスト分割および分割リクエストを処理する Web API コントローラー。
/// </summary>
[ApiController]
[Route("api/item-split")]
public sealed class ItemSplitController(IItemSplitService itemSplitService) : ControllerBase
{
    private readonly IItemSplitService _itemSplitService =
        itemSplitService ?? throw new ArgumentNullException(nameof(itemSplitService));

    /// <summary>
    /// アイテム所有者として、選択テキストを新規アイテムへ直接分割します。
    /// </summary>
    [HttpPost("direct")]
    [Authorize]
    public async Task<ActionResult<ItemDto>> DirectSplit(
        [FromBody] DirectSplitItemRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized("分割を実行するにはログインが必要です。");
        }

        if (string.IsNullOrWhiteSpace(request.SelectedText))
        {
            return BadRequest("分割対象のテキストを指定してください。");
        }

        Result<Item> result = await _itemSplitService.SplitDirectlyAsync(
            request.OriginalItemId,
            request.SelectedText.Trim(),
            currentUserId,
            cancellationToken);

        switch (result)
        {
            case Success<Item> success:
                return Ok(MapToItemDto(success.Value));
            case Failure failure:
                return BadRequest(failure.ErrorMessage);
            default:
                return StatusCode(500, "アイテムの直接分割中に予期しないエラーが発生しました。");
        }
    }

    /// <summary>
    /// 他ユーザーのアイテムに対し、テキスト分割リクエストを申請します。
    /// </summary>
    [HttpPost("request")]
    [Authorize]
    public async Task<ActionResult<ItemSplitRequestDto>> RequestSplit(
        [FromBody] RequestItemSplitRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized("分割リクエストを申請するにはログインが必要です。");
        }

        if (string.IsNullOrWhiteSpace(request.SelectedText))
        {
            return BadRequest("分割対象のテキストを指定してください。");
        }

        Result<ItemSplitRequest> result = await _itemSplitService.RequestSplitAsync(
            request.OriginalItemId,
            request.SelectedText.Trim(),
            currentUserId,
            cancellationToken);

        switch (result)
        {
            case Success<ItemSplitRequest> success:
                return Ok(MapToSplitRequestDto(success.Value));
            case Failure failure:
                return BadRequest(failure.ErrorMessage);
            default:
                return StatusCode(500, "分割リクエストの申請中に予期しないエラーが発生しました。");
        }
    }

    /// <summary>
    /// 指定されたアイテムに対する保留中の分割リクエスト一覧を取得します。
    /// </summary>
    [HttpGet("item/{itemId:int}/pending")]
    public async Task<ActionResult<IReadOnlyList<ItemSplitRequestDto>>> GetPendingSplitRequests(
        int itemId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ItemSplitRequest> requests =
            await _itemSplitService.GetPendingSplitRequestsForOriginalItemAsync(itemId, cancellationToken);

        var dtos = requests.Select(MapToSplitRequestDto).ToList();
        return Ok(dtos);
    }

    /// <summary>
    /// アイテム所有者として分割リクエストを承認します。
    /// </summary>
    [HttpPost("{requestId:int}/approve")]
    [Authorize]
    public async Task<ActionResult<ItemDto>> ApproveSplit(
        int requestId,
        CancellationToken cancellationToken)
    {
        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized();
        }

        Result<Item> result = await _itemSplitService.ApproveSplitAsync(requestId, currentUserId, cancellationToken);
        switch (result)
        {
            case Success<Item> success:
                return Ok(MapToItemDto(success.Value));
            case Failure failure:
                return BadRequest(failure.ErrorMessage);
            default:
                return StatusCode(500, "分割リクエストの承認中に予期しないエラーが発生しました。");
        }
    }

    /// <summary>
    /// アイテム所有者として分割リクエストを却下します。
    /// </summary>
    [HttpPost("{requestId:int}/reject")]
    [Authorize]
    public async Task<ActionResult<bool>> RejectSplit(
        int requestId,
        [FromBody] RejectSplitRequest? request,
        CancellationToken cancellationToken)
    {
        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized();
        }

        Result<bool> result = await _itemSplitService.RejectSplitAsync(
            requestId,
            currentUserId,
            request?.RejectReason?.Trim(),
            cancellationToken);

        switch (result)
        {
            case Success<bool>:
                return Ok(true);
            case Failure failure:
                return BadRequest(failure.ErrorMessage);
            default:
                return StatusCode(500, "分割リクエストの却下中に予期しないエラーが発生しました。");
        }
    }

    /// <summary>
    /// 申請した分割リクエストを取り下げます（申請者本人のみ）。
    /// </summary>
    [HttpPost("{requestId:int}/cancel")]
    [Authorize]
    public async Task<ActionResult<bool>> CancelSplit(
        int requestId,
        CancellationToken cancellationToken)
    {
        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized();
        }

        Result<bool> result = await _itemSplitService.CancelSplitAsync(requestId, currentUserId, cancellationToken);
        switch (result)
        {
            case Success<bool>:
                return Ok(true);
            case Failure failure:
                return BadRequest(failure.ErrorMessage);
            default:
                return StatusCode(500, "分割リクエストの取り下げ中に予期しないエラーが発生しました。");
        }
    }

    private static ItemDto MapToItemDto(Item entity)
    {
        var tagRelationDtos = (entity.TagRelations ?? [])
            .Select(tr => new TagRelationDto(
                TagId: tr.TagId,
                TagName: tr.Tag?.Name ?? string.Empty,
                Weight: tr.Weight,
                OwnerUserName: tr.Owner?.UserName ?? tr.Tag?.Owner?.UserName
            )).ToList();

        return new ItemDto(
            Id: entity.Id,
            Content: entity.Content,
            OwnerUserName: entity.Owner?.UserName,
            UpdatedDate: entity.UpdatedDate,
            IsPrivate: entity.IsPrivate,
            TagRelations: tagRelationDtos,
            ItemKindJson: entity.ItemKindJson ?? "{}",
            ParentItemId: entity.ParentItemId,
            RootItemId: entity.RootItemId,
            QuotedItemId: entity.QuotedItemId,
            OwnerUserId: entity.OwnerId
        );
    }

    private static ItemSplitRequestDto MapToSplitRequestDto(ItemSplitRequest entity) => new(
        Id: entity.Id,
        OriginalItemId: entity.OriginalItemId,
        SelectedText: entity.SelectedText,
        RequesterUserId: entity.RequesterUserId,
        RequesterUserName: entity.RequesterUser?.UserName,
        OwnerUserId: entity.OwnerUserId,
        Status: entity.Status.ToString(),
        RejectReason: entity.RejectReason,
        CreatedDate: entity.CreatedDate
    );
}