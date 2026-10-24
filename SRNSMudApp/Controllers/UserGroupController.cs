namespace SRNSMudApp.Controllers;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;

using SRNSMudApp.Client.Models.Api;
using SRNSMudApp.Data;
using SRNSMudApp.Services;

/// <summary>
/// ユーザーグループの作成・更新・削除およびメンバー管理に関する API を提供するコントローラー。
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class UserGroupController(
    IUserGroupDataProvider userGroupDataProvider,
    IUserDataProvider? userDataProvider = null) : ControllerBase
{
    private readonly IUserGroupDataProvider _userGroupDataProvider =
        userGroupDataProvider ?? throw new ArgumentNullException(nameof(userGroupDataProvider));
    private readonly IUserDataProvider? _userDataProvider = userDataProvider;

    /// <summary>
    /// ログインユーザーが所属または管理するグループ一覧を取得します。
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UserGroupDto>>> GetUserGroups(CancellationToken cancellationToken)
    {
        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Ok((IReadOnlyList<UserGroupDto>)[]);
        }

        IReadOnlyList<UserGroup> groups = await _userGroupDataProvider
            .GetUserGroupsForUserAsync(currentUserId, cancellationToken);

        var dtos = groups.Select(g => new UserGroupDto(g.Id, g.Name, g.Description)).ToList();
        return Ok(dtos);
    }

    /// <summary>
    /// ログインユーザーが管理（オーナー）するグループ一覧を取得します。
    /// </summary>
    [HttpGet("managed")]
    public async Task<ActionResult<IReadOnlyList<UserGroupDto>>> GetManagedGroups(CancellationToken cancellationToken)
    {
        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Ok((IReadOnlyList<UserGroupDto>)[]);
        }

        IReadOnlyList<UserGroup> groups = await _userGroupDataProvider
            .GetManagedGroupsAsync(currentUserId, cancellationToken);

        var dtos = groups.Select(g => new UserGroupDto(g.Id, g.Name, g.Description)).ToList();
        return Ok(dtos);
    }

    /// <summary>
    /// 指定されたグループの詳細情報（メンバー一覧を含む）を取得します。
    /// </summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<UserGroupDetailDto>> GetUserGroupById(int id, CancellationToken cancellationToken)
    {
        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized("グループ詳細を取得するにはログインが必要です。");
        }

        UserGroup? group = await _userGroupDataProvider.GetUserGroupByIdAsync(id, cancellationToken);
        if (group == null)
        {
            return NotFound("グループが見つかりません。");
        }

        var isMemberOrOwner = group.OwnerId == currentUserId || group.Members.Any(m => m.UserId == currentUserId);
        var isAdmin = User?.IsInRole("Admin") ?? false;
        if (!isMemberOrOwner && !isAdmin)
        {
            return Forbid();
        }

        var members = group.Members
            .Select(m => new UserGroupMemberDto(m.UserId, m.User?.UserName, m.CreatedDate))
            .ToList();

        var detail = new UserGroupDetailDto(
            Id: group.Id,
            Name: group.Name,
            Description: group.Description,
            OwnerUserId: group.OwnerId,
            OwnerUserName: group.Owner?.UserName,
            Members: members
        );

        return Ok(detail);
    }

    /// <summary>
    /// 新規ユーザーグループを作成します。
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<UserGroupDto>> CreateUserGroup(
        [FromBody] CreateUserGroupRequest request,
        CancellationToken cancellationToken)
    {
        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized("グループを作成するにはログインが必要です。");
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("グループ名は必須です。");
        }

        UserGroup created = await _userGroupDataProvider
            .CreateUserGroupAsync(request.Name.Trim(), request.Description?.Trim(), currentUserId, cancellationToken);

        return Ok(new UserGroupDto(created.Id, created.Name, created.Description));
    }

    /// <summary>
    /// ユーザーグループの基本情報（名前、説明）を更新します（オーナーのみ）。
    /// </summary>
    [HttpPut("{id:int}")]
    public async Task<ActionResult> UpdateUserGroup(
        int id,
        [FromBody] UpdateUserGroupRequest request,
        CancellationToken cancellationToken)
    {
        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized("グループを更新するにはログインが必要です。");
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("グループ名は必須です。");
        }

        bool success = await _userGroupDataProvider
            .UpdateUserGroupAsync(id, request.Name.Trim(), request.Description?.Trim(), currentUserId, cancellationToken);

        if (!success)
        {
            return BadRequest("グループの更新に失敗しました。オーナー権限が必要です。");
        }

        return Ok();
    }

    /// <summary>
    /// ユーザーグループを削除します（オーナーのみ）。
    /// </summary>
    [HttpDelete("{id:int}")]
    public async Task<ActionResult> DeleteUserGroup(int id, CancellationToken cancellationToken)
    {
        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized("グループを削除するにはログインが必要です。");
        }

        bool success = await _userGroupDataProvider.DeleteUserGroupAsync(id, currentUserId, cancellationToken);
        if (!success)
        {
            return BadRequest("グループの削除に失敗しました。オーナー権限が必要です。");
        }

        return Ok();
    }

    /// <summary>
    /// ユーザーグループにメンバーを追加します（オーナーのみ）。
    /// </summary>
    [HttpPost("{id:int}/members")]
    public async Task<ActionResult> AddMember(
        int id,
        [FromBody] AddGroupMemberRequest request,
        CancellationToken cancellationToken)
    {
        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized("メンバーを追加するにはログインが必要です。");
        }

        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            return BadRequest("UserId は必須です。");
        }

        bool success = await _userGroupDataProvider.AddMemberAsync(id, request.UserId, currentUserId, cancellationToken);
        if (!success)
        {
            return BadRequest("メンバーの追加に失敗しました。オーナー権限が必要です。");
        }

        return Ok();
    }

    /// <summary>
    /// ユーザーグループからメンバーを削除します（オーナーまたはメンバー本人のみ）。
    /// </summary>
    [HttpDelete("{id:int}/members/{userId}")]
    public async Task<ActionResult> RemoveMember(int id, string userId, CancellationToken cancellationToken)
    {
        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized("メンバーを削除するにはログインが必要です。");
        }

        bool success = await _userGroupDataProvider.RemoveMemberAsync(id, userId, currentUserId, cancellationToken);
        if (!success)
        {
            return BadRequest("メンバーの削除に失敗しました。");
        }

        return Ok();
    }

    /// <summary>
    /// グループメンバー追加候補のユーザーを検索します。
    /// </summary>
    [HttpGet("search-users")]
    public async Task<ActionResult<IReadOnlyList<UserDto>>> SearchUsers([FromQuery] string? q, CancellationToken cancellationToken)
    {
        if (_userDataProvider == null || string.IsNullOrWhiteSpace(q))
        {
            return Ok((IReadOnlyList<UserDto>)[]);
        }

        List<ApplicationUser> users = await _userDataProvider.SearchUsersAsync(q.Trim(), cancellationToken);
        var dtos = users.Select(u => new UserDto(u.Id, u.UserName)).ToList();
        return Ok(dtos);
    }
}