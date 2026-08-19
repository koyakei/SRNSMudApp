using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using SRNSMudApp.Models.Unions;

namespace SRNSMudApp.Data;

public class TimelineEvent : BaseEntity
{
    // Actor is represented by BaseEntity.OwnerId and BaseEntity.Owner

    // Target Information (Serialized JSON of TimelineTarget union)
    public string TimelineTargetJson { get; set; } = string.Empty;

    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new TimelineTargetConverter() }
    };

    [NotMapped]
    public TimelineTarget Target
    {
        get => string.IsNullOrEmpty(TimelineTargetJson) ? new ItemTarget(0) : JsonSerializer.Deserialize<TimelineTarget>(TimelineTargetJson, Options)!;
        set => TimelineTargetJson = JsonSerializer.Serialize(value, Options);
    }

    // 操作対象のタグ（フォローされているかどうかの判定用）
    public int FollowedTagId { get; set; }
    public Tag FollowedTag { get; set; } = null!;

    // イベントの種類 ("Insert", "Update", "Delete")
    [MaxLength(20)] public string EventType { get; set; } = string.Empty;

    // Weight の変化 (Update や Delete 時の表示用)
    public int PreviousWeight { get; set; }
    public int NewWeight { get; set; }
}