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
using SRNSMudApp.Models;
using SRNSMudApp.Services;

/// <summary>
/// ユーザー通知に関する Web API コントローラー。
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class NotificationController(INotificationService notificationService) : ControllerBase
{
    private readonly INotificationService _notificationService =
        notificationService ?? throw new ArgumentNullException(nameof(notificationService));

    /// <summary>
    /// ログインユーザーの通知一覧を取得します。
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<NotificationItemDto>>> GetNotifications(CancellationToken cancellationToken)
    {
        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized("通知を取得するにはログインが必要です。");
        }

        IReadOnlyList<NotificationDto> notifications = await _notificationService.GetUserNotificationsAsync(currentUserId);
        var dtos = notifications.Select(n => new NotificationItemDto(
            SourceId: n.SourceId,
            SourceType: n.Kind.SourceType,
            Message: n.Message,
            CreatedAt: n.CreatedAt,
            IsRead: n.IsRead,
            ActorName: n.ActorName,
            AssociatedItemId: n.AssociatedItemId,
            HighlightTagId: n.HighlightTagId,
            TargetPath: n.TargetUrl.ToHref()
        )).ToList();

        return Ok(dtos);
    }

    /// <summary>
    /// ログインユーザーの未読通知件数を取得します。
    /// </summary>
    [HttpGet("unread-count")]
    public async Task<ActionResult<UnreadCountResponse>> GetUnreadCount(CancellationToken cancellationToken)
    {
        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized("未読件数を取得するにはログインが必要です。");
        }

        int unread = await _notificationService.GetUnreadCountAsync(currentUserId);
        return Ok(new UnreadCountResponse(unread));
    }

    /// <summary>
    /// 指定された通知を既読化します。
    /// </summary>
    [HttpPost("{sourceId:int}/read")]
    public async Task<ActionResult> MarkAsRead(
        int sourceId,
        [FromBody] MarkAsReadRequest request,
        CancellationToken cancellationToken)
    {
        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized("既読化を行うにはログインが必要です。");
        }

        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.SourceType))
        {
            return BadRequest("SourceType は必須です。");
        }

        await _notificationService.MarkAsReadAsync(currentUserId, sourceId, request.SourceType);
        return Ok();
    }

    /// <summary>
    /// すべての通知を一括で既読化します。
    /// </summary>
    [HttpPost("read-all")]
    public async Task<ActionResult> MarkAllAsRead(CancellationToken cancellationToken)
    {
        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized("一括既読化を行うにはログインが必要です。");
        }

        await _notificationService.MarkAllAsReadAsync(currentUserId);
        return Ok();
    }
}