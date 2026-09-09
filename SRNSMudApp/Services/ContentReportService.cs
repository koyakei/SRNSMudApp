#region

using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services.Commands;
using SRNSMudApp.Services.Reports;

#endregion

namespace SRNSMudApp.Services;

/// <summary>
///     通報作成用 DTO。
/// </summary>
public record CreateContentReportDto
{
    /// <summary>通報対象種別（Item または Tag）</summary>
    public required ReportTargetType TargetType { get; init; }

    /// <summary>対象アイテムID（Item の場合）</summary>
    public int? ItemId { get; init; }

    /// <summary>対象タグID（Tag の場合）</summary>
    public int? TagId { get; init; }

    /// <summary>通報理由カテゴリ</summary>
    public required string Reason { get; init; }

    /// <summary>通報者による詳細コメント</summary>
    public string? Detail { get; init; }
}

/// <summary>
///     不適切なコンテンツ（アイテム・タグ）の通報および通報管理のドメインサービスインターフェース。
/// </summary>
public interface IContentReportService
{
    /// <summary>
    ///     新しい通報を作成する。通報時点のコンテンツ内容をスナップショットとして自動保存する。
    /// </summary>
    /// <param name="dto">通報内容。</param>
    /// <param name="reporterUserId">通報を行ったユーザーのID。</param>
    /// <returns>作成された ContentReport。</returns>
    Task<ContentReport> CreateReportAsync(CreateContentReportDto dto, string reporterUserId);

    /// <summary>
    ///     通報一覧を取得する。ステータスおよび対象種別による絞り込みが可能。
    /// </summary>
    /// <param name="status">絞り込みステータス（null の場合はすべて）。</param>
    /// <param name="targetType">絞り込み対象種別（null の場合はすべて）。</param>
    /// <returns>通報エンティティのリスト。</returns>
    Task<List<ContentReport>> GetReportsAsync(ReportStatus? status = null, ReportTargetType? targetType = null);

    /// <summary>
    ///     指定した通報の最新情報を取得する。
    /// </summary>
    /// <param name="reportId">通報ID。</param>
    /// <returns>通報エンティティ（存在しない場合は null）。</returns>
    Task<ContentReport?> GetReportByIdAsync(int reportId);

    /// <summary>
    ///     通報のステータスおよび管理者メモを更新する。
    /// </summary>
    /// <param name="reportId">通報ID。</param>
    /// <param name="status">新しいステータス。</param>
    /// <param name="resolutionNote">管理者メモ・解決理由。</param>
    /// <param name="adminUserId">対応を行った管理者のユーザーID。</param>
    /// <returns>更新に成功した場合は true、通報が存在しない場合は false。</returns>
    Task<bool> UpdateReportStatusAsync(int reportId, ReportStatus status, string? resolutionNote, string adminUserId);

    /// <summary>
    ///     通報への処置を実行し、必要に応じて対象コンテンツを削除してステータスを ActionTaken に設定する。
    /// </summary>
    /// <param name="reportId">通報ID。</param>
    /// <param name="deleteTarget">対象コンテンツを削除するかどうか。</param>
    /// <param name="resolutionNote">管理者メモ・処置内容。</param>
    /// <param name="adminUserId">対応を行った管理者のユーザーID。</param>
    /// <returns>処理に成功した場合は true、通報が存在しない場合は false。</returns>
    Task<bool> ResolveReportWithActionAsync(int reportId, bool deleteTarget, string? resolutionNote, string adminUserId);

    /// <summary>
    ///     通報記録を削除する。
    /// </summary>
    /// <param name="reportId">通報ID。</param>
    /// <returns>削除に成功した場合は true、通報が存在しない場合は false。</returns>
    Task<bool> DeleteReportAsync(int reportId);
}

