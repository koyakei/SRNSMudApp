using System.Diagnostics.CodeAnalysis;
namespace SRNSMudApp.Models.Unions;

public record GratisPayload(string RequesterMessage);
public record MutualPayload(int OfferedTargetItemId, int OfferedTagId);
public record PublicOfferPayload(int TargetPublicTradeOfferId);
public record BountyPayload(int OfferedRewardAssetId);
public record EmptyPayload();

[SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "Union type handled by C# compiler")]
public readonly union ContractPayload(
    GratisPayload, MutualPayload, PublicOfferPayload, BountyPayload, EmptyPayload);