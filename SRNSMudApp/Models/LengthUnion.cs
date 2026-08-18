using System.Diagnostics.CodeAnalysis;

namespace SRNSMudApp.Models;

public record class Meters(double Value);
public record class Feet(double Value);

[SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "Union type handled by C# compiler")]
public union Length(Meters, Feet)
{
    public readonly double TotalMeters => this switch
    {
        Meters m => m.Value,
        Feet f => f.Value * 0.3048,
        _ => throw new InvalidOperationException("The Length has no value."),
    };

    public readonly Length Add(Length other) => new Meters(TotalMeters + other.TotalMeters);
}