/// <summary>
///     不適切なコンテンツの通報および通報管理の具象サービス。
///     スナップショット生成・削除は Strategy (IReportTargetHandler) に委譲し、
///     処置確定処理は Command Handler (ResolveContentReportCommand) へ委譲する。
/// </summary>
public class ContentReportService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    IReportTargetHandlerFactory handlerFactory,
    ICommandHandler<ResolveContentReportCommand, Result<bool>> resolveCommandHandler)
    : IContentReportService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));

    private readonly IReportTargetHandlerFactory _handlerFactory =
        handlerFactory ?? throw new ArgumentNullException(nameof(handlerFactory));

    private readonly ICommandHandler<ResolveContentReportCommand, Result<bool>> _resolveCommandHandler =
        resolveCommandHandler ?? throw new ArgumentNullException(nameof(resolveCommandHandler));

    /// <inheritdoc />
    public async Task<ContentReport> CreateReportAsync(CreateContentReportDto dto, string reporterUserId)
    {
        ArgumentNullException.ThrowIfNull(dto);
        ArgumentException.ThrowIfNullOrWhiteSpace(reporterUserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(dto.Reason);

        var targetId = dto.TargetType switch
        {
            ReportTargetType.Item => dto.ItemId ?? throw new ArgumentException("Item を対象とする通報には ItemId が必須です。", nameof(dto)),
            ReportTargetType.Tag => dto.TagId ?? throw new ArgumentException("Tag を対象とする通報には TagId が必須です。", nameof(dto)),
            _ => throw new NotSupportedException($"未対応の通報対象種別です: {dto.TargetType}")
        };

        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();

        // Strategy パターンにより対象エンティティごとのスナップショットを取得
        IReportTargetHandler targetHandler = _handlerFactory.GetHandler(dto.TargetType);
        var snapshot = await targetHandler.CaptureSnapshotAsync(context, targetId);

        var report = new ContentReport
        {
            OwnerId = reporterUserId,
            TargetType = dto.TargetType,
            ItemId = dto.ItemId,
            TagId = dto.TagId,
            TargetContentSnapshot = snapshot,
            Reason = dto.Reason,
            Detail = dto.Detail ?? string.Empty,
            Status = ReportStatus.Pending,
            CreatedDate = DateTime.UtcNow,
            UpdatedDate = DateTime.UtcNow
        };

        _ = context.ContentReports.Add(report);
        _ = await context.SaveChangesAsync();

        return report;
    }

    /// <inheritdoc />
    public async Task<List<ContentReport>> GetReportsAsync(ReportStatus? status = null, ReportTargetType? targetType = null)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();

        IQueryable<ContentReport> query = context.ContentReports
            .Include(r => r.Owner)
            .Include(r => r.Item)
            .Include(r => r.Tag)
            .Include(r => r.HandledByAdmin);

        if (status.HasValue)
        {
            query = query.Where(r => r.Status == status.Value);
        }

        if (targetType.HasValue)
        {
            query = query.Where(r => r.TargetType == targetType.Value);
        }

        return await query
            .OrderByDescending(r => r.CreatedDate)
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<ContentReport?> GetReportByIdAsync(int reportId)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();

        return await context.ContentReports
            .Include(r => r.Owner)
            .Include(r => r.Item)
                .ThenInclude(i => i!.Owner)
            .Include(r => r.Tag)
                .ThenInclude(t => t!.Owner)
            .Include(r => r.HandledByAdmin)
            .FirstOrDefaultAsync(r => r.Id == reportId);
    }

    /// <inheritdoc />
    public async Task<bool> UpdateReportStatusAsync(int reportId, ReportStatus status, string? resolutionNote, string adminUserId)
    {
        var command = new ResolveContentReportCommand(reportId, status, DeleteTarget: false, resolutionNote, adminUserId);
        Result<bool> result = await _resolveCommandHandler.HandleAsync(command);
        return result is Success<bool>(true);
    }

    /// <inheritdoc />
    public async Task<bool> ResolveReportWithActionAsync(int reportId, bool deleteTarget, string? resolutionNote, string adminUserId)
    {
        var command = new ResolveContentReportCommand(reportId, ReportStatus.ActionTaken, deleteTarget, resolutionNote, adminUserId);
        Result<bool> result = await _resolveCommandHandler.HandleAsync(command);
        return result is Success<bool>(true);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteReportAsync(int reportId)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();

        ContentReport? report = await context.ContentReports.FindAsync(reportId);
        if (report is null)
        {
            return false;
        }

        _ = context.ContentReports.Remove(report);
        _ = await context.SaveChangesAsync();
        return true;
    }
}