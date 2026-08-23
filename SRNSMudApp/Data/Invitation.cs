using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

using SRNSMudApp.Models.Unions;

namespace SRNSMudApp.Data;

public class Invitation : BaseEntity
{
    public string Email { get; set; } = string.Empty;
    public string InvitationCode { get; set; } = string.Empty;
    public DateTime ExpirationDate { get; set; }
    public bool IsUsed { get; set; }
    public string InvitationSourceJson { get; set; } = string.Empty;

    [NotMapped]
    public InvitationSource Source
    {
        get => string.IsNullOrEmpty(InvitationSourceJson) ? new SystemInvitation() : JsonSerializer.Deserialize<InvitationSource>(InvitationSourceJson);
        set => InvitationSourceJson = JsonSerializer.Serialize(value);
    }

    public string InvitedByAdminId { get; set; } = string.Empty;
    public ApplicationUser InvitedByAdmin { get; set; } = null!;
}