using System.Diagnostics.CodeAnalysis;

namespace SRNSMudApp.Models.Unions;

public record Success<T>(T Value);
public record Failure(Exception Error, string Message = "");

[SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "Union type handled by C# compiler")]
public union Result<T>(Success<T>, Failure);
