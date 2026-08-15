namespace SRNSMudApp.Data;

public class TagRelation : BaseEntity
{
    // required を外す
    public int ItemId { get; set; }
    public Item Item { get; set; } = null!;

    public int TagId { get; set; }
    public Tag Tag { get; set; } = null!;

    public int Weight { get; set; } = 1; // デフォルト値を入れるとさらに記述が減ります
}