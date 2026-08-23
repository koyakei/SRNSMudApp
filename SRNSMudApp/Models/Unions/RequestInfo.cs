using System.Diagnostics.CodeAnalysis;

using SRNSMudApp.Data;

namespace SRNSMudApp.Models.Unions;

public record NoRequest();
public record TaggingRequest(
    TaggingRequestType RequestType, int ProposedWeight,
    int TargetItemId, string TargetItemContent,
    int TargetTagId, string TargetTagName, TradeStatus Status);

[SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "Union type handled by C# compiler")]
public union RequestInfo(NoRequest, TaggingRequest);