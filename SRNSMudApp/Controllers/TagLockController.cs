namespace SRNSMudApp.Controllers;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SRNSMudApp.Client.Models.Api;
using SRNSMudApp.Services;

using ClientTagLockItemDto = SRNSMudApp.Client.Models.Api.TagLockItemDto;

/// <summary>
/// 管理者向けタグロック・階層管理に関する Web API コントローラー。
/// </summary>
[ApiController]
[Route("api/admin/tag-lock")]
[Authorize(Roles = "Admin")]
public sealed class TagLockController(ITagLockService tagLockService) : ControllerBase
{
    private readonly ITagLockService _tagLockService =
        tagLockService ?? throw new ArgumentNullException(nameof(tagLockService));

    /// <summary>
    /// 現在設定されている階層ロックレベル（n階層目までロック）を取得します。
    /// </summary>
    [HttpGet("level")]
    public async Task<ActionResult<int>> GetLockedHierarchyLevel(CancellationToken cancellationToken)
    {
        int level = await _tagLockService.GetLockedHierarchyLevelAsync(cancellationToken);
        return Ok(level);
    }

    /// <summary>
    /// 階層ロックレベル（n階層目までロック）を設定・更新します。
    /// </summary>
    [HttpPut("level")]
    public async Task<ActionResult<int>> SetLockedHierarchyLevel(
        [FromBody] SetHierarchyLockLevelRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // 関係パターンマッチングによる階層範囲チェック
        if (request.Level is < 0 or > 20)
        {
            return BadRequest("ロック階層は 0 以上 20 以下で指定してください。");
        }

        await _tagLockService.SetLockedHierarchyLevelAsync(request.Level, cancellationToken);
        return Ok(request.Level);
    }

    /// <summary>
    /// タグ管理画面向けに、全タグの階層・ロック状態を含む一覧を取得します。
    /// </summary>
    [HttpGet("tags")]
    public async Task<ActionResult<IReadOnlyList<ClientTagLockItemDto>>> GetAllTagsWithLockStatus(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<SRNSMudApp.Services.TagLockItemDto> serverItems =
            await _tagLockService.GetAllTagsWithLockStatusAsync(cancellationToken);

        var dtos = serverItems.Select(s => new ClientTagLockItemDto(
            s.Id,
            s.Name,
            s.Level,
            s.ParentTagId,
            s.ParentTagName,
            s.OwnerUserName,
            s.IsDirectlyLocked,
            s.IsLevelLocked,
            s.IsSiblingLocked,
            s.IsLockedEffective
        )).ToList();

        return Ok(dtos);
    }

    /// <summary>
    /// タグ個別のロック状態をトグル（反転）します。
    /// </summary>
    [HttpPost("{id:int}/toggle")]
    public async Task<ActionResult<bool>> ToggleTagLock(int id, CancellationToken cancellationToken)
    {
        // 関係パターンマッチングによるID正値チェック
        if (id is <= 0)
        {
            return BadRequest("有効なタグIDを指定してください。");
        }

        bool isLocked = await _tagLockService.ToggleTagLockAsync(id, cancellationToken);
        return Ok(isLocked);
    }
}