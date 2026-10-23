using System.Text.Json;

using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;

namespace SRNSMudApp.Tests.Models;

public class ContractUnionConverterTests
{
    private static readonly JsonSerializerOptions PayloadOptions = new()
    {
        Converters = { new ContractPayloadConverter() }
    };

    private static readonly JsonSerializerOptions RejectionOptions = new()
    {
        Converters = { new RejectionInfoConverter() }
    };

    [Fact]
    public void ContractPayloadConverter_GratisPayload_RoundTrip()
    {
        ContractPayload original = new GratisPayload("Please approve my request");
        var json = JsonSerializer.Serialize(original, PayloadOptions);
        Assert.Contains("\"$type\":\"GratisPayload\"", json);
        Assert.Contains("Please approve my request", json);

        var deserialized = JsonSerializer.Deserialize<ContractPayload>(json, PayloadOptions);
        Assert.True(deserialized is GratisPayload);
        var gratis = deserialized switch
        {
            GratisPayload g => g,
            _ => throw new InvalidOperationException()
        };
        Assert.Equal("Please approve my request", gratis.RequesterMessage);
    }

    [Fact]
    public void ContractPayloadConverter_MutualPayload_RoundTrip()
    {
        ContractPayload original = new MutualPayload(123, 456);
        var json = JsonSerializer.Serialize(original, PayloadOptions);
        Assert.Contains("\"$type\":\"MutualPayload\"", json);
        Assert.Contains("123", json);
        Assert.Contains("456", json);

        var deserialized = JsonSerializer.Deserialize<ContractPayload>(json, PayloadOptions);
        Assert.True(deserialized is MutualPayload);
        var mutual = deserialized switch
        {
            MutualPayload m => m,
            _ => throw new InvalidOperationException()
        };
        Assert.Equal(123, mutual.OfferedTargetItemId);
        Assert.Equal(456, mutual.OfferedTagId);
    }

    [Fact]
    public void ContractPayloadConverter_PublicOfferPayload_RoundTrip()
    {
        ContractPayload original = new PublicOfferPayload(789);
        var json = JsonSerializer.Serialize(original, PayloadOptions);
        Assert.Contains("\"$type\":\"PublicOfferPayload\"", json);
        Assert.Contains("789", json);

        var deserialized = JsonSerializer.Deserialize<ContractPayload>(json, PayloadOptions);
        Assert.True(deserialized is PublicOfferPayload);
        var offer = deserialized switch
        {
            PublicOfferPayload p => p,
            _ => throw new InvalidOperationException()
        };
        Assert.Equal(789, offer.TargetPublicTradeOfferId);
    }

    [Fact]
    public void ContractPayloadConverter_BountyPayload_RoundTrip()
    {
        ContractPayload original = new BountyPayload(999);
        var json = JsonSerializer.Serialize(original, PayloadOptions);
        Assert.Contains("\"$type\":\"BountyPayload\"", json);
        Assert.Contains("999", json);

        var deserialized = JsonSerializer.Deserialize<ContractPayload>(json, PayloadOptions);
        Assert.True(deserialized is BountyPayload);
        var bounty = deserialized switch
        {
            BountyPayload b => b,
            _ => throw new InvalidOperationException()
        };
        Assert.Equal(999, bounty.OfferedRewardAssetId);
    }

    [Fact]
    public void ContractPayloadConverter_TagMovePayload_WithParentAndMessage_RoundTrip()
    {
        ContractPayload original = new TagMovePayload(42, "Moving to new parent");
        var json = JsonSerializer.Serialize(original, PayloadOptions);
        Assert.Contains("\"$type\":\"TagMovePayload\"", json);
        Assert.Contains("42", json);
        Assert.Contains("Moving to new parent", json);

        var deserialized = JsonSerializer.Deserialize<ContractPayload>(json, PayloadOptions);
        Assert.True(deserialized is TagMovePayload);
        var move = deserialized switch
        {
            TagMovePayload tm => tm,
            _ => throw new InvalidOperationException()
        };
        Assert.Equal(42, move.NewParentTagId);
        Assert.Equal("Moving to new parent", move.RequesterMessage);
    }

    [Fact]
    public void ContractPayloadConverter_TagMovePayload_WithNullParent_RoundTrip()
    {
        ContractPayload original = new TagMovePayload(null, null);
        var json = JsonSerializer.Serialize(original, PayloadOptions);
        Assert.Contains("\"$type\":\"TagMovePayload\"", json);
        Assert.Contains("\"NewParentTagId\":null", json);

        var deserialized = JsonSerializer.Deserialize<ContractPayload>(json, PayloadOptions);
        Assert.True(deserialized is TagMovePayload);
        var move = deserialized switch
        {
            TagMovePayload tm => tm,
            _ => throw new InvalidOperationException()
        };
        Assert.Null(move.NewParentTagId);
    }

