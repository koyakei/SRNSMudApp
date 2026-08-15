namespace SRNSMudApp.Data;

public class Invitation : BaseEntity
{
    public string Email { get; set; } = string.Empty;
    public string InvitationCode { get; set; } = string.Empty;
    public DateTime ExpirationDate { get; set; }
    public bool IsUsed { get; set; }
    public string? InvitedByAdminId { get; set; }
    public ApplicationUser? InvitedByAdmin { get; set; }
}