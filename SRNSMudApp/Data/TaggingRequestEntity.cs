using System.ComponentModel.DataAnnotations;

namespace SRNSMudApp.Data;

public abstract class TaggingRequestEntity : BaseEntity, ITaggable
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
    public ICollection<Item> Replies { get; set; } = [];

    [MaxLength(1000)] public string? RejectComment { get; set; }

    public DateTimeOffset? RejectedAt { get; set; }

    // ITaggable Implementation
    public virtual ICollection<Tag> Tags { get; set; } = [];
}