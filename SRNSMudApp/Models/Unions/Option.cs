using System.Diagnostics.CodeAnalysis;

namespace SRNSMudApp.Models.Unions;

public record Some<T>(T Value);
public record None;

[SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "Union type handled by C# compiler")]
[SuppressMessage("Naming", "CA1716:Identifiers should not match keywords", Justification = "Option はドメイン横断の union 基盤型であり、リネームは全コードへ波及するため現行名を維持する")]
[SuppressMessage("Design", "CA1000:Do not declare static members on generic types", Justification = "Union 型の利便性ファクトリとして Create をジェネリック型に提供する")]
public union Option<T>(Some<T>, None)
{
    // Convenience factory methods
    public static Option<T> Create(T? value) => value is not null ? new Some<T>(value) : new None();
}