    [Fact]
    public void ContractPayloadConverter_EmptyPayload_RoundTrip()
    {
        ContractPayload original = new EmptyPayload();
        var json = JsonSerializer.Serialize(original, PayloadOptions);
        Assert.Contains("\"$type\":\"EmptyPayload\"", json);

        var deserialized = JsonSerializer.Deserialize<ContractPayload>(json, PayloadOptions);
        Assert.True(deserialized is EmptyPayload);
    }

    [Fact]
    public void ContractPayloadConverter_WhenUnknownTypeOrMissingType_ReturnsEmptyPayload()
    {
        var jsonWithoutType = "{\"SomeOtherProp\":123}";
        var res1 = JsonSerializer.Deserialize<ContractPayload>(jsonWithoutType, PayloadOptions);
        Assert.True(res1 is EmptyPayload);

        var jsonWithUnknownType = "{\"$type\":\"NonExistentType\",\"foo\":\"bar\"}";
        var res2 = JsonSerializer.Deserialize<ContractPayload>(jsonWithUnknownType, PayloadOptions);
        Assert.True(res2 is EmptyPayload);
    }

    [Fact]
    public void RejectionInfoConverter_RejectionReason_RoundTrip()
    {
        RejectionInfo original = new RejectionReason("Rejected due to duplicate content");
        var json = JsonSerializer.Serialize(original, RejectionOptions);
        Assert.Contains("\"$type\":\"RejectionReason\"", json);
        Assert.Contains("Rejected due to duplicate content", json);

        var deserialized = JsonSerializer.Deserialize<RejectionInfo>(json, RejectionOptions);
        Assert.True(deserialized is RejectionReason);
        var reason = deserialized switch
        {
            RejectionReason r => r,
            _ => throw new InvalidOperationException()
        };
        Assert.Equal("Rejected due to duplicate content", reason.Reason);
    }

    [Fact]
    public void RejectionInfoConverter_NoRejection_RoundTrip()
    {
        RejectionInfo original = new NoRejection();
        var json = JsonSerializer.Serialize(original, RejectionOptions);
        Assert.Contains("\"$type\":\"NoRejection\"", json);

        var deserialized = JsonSerializer.Deserialize<RejectionInfo>(json, RejectionOptions);
        Assert.True(deserialized is NoRejection);
    }

    [Fact]
    public void RejectionInfoConverter_LegacyJsonWithoutType_ReturnsRejectionReason()
    {
        var legacyJson = "{\"Reason\":\"Legacy reason without type tag\"}";
        var deserialized = JsonSerializer.Deserialize<RejectionInfo>(legacyJson, RejectionOptions);
        Assert.True(deserialized is RejectionReason);
        var reason = deserialized switch
        {
            RejectionReason r => r,
            _ => throw new InvalidOperationException()
        };
        Assert.Equal("Legacy reason without type tag", reason.Reason);
    }

    [Fact]
    public void RejectionInfoConverter_EmptyObject_ReturnsNoRejection()
    {
        var emptyJson = "{}";
        var deserialized = JsonSerializer.Deserialize<RejectionInfo>(emptyJson, RejectionOptions);
        Assert.True(deserialized is NoRejection);
    }

    [Fact]
    public void TaggingRequestEntity_TransparentSerializationProperties_WorkCorrectly()
    {
        var entity = new TaggingRequestEntity { OwnerId = "test_owner" };

        // 初期状態
        Assert.True(entity.Payload is EmptyPayload);
        Assert.True(entity.Rejection is NoRejection);

        // Payload 設定
        entity.Payload = new GratisPayload("Hello transparent payload");
        Assert.Contains("Hello transparent payload", entity.ContractPayloadJson);
        Assert.Contains("\"$type\":\"GratisPayload\"", entity.ContractPayloadJson);

        // 再読み込み
        Assert.True(entity.Payload is GratisPayload g && g.RequesterMessage == "Hello transparent payload");

        // Rejection 設定
        entity.Rejection = new RejectionReason("Transparent rejection");
        Assert.Contains("Transparent rejection", entity.RejectionInfoJson);
        Assert.Contains("\"$type\":\"RejectionReason\"", entity.RejectionInfoJson);

        // 再読み込み
        Assert.True(entity.Rejection is RejectionReason r && r.Reason == "Transparent rejection");
    }
}