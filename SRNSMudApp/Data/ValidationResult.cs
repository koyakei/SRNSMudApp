using System.Diagnostics.CodeAnalysis;

namespace SRNSMudApp.Data;

public record Valid;
public record Invalid(string ErrorMessage);

[SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "Union type handled by C# compiler")]
public union ValidationResult(Valid, Invalid);
