

namespace SRNSMudApp.Data;

public class RightAsset : BaseEntity
{
    // アセットの価値や量
    public int Amount { get; set; }

    // 論理削除（Burn）フラグ
    public bool IsBurned { get; set; }

    // 燃焼日時（追跡用）
    public DateTime? BurnedAt { get; set; }

    // 対象となるタグ
    public int? TargetTagId { get; set; }
    public Tag? TargetTag { get; set; }
}