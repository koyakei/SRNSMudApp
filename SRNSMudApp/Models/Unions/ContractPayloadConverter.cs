using System.Text.Json;
using System.Text.Json.Serialization;

namespace SRNSMudApp.Models.Unions;

/// <summary>
/// ContractPayload union の JSON シリアライズ/デシリアライズ用 Converter。
/// "$type" フィールドで型を区別する。
/// C# 15 union は System.Text.Json がネイティブで型情報を埋め込まないため、
/// 本 Converter でポリモーフィックなシリアライズを実現する。
/// </summary>
public sealed class ContractPayloadConverter : JsonConverter<ContractPayload>
{
    public override ContractPayload Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        if (!root.TryGetProperty("$type", out var typeProp))
            return new EmptyPayload();

        return typeProp.GetString() switch
        {
            nameof(GratisPayload) => root.Deserialize<GratisPayload>(options)!,
            nameof(MutualPayload) => root.Deserialize<MutualPayload>(options)!,
            nameof(PublicOfferPayload) => root.Deserialize<PublicOfferPayload>(options)!,
            nameof(BountyPayload) => root.Deserialize<BountyPayload>(options)!,
            nameof(EmptyPayload) => new EmptyPayload(),
            _ => new EmptyPayload()
        };
    }

    public override void Write(Utf8JsonWriter writer, ContractPayload value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("$type", value switch
        {
            GratisPayload => nameof(GratisPayload),
            MutualPayload => nameof(MutualPayload),
            PublicOfferPayload => nameof(PublicOfferPayload),
            BountyPayload => nameof(BountyPayload),
            EmptyPayload => nameof(EmptyPayload),
            _ => nameof(EmptyPayload)
        });

        // それぞれの型のプロパティを展開して書き込む
        switch (value)
        {
            case GratisPayload g:
                writer.WriteString(nameof(GratisPayload.RequesterMessage), g.RequesterMessage);
                break;
            case MutualPayload m:
                writer.WriteNumber(nameof(MutualPayload.OfferedTargetItemId), m.OfferedTargetItemId);
                writer.WriteNumber(nameof(MutualPayload.OfferedTagId), m.OfferedTagId);
                break;
            case PublicOfferPayload p:
                writer.WriteNumber(nameof(PublicOfferPayload.TargetPublicTradeOfferId), p.TargetPublicTradeOfferId);
                break;
            case BountyPayload b:
                writer.WriteNumber(nameof(BountyPayload.OfferedRewardAssetId), b.OfferedRewardAssetId);
                break;
            default:
                break;
        }

        writer.WriteEndObject();
    }
}