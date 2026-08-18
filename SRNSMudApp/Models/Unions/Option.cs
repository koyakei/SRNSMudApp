using System.Diagnostics.CodeAnalysis;

namespace SRNSMudApp.Models.Unions;

public record Some<T>(T Value);
public record None;

[SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "Union type handled by C# compiler")]
public union Option<T>(Some<T>, None)
{
    // Convenience factory methods
    public static Option<T> Create(T? value) => value is not null ? new Some<T>(value) : new None();
}
