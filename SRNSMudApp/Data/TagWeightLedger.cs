using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

using SRNSMudApp.Models.Unions;

namespace SRNSMudApp.Data;

public class TagWeightLedger : BaseEntity
{
    public int TagId { get; set; }
    public Tag Tag { get; set; } = null!;

    // どのような操作によって発生したか（"TagRelation" など）
    public string SourceType { get; set; } = string.Empty;

    // ソースとなるエンティティのID (LedgerSource Union is used to handle 0/null)
    public int? SourceId { get; set; }
    public TagRelation? TagRelation { get; set; }

    // 対象アイテムのID (TagRelationが削除された後も履歴を保持するため)
    public int? ItemId { get; set; }

    // 対象親タグのID (TagRelationToTagが削除された後も履歴を保持するため)
    public int? TargetTagId { get; set; }

    [NotMapped]
    public LedgerSource Source
    {
        get => (SourceId ?? 0) == 0 ? new ManualSource() : new TagRelationSource(SourceId!.Value);
        set => SourceId = value switch { TagRelationSource(var id) => id, _ => null };
    }

    // 証明: 対価として消費された（Burnされた）アセットのID (null if owner action with no contract)
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