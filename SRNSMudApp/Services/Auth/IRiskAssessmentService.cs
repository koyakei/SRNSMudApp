namespace SRNSMudApp.Services.Auth;

/// <summary>
///     認証リクエストのリスク（IP、デバイス、振る舞い等）を評価するサービスのインターフェース。
///     SOLID の依存性逆転の原則 (DIP) に従い、具象クラスへの直接依存を排除する。
/// </summary>
public interface IRiskAssessmentService
{
    /// <summary>
    ///     現在の認証リクエストのリスクを評価する。
    /// </summary>
    /// <param name="ipAddress">クライアントの IP アドレス。</param>
    /// <param name="deviceId">クライアントのデバイス識別子。</param>
    /// <param name="userEmail">ユーザーのメールアドレス。</param>
    /// <param name="cancellationToken">キャンセレーショントークン。</param>
    /// <returns>リクエストが高リスクと判定された場合は true、安全な場合は false。</returns>
    Task<bool> IsRequestRiskyAsync(string? ipAddress, string? deviceId, string? userEmail, CancellationToken cancellationToken = default);
}