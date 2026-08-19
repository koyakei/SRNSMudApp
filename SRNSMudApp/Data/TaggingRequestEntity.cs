using System.ComponentModel.DataAnnotations;

namespace SRNSMudApp.Data;

public class TaggingRequestEntity : BaseEntity, ITaggable
{
    [MaxLength(50)] public string RequesterUserId { get; set; } = string.Empty;

    [MaxLength(50)] public string TagOwnerUserId { get; set; } = string.Empty;

    public int TargetItemId { get; set; }
    public Item TargetItem { get; set; } = null!;

    public int RequestedTagId { get; set; }
    public Tag RequestedTag { get; set; } = null!;

    public TradeStatus Status { get; set; } = TradeStatus.Proposed;
    public int ProposedWeight { get; set; } = 1;

    // The asset that will be burned when this contract is accepted.
    public int? ConsumedRightAssetId { get; set; }
    public RightAsset? ConsumedRightAsset { get; set; }

    public int? RequestItemId { get; set; }
    public Item? RequestItem { get; set; }

    public TaggingRequestType RequestType { get; set; } = TaggingRequestType.Add;
    public ICollection<Item> Replies { get; init; } = [];

    [MaxLength(1000)] public string? RejectComment { get; set; }

    public DateTimeOffset? RejectedAt { get; set; }

    // --- Merged Subclass Properties ---

    // Type discriminator equivalent
    [MaxLength(50)] public string ContractType { get; set; } = string.Empty;

    // From GratisTaggingContract
    [MaxLength(200)] public string? RequesterMessage { get; set; }

    // From MutualTaggingContract
    public int? OfferedTargetItemId { get; set; }
    public Item? OfferedTargetItem { get; set; }

    public int? OfferedTagId { get; set; }
    public Tag? OfferedTag { get; set; }

    // From PublicOfferTriggerContract
    public int? TargetPublicTradeOfferId { get; set; }
    public PublicTradeOffer? TargetPublicTradeOffer { get; set; }

    // From BountyTaggingContract
    public int? OfferedRewardAssetId { get; set; }
    public RightAsset? OfferedRewardAsset { get; set; }

    // ITaggable Implementation
    public virtual ICollection<Tag> Tags { get; init; } = [];
}