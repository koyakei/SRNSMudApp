namespace SRNSMudApp.Services;

/// <summary>
///     タグ付けリクエストの却下操作を担当するインターフェース（ISP 準拠）。
/// </summary>
public interface ITagRequestRejectionService
{
    /// <summary>
    ///     タグ付けリクエストを却下する。
    /// </summary>
    Task RejectRequestAsync(int requestId, string rejectUserId, string? comment);
}