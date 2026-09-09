using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace SRNSMudApp.Data;

/// <summary>
///     ユーザーグループエンティティ。
///     タグの自動承認委任対象など、複数ユーザーをまとめた単位として使用する。
/// </summary>
public class UserGroup : BaseEntity
{
    [Required]
    [MaxLength(100)]
    public required string Name { get; set; }

    [MaxLength(500)]
    public string Description { get; set; } = string.Empty;

    [SuppressMessage("Usage", "CA2227:Collection properties should be read only")]
    public ICollection<UserGroupMember> Members { get; set; } = [];
}