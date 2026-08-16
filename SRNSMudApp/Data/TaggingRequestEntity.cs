using System.ComponentModel.DataAnnotations;

namespace SRNSMudApp.Data;

public abstract class TaggingRequestEntity : BaseEntity
{
    [MaxLength(50)]
    public string RequesterUserId { get; set; } = string.Empty;

    [MaxLength(50)]
    public string TagOwnerUserId { get; set; } = string.Empty;

    public int TargetItemId { get; set; }
    public Item TargetItem { get; set; } = null!;

    public int RequestedTagId { get; set; }
    public Tag RequestedTag { get; set; } = null!;

    public TradeStatus Status { get; set; } = TradeStatus.Proposed;

    // The asset that will be burned when this contract is accepted.
    public int? ConsumedRightAssetId { get; set; }
    public RightAsset? ConsumedRightAsset { get; set; }

    public TaggingRequestType RequestType { get; set; } = TaggingRequestType.Add;
    public ICollection<TaggingRequestReply> Replies { get; set; } = new List<TaggingRequestReply>();
}