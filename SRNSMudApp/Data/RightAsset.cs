using System.Text.Json;
using System.ComponentModel.DataAnnotations.Schema;
using SRNSMudApp.Models.Unions;

namespace SRNSMudApp.Data;

public class RightAsset : BaseEntity
{
    // アセットの価値や量
    public int Amount { get; set; }

    // 論理削除（Burn）フラグ
    public bool IsBurned { get; set; }

    // 燃焼日時や状態 (Serialized JSON of BurnStatus union)
    public string BurnStatusJson { get; set; } = string.Empty;

    [NotMapped]
    public BurnStatus Status
    {
        get => string.IsNullOrEmpty(BurnStatusJson) ? new NotBurned() : JsonSerializer.Deserialize<BurnStatus>(BurnStatusJson)!;
        set => BurnStatusJson = JsonSerializer.Serialize(value);
    }

    // 対象となるタグ
    public int TargetTagId { get; set; }
    public Tag TargetTag { get; set; } = null!;
}