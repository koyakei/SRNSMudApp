namespace SRNSMudApp.Models.Push;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// VAPID（Voluntary Application Server Identification）設定クラス。
/// </summary>
public sealed class VapidOptions
{
    /// <summary>
    /// 設定セクション名
    /// </summary>
    public const string SectionName = "Vapid";

    /// <summary>
    /// 管理者への連絡先 URI（例: "mailto:admin@example.com"）。
    /// </summary>
    [Required]
    public string Subject { get; set; } = string.Empty;

    /// <summary>
    /// フロントエンドの PushManager に渡す VAPID 公開鍵。
    /// </summary>
    [Required]
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>
    /// 送信署名に使用する VAPID 秘密鍵。
    /// </summary>
    [Required]
    public string PrivateKey { get; set; } = string.Empty;
}