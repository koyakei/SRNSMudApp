using SRNSMudApp.Data;

namespace SRNSMudApp.Models.Unions;

public record NoRequest();
public record TaggingRequest(
    TaggingRequestType RequestType, int ProposedWeight,
    int TargetItemId, string TargetItemContent,
    int TargetTagId, string TargetTagName, TradeStatus Status);

public union RequestInfo(NoRequest, TaggingRequest);
