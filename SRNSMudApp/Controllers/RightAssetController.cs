namespace SRNSMudApp.Controllers;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SRNSMudApp.Client.Models.Api;
using SRNSMudApp.Models;
using SRNSMudApp.Services;

/// <summary>
/// RightAsset の状況・保有データに関する API を提供するコントローラー。
/// </summary>
[ApiController]
[Route("api/right-asset")]
public sealed class RightAssetController(
    IRightAssetDataProvider rightAssetDataProvider) : ControllerBase
{
    private readonly IRightAssetDataProvider _rightAssetDataProvider =
        rightAssetDataProvider ?? throw new ArgumentNullException(nameof(rightAssetDataProvider));

    /// <summary>
    /// RightAsset が発行されている代表的なタグ一覧を取得します。
    /// </summary>
    [HttpGet("top-tags")]
    [AllowAnonymous]
    public async Task<ActionResult<IReadOnlyList<TagRightAssetSummaryDto>>> GetTopTags(
        [FromQuery] int count = 10,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<TagRightAssetSummary> summaries =
            await _rightAssetDataProvider.GetTopTagsWithRightAssetsAsync(count, cancellationToken);

        var dtos = summaries.Select(s => new TagRightAssetSummaryDto(
            TagId: s.TagId,
            TagName: s.TagName,
            TagContent: s.TagContent,
            TotalAmount: s.TotalAmount,
            HolderCount: s.HolderCount
        )).ToList();

        return Ok(dtos);
    }

    /// <summary>
    /// 指定されたタグの RightAsset 保有状況および詳細アセット一覧を取得します。
    /// </summary>
    [HttpGet("overview/{tagId:int}")]
    [AllowAnonymous]
    public async Task<ActionResult<RightAssetOverviewResponse>> GetOverview(
        int tagId,
        CancellationToken cancellationToken = default)
    {
        RightAssetOverviewData? overview =
            await _rightAssetDataProvider.GetRightAssetOverviewByTagIdAsync(tagId, cancellationToken);

        if (overview is null)
        {
            return NotFound();
        }

        var holders = overview.Holders.Select(h => new RightAssetHolderDto(
            UserId: h.UserId,
            UserName: h.UserName,
            TotalAmount: h.TotalAmount,
            ActiveAssetCount: h.ActiveAssetCount,
            BurnedAmount: h.BurnedAmount,
            BurnedAssetCount: h.BurnedAssetCount,
            LastUpdated: h.LastUpdated
        )).ToList();

        var assets = overview.Assets.Select(a => new RightAssetItemDto(
            Id: a.Id,
            OwnerId: a.OwnerId,
            OwnerName: a.OwnerName,
            Amount: a.Amount,
            IsBurned: a.IsBurned,
            StatusSummary: a.StatusSummary,
            CreatedDate: a.CreatedDate,
            UpdatedDate: a.UpdatedDate
        )).ToList();

        var response = new RightAssetOverviewResponse(
            TagId: overview.Tag.Id,
            TagName: overview.Tag.Name,
            TagContent: overview.Tag.Content,
            TagWeight: overview.Tag.CachedWeight,
            IsSystem: overview.Tag.IsSystem,
            TotalHoldersCount: overview.TotalHoldersCount,
            TotalActiveAmount: overview.TotalActiveAmount,
            TotalActiveAssetsCount: overview.TotalActiveAssetsCount,
            TotalBurnedAmount: overview.TotalBurnedAmount,
            Holders: holders,
            Assets: assets
        );

        return Ok(response);
    }
}