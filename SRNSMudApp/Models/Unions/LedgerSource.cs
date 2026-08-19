namespace SRNSMudApp.Models.Unions;

public record TagRelationSource(int SourceId);
public record ManualSource();

public union LedgerSource(TagRelationSource, ManualSource);
