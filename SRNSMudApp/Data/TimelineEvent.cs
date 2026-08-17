using System.ComponentModel.DataAnnotations;

namespace SRNSMudApp.Data;

public class TimelineEvent : BaseEntity
{
    // Actor is represented by BaseEntity.OwnerId and BaseEntity.Owner

    // イベントの対象となったターゲットタイプ ("Item" または "Tag")
    [MaxLength(20)] public string TargetType { get; set; } = string.Empty;

    // 対象のアイテムID (TargetType == "Item" の場合)
    public int? TargetItemId { get; set; }
    public Item? TargetItem { get; set; }

    // 対象のタグID (TargetType == "Tag" の場合)
    public int? TargetTagId { get; set; }
    public Tag? TargetTag { get; set; }

    // 操作対象のタグ（フォローされているかどうかの判定用）
    public int FollowedTagId { get; set; }
    public Tag? FollowedTag { get; set; }

    // イベントの種類 ("Insert", "Update", "Delete")
    [MaxLength(20)] public string EventType { get; set; } = string.Empty;

    // Weight の変化 (Update や Delete 時の表示用)
    public int? PreviousWeight { get; set; }
    public int? NewWeight { get; set; }
}