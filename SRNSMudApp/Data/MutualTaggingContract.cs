namespace SRNSMudApp.Data;

public sealed class MutualTaggingContract : TaggingRequestEntity
{
    public int OfferedTargetItemId { get; set; }
    public Item OfferedTargetItem { get; set; } = null!;

    public int OfferedTagId { get; set; }
    public Tag OfferedTag { get; set; } = null!;
}