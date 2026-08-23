using System.Diagnostics.CodeAnalysis;

namespace SRNSMudApp.Components.UI;

public record Loading;
public record Empty(string Message);
public record Loaded<T>(T Data);
public record Failed(Exception Error);

[SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "Union type handled by C# compiler")]
public union AsyncPageState<T>(Loading, Empty, Loaded<T>, Failed);