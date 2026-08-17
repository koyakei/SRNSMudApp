using System.ComponentModel.DataAnnotations;

namespace SRNSMudApp.Data;

public class TagWeightLedger : BaseEntity
{
    public int TagId { get; set; }
    public Tag Tag { get; set; } = null!;

    // どのような操作によって発生したか（"TagRelation" など）
    public string SourceType { get; set; } = string.Empty;

    // ソースとなるエンティティのID
    public int? SourceId { get; set; }
    public TagRelation? TagRelation { get; set; }

    // 証明: 対価として消費された（Burnされた）アセットのID (任意)
    public int? ConsumedRightAssetId { get; set; }
    public RightAsset? ConsumedRightAsset { get; set; }

    // --- 監査ログ統合フィールド ---
    // 実行者がタグのオーナーだったか
    public bool IsOwnerAction { get; set; }

    // タグ名のスナップショット（履歴表示時のJOIN回避のため）
    [MaxLength(100)] public string TagNameSnapshot { get; set; } = string.Empty;

    // 変更理由やコンテキスト
    [MaxLength(500)] public string Reason { get; set; } = string.Empty;

    // --- 数値の変化の記録 ---
    public int PreviousWeight { get; set; }
    public int NewWeight { get; set; }
    public int Delta { get; set; }
}