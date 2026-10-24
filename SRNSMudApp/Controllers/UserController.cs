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
using SRNSMudApp.Components.UI;
using SRNSMudApp.Data;
using SRNSMudApp.Services;

/// <summary>
/// ユーザープロフィールおよびフォロー機能に関する Web API コントローラー。
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class UserController(IUserDataProvider userDataProvider) : ControllerBase
{
    private readonly IUserDataProvider _userDataProvider =
        userDataProvider ?? throw new ArgumentNullException(nameof(userDataProvider));

    /// <summary>
    /// ユーザーの詳細情報（プロフィール、投稿アイテム、作成タグ、フォロー関係）を取得します。
    /// </summary>
    [HttpGet("{userId}")]
    public async Task<ActionResult<UserDetailDto>> GetUserDetail(
        string userId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return BadRequest("UserId は必須です。");
        }

        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        UserDetailPageData data = await _userDataProvider.GetUserDetailAsync(userId, currentUserId);

        if (data.User == null)
        {
            return NotFound("ユーザーが見つかりません。");
        }

        var itemDtos = data.UserItems.Select(i => new ItemDto(
            Id: i.Id,
            Content: i.Content,
            OwnerUserName: data.User.UserName,
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

        var tagDtos = data.UserTags.Select(t => new TagDto(
            Id: t.Id,
            Name: t.Name,
            Content: t.Content,
            OwnerUserName: data.User.UserName,
            OwnerUserId: t.OwnerId,
            Score: TagCardViewModel.GetTagScore(t),
            IsPrivate: t.IsPrivate,
            TargetUserGroupName: t.TargetUserGroup?.Name
        )).ToList();

        var dto = new UserDetailDto(
            UserId: data.User.Id,
            UserName: data.User.UserName ?? string.Empty,
            IsBanned: data.User.IsBanned,
            IsFollowing: data.IsFollowing,
            FollowingCount: data.FollowingCount,
            FollowersCount: data.FollowersCount,
            Items: itemDtos,
            Tags: tagDtos
        );

        return Ok(dto);
    }

    /// <summary>
    /// ユーザーのフォロー状態を切り替えます。
    /// </summary>
    [HttpPost("{userId}/follow")]
    public async Task<ActionResult<ToggleFollowResponse>> ToggleFollow(
        string userId,
        CancellationToken cancellationToken)
    {
        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized("フォロー操作を行うにはログインが必要です。");
        }

        if (string.Equals(currentUserId, userId, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("自分自身をフォローすることはできません。");
        }

        bool isFollowing = await _userDataProvider.ToggleFollowUserAsync(currentUserId, userId);
        return Ok(new ToggleFollowResponse(isFollowing));
    }

    /// <summary>
    /// ユーザーを検索します。
    /// </summary>
    [HttpGet("search")]
    public async Task<ActionResult<IReadOnlyList<UserDto>>> SearchUsers(
        [FromQuery] string? q,
        CancellationToken cancellationToken)
    {
        List<ApplicationUser> users = await _userDataProvider.SearchUsersAsync(q, cancellationToken);
        var dtos = users.Select(u => new UserDto(u.Id, u.UserName ?? string.Empty)).ToList();
        return Ok(dtos);
    }

    /// <summary>
    /// 管理者用ユーザー管理一覧を取得します（管理者専用）。
    /// </summary>
    [HttpGet("management")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<IReadOnlyList<ManagedUserDto>>> GetManagementUsers(
        CancellationToken cancellationToken)
    {
        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        List<ApplicationUser> users = await _userDataProvider.GetAllUsersAsync() ?? [];
        List<ManagedUserDto> dtos = [];

        foreach (var user in users)
        {
            bool isAdmin = await _userDataProvider.IsUserInRoleAsync(user, "Admin");
            bool isProtected = string.Equals(user.Id, currentUserId, StringComparison.Ordinal)
                || string.Equals(user.UserName, "system", StringComparison.OrdinalIgnoreCase);

            dtos.Add(new ManagedUserDto(
                UserId: user.Id,
                UserName: user.UserName ?? string.Empty,
                IsAdmin: isAdmin,
                IsBanned: user.IsBanned,
                IsProtected: isProtected
            ));
        }

        return Ok(dtos);
    }

    /// <summary>
    /// ユーザーの管理者（Admin）権限を付与または剥奪します（管理者専用）。
    /// </summary>
    [HttpPut("{userId}/admin")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<bool>> SetUserAdmin(
        string userId,
        [FromBody] SetUserAdminRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        ApplicationUser? targetUser = await _userDataProvider.FindUserByIdAsync(userId);
        if (targetUser == null)
        {
            return NotFound("ユーザーが見つかりません。");
        }

        bool isProtected = string.Equals(targetUser.Id, currentUserId, StringComparison.Ordinal)
            || string.Equals(targetUser.UserName, "system", StringComparison.OrdinalIgnoreCase);

        if (isProtected && !request.IsAdmin)
        {
            return BadRequest("保護されたユーザーの管理者権限は剥奪できません。");
        }

        var result = await _userDataProvider.UpdateUserAdminRoleAsync(userId, request.IsAdmin);
        if (!result.Succeeded)
        {
            return BadRequest(string.Join(", ", result.Errors.Select(e => e.Description)));
        }

        return Ok(true);
    }

    /// <summary>
    /// ユーザーの利用停止（BAN）状態を設定または解除します（管理者専用）。
    /// </summary>
    [HttpPut("{userId}/ban")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<bool>> SetUserBan(
        string userId,
        [FromBody] SetUserBanRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        ApplicationUser? targetUser = await _userDataProvider.FindUserByIdAsync(userId);
        if (targetUser == null)
        {
            return NotFound("ユーザーが見つかりません。");
        }

        bool isProtected = string.Equals(targetUser.Id, currentUserId, StringComparison.Ordinal)
            || string.Equals(targetUser.UserName, "system", StringComparison.OrdinalIgnoreCase);

        if (isProtected && request.IsBanned)
        {
            return BadRequest("保護されたユーザーを利用停止にすることはできません。");
        }

        var result = await _userDataProvider.SetUserBanStatusAsync(userId, request.IsBanned);
        if (!result.Succeeded)
        {
            return BadRequest(string.Join(", ", result.Errors.Select(e => e.Description)));
        }

        return Ok(true);
    }

    /// <summary>
    /// 全ユーザーのメール確認（EmailConfirmed）状態一覧を取得します（管理者専用）。
    /// </summary>
    [HttpGet("confirmed-accounts")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<IReadOnlyList<UserConfirmationDto>>> GetConfirmedAccounts(
        CancellationToken cancellationToken)
    {
        List<ApplicationUser> users = await _userDataProvider.GetAllUsersAsync() ?? [];
        var dtos = users.Select(u => new UserConfirmationDto(
            UserId: u.Id,
            UserName: u.UserName,
            Email: u.Email,
            EmailConfirmed: u.EmailConfirmed
        )).ToList();

        return Ok(dtos);
    }

    /// <summary>
    /// 指定したユーザーのメール確認（EmailConfirmed）状態を反転します（管理者専用）。
    /// </summary>
    [HttpPost("{userId}/toggle-confirmation")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<bool>> ToggleUserConfirmation(
        string userId,
        CancellationToken cancellationToken)
    {
        var targetUser = await _userDataProvider.FindUserByIdAsync(userId);
        if (targetUser == null)
        {
            return NotFound("ユーザーが見つかりません。");
        }

        var result = await _userDataProvider.ToggleUserEmailConfirmationAsync(userId);
        if (!result.Succeeded)
        {
            return BadRequest(string.Join(", ", result.Errors.Select(e => e.Description)));
        }

        var updated = await _userDataProvider.FindUserByIdAsync(userId);
        return Ok(updated?.EmailConfirmed ?? !targetUser.EmailConfirmed);
    }
}