using System.Text.Json;
using System.Text.Json.Serialization;

namespace SRNSMudApp.Models.Unions;

/// <summary>
///     RejectionInfo union の JSON シリアライズ/デシリアライズ用 Converter。
///     C# 15 union においてオブジェクト型同士のデシリアライズ曖昧性を解消するため、$type またはプロパティ存在により判別する。
/// </summary>
public sealed class RejectionInfoConverter : JsonConverter<RejectionInfo>
{
    public override RejectionInfo Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        JsonElement root = doc.RootElement;

        if (root.TryGetProperty("$type", out JsonElement typeProp))
        {
            return typeProp.GetString() switch
            {
                nameof(RejectionReason) => root.Deserialize<RejectionReason>(options)!,
                _ => new NoRejection()
            };
        }

        // 後方互換性: $type がない場合でも Reason プロパティがあれば RejectionReason として扱う
        return root.TryGetProperty("Reason", out JsonElement reasonProp)
            ? new RejectionReason(reasonProp.GetString() ?? "")
            : new NoRejection();
    }

    public override void Write(Utf8JsonWriter writer, RejectionInfo value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteStartObject();
        switch (value)
        {
            case RejectionReason r:
                writer.WriteString("$type", nameof(RejectionReason));
                writer.WriteString(nameof(RejectionReason.Reason), r.Reason);
                break;
            default:
                writer.WriteString("$type", nameof(NoRejection));
                break;
        }
        writer.WriteEndObject();
    }
}