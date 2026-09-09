namespace SRNSMudApp.Data;

/// <summary>
///     タグの自動承認を許可するユーザーグループとの紐付け（多対多中間エンティティ）。
/// </summary>
public class TagAutoApproveUserGroup : BaseEntity
{
    public int TagId { get; set; }

    public Tag Tag { get; set; } = null!;

    public int UserGroupId { get; set; }

    public UserGroup UserGroup { get; set; } = null!;
}