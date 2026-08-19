using System.Text.Json;
using System.Text.Json.Serialization;

namespace SRNSMudApp.Models.Unions;

public sealed class TimelineTargetConverter : JsonConverter<TimelineTarget>
{
    public override TimelineTarget Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        if (!root.TryGetProperty("$type", out var typeProp))
            return new ItemTarget(0);

        return typeProp.GetString() switch
        {
            nameof(ItemTarget) => root.Deserialize<ItemTarget>(options)!,
            nameof(TagTarget) => root.Deserialize<TagTarget>(options)!,
            _ => new ItemTarget(0)
        };
    }

    public override void Write(Utf8JsonWriter writer, TimelineTarget value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("$type", value switch
        {
            ItemTarget _ => nameof(ItemTarget),
            TagTarget _ => nameof(TagTarget),
            _ => nameof(ItemTarget)
        });

        switch (value)
        {
            case ItemTarget i:
                writer.WriteNumber(nameof(ItemTarget.TargetItemId), i.TargetItemId);
                break;
            case TagTarget t:
                writer.WriteNumber(nameof(TagTarget.TargetTagId), t.TargetTagId);
                break;
        }

        writer.WriteEndObject();
    }
}
