namespace SRNSMudApp.Models.Unions;

public record SystemInvitation();
public record AdminInvitation(string InvitedByAdminId);

public union InvitationSource(SystemInvitation, AdminInvitation);