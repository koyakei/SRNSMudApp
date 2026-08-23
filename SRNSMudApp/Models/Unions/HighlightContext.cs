using System.Diagnostics.CodeAnalysis;
namespace SRNSMudApp.Models.Unions;

public record NoHighlight();
public record TagHighlight(int TagId);

[SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "Union type handled by C# compiler")]
public union HighlightContext(NoHighlight, TagHighlight);