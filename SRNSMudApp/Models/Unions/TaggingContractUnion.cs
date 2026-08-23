using SRNSMudApp.Data;

namespace SRNSMudApp.Models.Unions;

public record GratisContractData(TaggingRequestEntity Entity);

public record MutualContractData(TaggingRequestEntity Entity);

public record TriggerContractData(TaggingRequestEntity Entity);

public record BountyContractData(TaggingRequestEntity Entity);

public union TaggingContract(
    GratisContractData,
    MutualContractData,
    TriggerContractData,
    BountyContractData
);