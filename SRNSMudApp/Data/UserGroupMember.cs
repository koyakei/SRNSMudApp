using System.ComponentModel.DataAnnotations;

namespace SRNSMudApp.Data;

/// <summary>
///     ユーザーグループに所属するメンバーエンティティ。
/// </summary>
public class UserGroupMember : BaseEntity
{
    public int UserGroupId { get; set; }

    public UserGroup UserGroup { get; set; } = null!;

    [Required]
    public required string UserId { get; set; }

    public ApplicationUser User { get; set; } = null!;
}