namespace SRNSMudApp.Controllers;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SRNSMudApp.Client.Models.Api;
using SRNSMudApp.Data;
using SRNSMudApp.Services;

/// <summary>
/// 管理者向け招待コード管理に関する Web API コントローラー。
/// </summary>
[ApiController]
[Route("api/admin/invitation")]
[Authorize(Roles = "Admin")]
public sealed class InvitationController(
    IAdminDataProvider adminDataProvider,
    TimeProvider? timeProvider = null) : ControllerBase
{
    private const string CodeCharacters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    private readonly IAdminDataProvider _adminDataProvider =
        adminDataProvider ?? throw new ArgumentNullException(nameof(adminDataProvider));
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    /// <summary>
    /// 登録済みの招待一覧を取得します。
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<InvitationDto>>> GetInvitations(CancellationToken cancellationToken)
    {
        List<Invitation> invitations = await _adminDataProvider.GetInvitationsAsync();
        DateTime now = _timeProvider.GetUtcNow().UtcDateTime;

        var dtos = invitations.Select(i => MapToDto(i, now)).ToList();
        return Ok(dtos);
    }

    /// <summary>
    /// 新規招待コードを発行します。
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<InvitationDto>> CreateInvitation(
        [FromBody] CreateInvitationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return BadRequest("メールアドレスは必須です。");
        }

        string currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "system";
        string code = RandomNumberGenerator.GetString(CodeCharacters, 16);
        DateTime now = _timeProvider.GetUtcNow().UtcDateTime;

        var invitation = new Invitation
        {
            Email = request.Email.Trim(),
            InvitationCode = code,
            ExpirationDate = now.AddDays(7),
            IsUsed = false,
            InvitedByAdminId = currentUserId,
            OwnerId = currentUserId
        };

        await _adminDataProvider.CreateInvitationAsync(invitation);
        return Ok(MapToDto(invitation, now));
    }

    /// <summary>
    /// 指定された招待を削除します。
    /// </summary>
    [HttpDelete("{id:int}")]
    public async Task<ActionResult<bool>> DeleteInvitation(int id, CancellationToken cancellationToken)
    {
        List<Invitation> invitations = await _adminDataProvider.GetInvitationsAsync();
        Invitation? target = invitations.FirstOrDefault(i => i.Id == id);

        if (target is null)
        {
            return NotFound("招待が見つかりません。");
        }

        await _adminDataProvider.DeleteInvitationAsync(target);
        return Ok(true);
    }

    private static InvitationDto MapToDto(Invitation invitation, DateTime utcNow)
    {
        string status = invitation.IsUsed
            ? "Used"
            : invitation.ExpirationDate < utcNow ? "Expired" : "Pending";

        return new InvitationDto(
            Id: invitation.Id,
            Email: invitation.Email,
            InvitationCode: invitation.InvitationCode,
            ExpirationDate: invitation.ExpirationDate,
            IsUsed: invitation.IsUsed,
            Status: status,
            InvitedByAdminId: invitation.InvitedByAdminId,
            CreatedDate: invitation.CreatedDate
        );
    }
}