namespace SRNSMudApp.Models.Push;

/// <summary>
/// Firebase Cloud Messaging (FCM) を利用したプッシュ通知の接続設定オプション。
/// FirebaseApp の初期化に使用するサービスアカウント JSON ファイルのパスを保持します。
/// 未設定の場合はローカル VAPID/WebPush にフォールバックします。
/// </summary>
public sealed class FirebaseOptions
{
    /// <summary>
    /// 設定セクション名
    /// </summary>
    public const string SectionName = "Firebase";

    /// <summary>
    /// Firebase サービスアカウント JSON ファイルの絶対パス、または JSON 文字列そのもの。
    /// 環境変数 <c>Firebase__ServiceAccountJson</c> でも設定可能。
    /// （例: /etc/secrets/firebase-service-account.json）
    /// </summary>
    public string? ServiceAccountJson { get; set; }

    /// <summary>
    /// Firebase プロジェクト ID。
    /// 省略可能。指定するとサービスアカウント JSON からの自動検出を上書きします。
    /// </summary>
    public string? ProjectId { get; set; }

    /// <summary>
    /// Firebase の設定が有効（ServiceAccountJson が入力されている）かどうか。
    /// </summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ServiceAccountJson);
}