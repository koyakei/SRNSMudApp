namespace SRNSMudApp.Models.Unions;

public record GratisPayload(string RequesterMessage);
public record MutualPayload(int OfferedTargetItemId, int OfferedTagId);
public record PublicOfferPayload(int TargetPublicTradeOfferId);
public record BountyPayload(int OfferedRewardAssetId);
public record EmptyPayload();

public union ContractPayload(
    GratisPayload, MutualPayload, PublicOfferPayload, BountyPayload, EmptyPayload);
