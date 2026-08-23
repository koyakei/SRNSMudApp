using System.Diagnostics.CodeAnalysis;
namespace SRNSMudApp.Models.Unions;

public record TagRelationSource(int SourceId);
public record ManualSource();

[SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "Union type handled by C# compiler")]
public union LedgerSource(TagRelationSource, ManualSource);