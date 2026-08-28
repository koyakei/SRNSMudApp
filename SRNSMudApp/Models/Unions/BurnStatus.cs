using System.Diagnostics.CodeAnalysis;
namespace SRNSMudApp.Models.Unions;

public record Burned(DateTime BurnedAt);
public record NotBurned();

[SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "Union type handled by C# compiler")]
public readonly union BurnStatus(Burned, NotBurned);