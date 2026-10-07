using SRNSMudApp.Data;

namespace SRNSMudApp.Services;

/// <summary>
///     タグ間の関連付け（TagRelationToTag）および親子階層関係の管理を担当するインターフェース（ISP 準拠）。
/// </summary>
public interface ITagRelationToTagService
{
    /// <summary>
    ///     TagRelationToTag (タグにタグを関連付け) を追加する。
    /// </summary>
    Task<string?> AddTagToTagAsync(int targetTagId, int tagId, string currentUserId);

    /// <summary>
    ///     TagRelationToTag を削除する。
    /// </summary>
    Task<string?> RemoveTagToTagRelationAsync(int relationId, string currentUserId);

    /// <summary>
    ///     タグの ParentTagId を変更する（子タグとして設定）。
    /// </summary>
    Task<string?> SetParentTagAsync(int parentTagId, int childTagId, string currentUserId,
        IReadOnlyList<Tag> allTagsForCycleCheck);
}