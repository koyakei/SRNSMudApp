using System.Diagnostics.CodeAnalysis;
namespace SRNSMudApp.Models.Unions;

public record NoFocus();
public record FocusedItem(int ItemId);

[SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "Union type handled by C# compiler")]
public readonly union FocusState(NoFocus, FocusedItem);