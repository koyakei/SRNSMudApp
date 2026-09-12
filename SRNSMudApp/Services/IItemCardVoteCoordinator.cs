using SRNSMudApp.Data;

namespace SRNSMudApp.Services;

/// <summary>
///     ItemCard における投票（Good）および三相リアクション（真実・善・美）の操作を調整するコーディネーター。
/// </summary>
public interface IItemCardVoteCoordinator
{
    /// <summary>
    ///     アイテムへの Good 投票（アップボート / ダウンボート）を切り替える。
    /// </summary>
    Task<bool> ToggleVoteAsync(
        int itemId,
        string currentUserId,
        int? goodTagId,
        bool isUpvote,
        Func<Task>? ensureSystemTagsAsync = null);

    /// <summary>
    ///     アイテムへの三相リアクション投票を切り替える。
    /// </summary>
    Task<bool> ToggleReactionAsync(
        int itemId,
        string currentUserId,
        string reactionTagName,
        int targetWeight,
        int? reactionTagId,
        IReadOnlyList<Tag> allTags,
        Func<Task>? ensureSystemTagsAsync = null);
}