using System.Diagnostics.CodeAnalysis;

using SRNSMudApp.Data;

namespace SRNSMudApp.Models.Unions;

public record GratisContractData(TaggingRequestEntity Entity);

public record MutualContractData(TaggingRequestEntity Entity);

public record TriggerContractData(TaggingRequestEntity Entity);

public record BountyContractData(TaggingRequestEntity Entity);

[SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "Union type handled by C# compiler")]
public readonly union TaggingContract(
    GratisContractData,
    MutualContractData,
    TriggerContractData,
    BountyContractData
);