namespace SRNSMudApp.Data;

public class TagRelationToTag : BaseEntity
{
    // required を外す
    public int TargetTagId { get; set; }
    public Tag TargetTag { get; set; } = null!;

    public int TagId { get; set; }
    public Tag Tag { get; set; } = null!;

    public int Weight { get; set; } = 1; // デフォルト値を入れるとさらに記述が減ります
}