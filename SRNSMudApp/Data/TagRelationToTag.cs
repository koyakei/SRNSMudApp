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

public record class Meters(double Value);

public record class Feet(double Value);

public union Length(Meters, Feet)
{
    public double TotalMeters => this switch
    {
        Meters m => m.Value,
        Feet f => f.Value * 0.3048,
        _ => throw new InvalidOperationException("The Length has no value."),
    };

    public Length Add(Length other) => new Meters(TotalMeters + other.TotalMeters);
}