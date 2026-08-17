namespace SRNSMudApp.Data;

public sealed class PublicOfferTriggerContract : TaggingRequestEntity
{
    public int TargetPublicTradeOfferId { get; set; }
    public PublicTradeOffer TargetPublicTradeOffer { get; set; } = null!;
}