namespace SRNSMudApp.Data;

/// <summary>
///     JPYC 受取トランザクションのステータス。
/// </summary>
public enum JpycDepositStatus
{
    /// <summary>保留中・検証中</summary>
    Pending,

    /// <summary>システムにより検証完了・RightAsset付与済み</summary>
    Confirmed,

    /// <summary>検証失敗（金額不足・宛先不一致等）</summary>
    Failed
}

/// <summary>
///     ユーザー専用受取ウォレットへの JPYC 送金トランザクション記録。
///     システムによる完了確認と二重付与防止を担う。
/// </summary>
public class JpycDepositTransaction : BaseEntity
{
    /// <summary>
    ///     送金先となったユーザー専用受取アドレス。
    /// </summary>
    public string DepositAddress { get; set; } = string.Empty;

    /// <summary>
    ///     オンチェーン上のトランザクションハッシュ。
    /// </summary>
    public string TransactionHash { get; set; } = string.Empty;

    /// <summary>
    ///     ブロックチェーンネットワーク識別子。
    /// </summary>
    public string NetworkName { get; set; } = string.Empty;

    /// <summary>
    ///     送金された JPYC 金額。
    /// </summary>
    public int AmountJpyc { get; set; }

    /// <summary>
    ///     購入対象となったタグID。
    /// </summary>
    public int TargetTagId { get; set; }

    /// <summary>
    ///     付与された RightAsset の数量。
    /// </summary>
    public int RightAssetAmount { get; set; }

    /// <summary>
    ///     発行・付与された RightAsset の ID（未発行時は null）。
    /// </summary>
    public int? RightAssetId { get; set; }

    /// <summary>
    ///     検証状態。
    /// </summary>
    public JpycDepositStatus Status { get; set; } = JpycDepositStatus.Pending;

    /// <summary>
    ///     システムによる検証・完了日時（UTC）。
    /// </summary>
    public DateTime? VerifiedAt { get; set; }
}