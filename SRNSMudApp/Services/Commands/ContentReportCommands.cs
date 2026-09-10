#region

using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services.Reports;

#endregion

namespace SRNSMudApp.Services.Commands;

/// <summary>
///     通報に対する管理者の処置・審査結果を確定するコマンド。
/// </summary>
/// <param name="ReportId">通報ID。</param>
/// <param name="Status">変更先ステータス (ActionTaken, Reviewed, Dismissed など)。</param>
/// <param name="DeleteTarget">対象コンテンツを削除するかどうか。</param>
/// <param name="ResolutionNote">管理者対応メモ。</param>
/// <param name="AdminUserId">対応を行った管理者ユーザーID。</param>
public record ResolveContentReportCommand(
    int ReportId,
    ReportStatus Status,
    bool DeleteTarget,
    string? ResolutionNote,
    string AdminUserId);

/// <summary>
///     通報処置コマンドハンドラー。
///     対象エンティティの削除（Strategy 経由）、ステータス更新、および通報者への通知連携を実行する。
/// </summary>
public class ResolveContentReportHandler(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    IReportTargetHandlerFactory handlerFactory,
    INotificationService notificationService)
    : CommandHandlerBase<ResolveContentReportCommand, Result<bool>>
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));

    private readonly IReportTargetHandlerFactory _handlerFactory =
        handlerFactory ?? throw new ArgumentNullException(nameof(handlerFactory));

    private readonly INotificationService _notificationService =
        notificationService ?? throw new ArgumentNullException(nameof(notificationService));

    /// <inheritdoc />
    protected override async Task<Result<bool>> ExecuteAsync(ResolveContentReportCommand command, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command.AdminUserId);

        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync(cancellationToken);

        ContentReport? report = await context.ContentReports
            .Include(r => r.Item)
            .Include(r => r.Tag)
            .FirstOrDefaultAsync(r => r.Id == command.ReportId, cancellationToken);

        if (report is null)
        {
            return new Failure($"通報 (ID: {command.ReportId}) が見つかりません。");
        }

        if (command.DeleteTarget)
        {
            var targetId = report.TargetType switch
            {
                ReportTargetType.Item => report.ItemId,
                ReportTargetType.Tag => report.TagId,
                _ => null
            };

            if (targetId.HasValue)
            {
                IReportTargetHandler targetHandler = _handlerFactory.GetHandler(report.TargetType);
                await targetHandler.DeleteTargetAsync(context, targetId.Value, cancellationToken);
            }
        }

        switch (command.Status)
        {
            case ReportStatus.Reviewed:
                report.MarkAsReviewed(command.AdminUserId, command.ResolutionNote);
                break;
            case ReportStatus.ActionTaken:
                report.TakeAction(command.AdminUserId, command.ResolutionNote);
                break;
            case ReportStatus.Dismissed:
                report.Dismiss(command.AdminUserId, command.ResolutionNote);
                break;
            case ReportStatus.Pending:
                throw new InvalidOperationException("Pendingへの変更はサポートされていません。");
            default:
                throw new ArgumentOutOfRangeException();
        }

        report.UpdatedDate = DateTime.UtcNow;

        _ = await context.SaveChangesAsync(cancellationToken);

        // 通報者向けの通知状態変更をブロードキャスト
        _notificationService.NotifyNotificationsChanged();

        return new Success<bool>(true);
    }
}