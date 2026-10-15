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
using SRNSMudApp.Services;

/// <summary>
/// 不適切なコンテンツ（アイテム・タグ）の通報および通報管理に関する API を提供するコントローラー。
/// </summary>
[ApiController]
[Route("api/report")]
public sealed class ReportController(
    IContentReportService contentReportService) : ControllerBase
{
    private readonly IContentReportService _contentReportService =
        contentReportService ?? throw new ArgumentNullException(nameof(contentReportService));

    /// <summary>
    /// コンテンツ（アイテムまたはタグ）に対する新しい通報を作成します。
    /// </summary>
    [HttpPost]
    [Authorize]
    public async Task<ActionResult<ContentReportDto>> CreateReport(
        [FromBody] CreateReportRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized("ユーザー情報が取得できません。");
        }

        ReportTargetType targetType = string.Equals(request.TargetType, "Tag", StringComparison.OrdinalIgnoreCase)
            ? ReportTargetType.Tag
            : ReportTargetType.Item;

        var dto = new CreateContentReportDto
        {
            TargetType = targetType,
            ItemId = request.ItemId,
            TagId = request.TagId,
            Reason = request.Reason,
            Detail = request.Detail
        };

        ContentReport report = await _contentReportService.CreateReportAsync(dto, currentUserId);
        return Ok(MapToDto(report));
    }

    /// <summary>
    /// 通報一覧を取得します（管理者専用）。
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<IReadOnlyList<ContentReportDto>>> GetReports(
        [FromQuery] string? status = null,
        [FromQuery] string? targetType = null,
        CancellationToken cancellationToken = default)
    {
        ReportStatus? filterStatus = !string.IsNullOrWhiteSpace(status) && Enum.TryParse<ReportStatus>(status, true, out var s) ? s : null;
        ReportTargetType? filterTargetType = !string.IsNullOrWhiteSpace(targetType) && Enum.TryParse<ReportTargetType>(targetType, true, out var t) ? t : null;

        List<ContentReport> reports = await _contentReportService.GetReportsAsync(filterStatus, filterTargetType);
        var dtos = reports.Select(MapToDto).ToList();
        return Ok(dtos);
    }

    /// <summary>
    /// 指定された通報の詳細情報を取得します（管理者専用）。
    /// </summary>
    [HttpGet("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ContentReportDto>> GetReportById(
        int id,
        CancellationToken cancellationToken)
    {
        ContentReport? report = await _contentReportService.GetReportByIdAsync(id);
        if (report is null)
        {
            return NotFound();
        }

        return Ok(MapToDto(report));
    }

    /// <summary>
    /// 通報のステータスおよび管理者メモを更新します（管理者専用）。
    /// </summary>
    [HttpPut("{id:int}/status")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<bool>> UpdateReportStatus(
        int id,
        [FromBody] UpdateReportStatusRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized("ユーザー情報が取得できません。");
        }

        if (!Enum.TryParse<ReportStatus>(request.Status, true, out var newStatus))
        {
            return BadRequest($"無効なステータスです: {request.Status}");
        }

        bool success = await _contentReportService.UpdateReportStatusAsync(id, newStatus, request.ResolutionNote, currentUserId);
        if (!success)
        {
            return NotFound();
        }

        return Ok(true);
    }

    /// <summary>
    /// 通報に対する処置（非公開化または削除）を実行します（管理者専用）。
    /// </summary>
    [HttpPost("{id:int}/resolve")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<bool>> ResolveReport(
        int id,
        [FromBody] ResolveReportRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized("ユーザー情報が取得できません。");
        }

        // 三項非同期式による処置分岐の簡潔化
        bool success = request.HideTarget
            ? await _contentReportService.ResolveReportWithHideAsync(id, request.ResolutionNote, currentUserId)
            : await _contentReportService.ResolveReportWithActionAsync(id, request.DeleteTarget, request.ResolutionNote, currentUserId);

        if (!success)
        {
            return NotFound();
        }

        return Ok(true);
    }

    private static ContentReportDto MapToDto(ContentReport report)
    {
        return new(
            Id: report.Id,
            TargetType: report.TargetType.ToString(),
            ItemId: report.ItemId,
            TagId: report.TagId,
            TargetContentSnapshot: report.TargetContentSnapshot,
            Reason: report.Reason,
            Detail: report.Detail,
            Status: report.Status.ToString(),
            ResolutionNote: report.ResolutionNote,
            ReporterUserId: report.OwnerId,
            ReporterUserName: report.Owner?.UserName,
            HandledByAdminId: report.HandledByAdminId,
            HandledByAdminName: report.HandledByAdmin?.UserName,
            CreatedDate: report.CreatedDate,
            HandledDate: report.HandledDate
        );
    }
}