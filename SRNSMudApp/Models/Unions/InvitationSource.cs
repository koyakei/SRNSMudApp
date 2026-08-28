using System.Diagnostics.CodeAnalysis;
namespace SRNSMudApp.Models.Unions;

public record SystemInvitation();
public record AdminInvitation(string InvitedByAdminId);

[SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "Union type handled by C# compiler")]
public readonly union InvitationSource(SystemInvitation, AdminInvitation);