using System.Diagnostics.CodeAnalysis;
namespace SRNSMudApp.Models.Unions;

public record RejectionReason(string Reason);
public record NoRejection();

[SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "Union type handled by C# compiler")]
public union RejectionInfo(RejectionReason, NoRejection);