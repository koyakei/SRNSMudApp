using System.Diagnostics.CodeAnalysis;

using MudBlazor;

using SRNSMudApp.Data;
using SRNSMudApp.Services;

namespace SRNSMudApp.Components.Admin;

/// <summary>
///     ReportManager コンポーネントの状態管理、データ取得、および表示・判定ロジックを担当する ViewModel。
///     通報ステータスや通報対象種別の表示文字列・カラーマッピングをカプセル化し、単体テストを可能にする。
///     プライマリコンストラクタにより依存サービスを簡潔に注入する。
/// </summary>
public class ReportManagerViewModel(IContentReportService contentReportService)
{
    public IReadOnlyList<ContentReport> Reports { get; private set; } = [];
    public ReportStatus? SelectedStatus { get; set; } = ReportStatus.Pending;
    public ReportTargetType? SelectedTargetType { get; set; }
    public bool IsLoading { get; private set; } = true;

    /// <summary>
    ///     選択されたステータスと対象種別に基づいて通報一覧を非同期取得する。
    /// </summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "例外発生時にも画面崩壊を防ぎ、UI にエラーメッセージを返却するため捕捉する")]
    public async Task<(bool Success, string? ErrorMessage)> LoadReportsAsync()
    {
        IsLoading = true;
        try
        {
            var reports = await contentReportService.GetReportsAsync(SelectedStatus, SelectedTargetType);
            Reports = reports ?? [];
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, $"通報一覧の取得中にエラーが発生しました: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    ///     指定した通報の最新情報を取得する。
    /// </summary>
    public async Task<ContentReport?> GetReportByIdAsync(int reportId)
    {
        return await contentReportService.GetReportByIdAsync(reportId);
    }

    /// <summary>
    ///     ReportStatus に対応する日本語表示文字列を取得する。
    /// </summary>
    public static string GetStatusText(ReportStatus status) => status switch
    {
        ReportStatus.Pending => "未対応",
        ReportStatus.Reviewed => "確認済み",
        ReportStatus.ActionTaken => "処置済み",
        ReportStatus.Dismissed => "却下",
        _ => "不明"
    };

    /// <summary>
    ///     ReportStatus に対応する MudBlazor Color を取得する。
    /// </summary>
    public static Color GetStatusColor(ReportStatus status) => status switch
    {
        ReportStatus.Pending => Color.Warning,
        ReportStatus.Reviewed => Color.Info,
        ReportStatus.ActionTaken => Color.Success,
        ReportStatus.Dismissed => Color.Default,
        _ => Color.Default
    };

    /// <summary>
    ///     ReportTargetType に対応する日本語表示文字列を取得する。
    /// </summary>
    public static string GetTargetTypeText(ReportTargetType targetType) => targetType switch
    {
        ReportTargetType.Item => "アイテム",
        ReportTargetType.Tag => "タグ",
        _ => "その他"
    };

    /// <summary>
    ///     ReportTargetType に対応する MudBlazor Color を取得する。
    /// </summary>
    public static Color GetTargetTypeColor(ReportTargetType targetType) => targetType switch
    {
        ReportTargetType.Item => Color.Primary,
        ReportTargetType.Tag => Color.Secondary,
        _ => Color.Default
    };
}