#region

using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;

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
///     IDbContextFactory を使用して独立したコンテキストで並行安全に動作する。
/// </summary>
public class ContentReportService(IDbContextFactory<ApplicationDbContext> dbFactory) : IContentReportService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));

    /// <inheritdoc />
    public async Task<ContentReport> CreateReportAsync(CreateContentReportDto dto, string reporterUserId)
    {
        ArgumentNullException.ThrowIfNull(dto);
        ArgumentException.ThrowIfNullOrWhiteSpace(reporterUserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(dto.Reason);

        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();

        var snapshot = string.Empty;
        if (dto.TargetType == ReportTargetType.Item)
        {
            if (!dto.ItemId.HasValue)
            {
                throw new ArgumentException("Item を対象とする通報には ItemId が必須です。", nameof(dto));
            }

            Item? item = await context.Items
                .Include(i => i.Owner)
                .FirstOrDefaultAsync(i => i.Id == dto.ItemId.Value);

            if (item is null)
            {
                throw new InvalidOperationException($"通報対象のアイテム (ID: {dto.ItemId.Value}) が見つかりません。");
            }

            snapshot = $"[投稿者: {item.Owner?.UserName ?? "不明"}] {item.Content}";
        }
        else if (dto.TargetType == ReportTargetType.Tag)
        {
            if (!dto.TagId.HasValue)
            {
                throw new ArgumentException("Tag を対象とする通報には TagId が必須です。", nameof(dto));
            }

            Tag? tag = await context.Tags
                .Include(t => t.Owner)
                .FirstOrDefaultAsync(t => t.Id == dto.TagId.Value);

            if (tag is null)
            {
                throw new InvalidOperationException($"通報対象のタグ (ID: {dto.TagId.Value}) が見つかりません。");
            }

            snapshot = $"[タグ名: {tag.Name} / 作成者: {tag.Owner?.UserName ?? "不明"}] {tag.Content}";
        }

        // スナップショットの最大長制限 (2000文字)
        if (snapshot.Length > 2000)
        {
            snapshot = snapshot[..1997] + "...";
        }

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
        ArgumentException.ThrowIfNullOrWhiteSpace(adminUserId);

        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();

        ContentReport? report = await context.ContentReports.FindAsync(reportId);
        if (report is null)
        {
            return false;
        }

        report.Status = status;
        report.ResolutionNote = resolutionNote;
        report.HandledByAdminId = adminUserId;
        report.HandledDate = DateTime.UtcNow;
        report.UpdatedDate = DateTime.UtcNow;

        _ = await context.SaveChangesAsync();
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> ResolveReportWithActionAsync(int reportId, bool deleteTarget, string? resolutionNote, string adminUserId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adminUserId);

        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();

        ContentReport? report = await context.ContentReports
            .Include(r => r.Item)
            .Include(r => r.Tag)
            .FirstOrDefaultAsync(r => r.Id == reportId);

        if (report is null)
        {
            return false;
        }

        if (deleteTarget)
        {
            if (report.TargetType == ReportTargetType.Item && report.ItemId.HasValue)
            {
                Item? itemToDelete = await context.Items.FindAsync(report.ItemId.Value);
                if (itemToDelete is not null)
                {
                    _ = context.Items.Remove(itemToDelete);
                }
            }
            else if (report.TargetType == ReportTargetType.Tag && report.TagId.HasValue)
            {
                Tag? tagToDelete = await context.Tags.FindAsync(report.TagId.Value);
                if (tagToDelete is not null)
                {
                    _ = context.Tags.Remove(tagToDelete);
                }
            }
        }

        report.Status = ReportStatus.ActionTaken;
        report.ResolutionNote = resolutionNote;
        report.HandledByAdminId = adminUserId;
        report.HandledDate = DateTime.UtcNow;
        report.UpdatedDate = DateTime.UtcNow;

        _ = await context.SaveChangesAsync();
        return true;
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