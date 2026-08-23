using System.Diagnostics.CodeAnalysis;
namespace SRNSMudApp.Models.Unions;

public record ItemTarget(int TargetItemId);
public record TagTarget(int TargetTagId);

[SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "Union type handled by C# compiler")]
public union TimelineTarget(ItemTarget, TagTarget);