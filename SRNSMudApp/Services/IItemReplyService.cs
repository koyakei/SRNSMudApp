using SRNSMudApp.Data;

namespace SRNSMudApp.Services;

/// <summary>
///     アイテムへのリプライやメッセージング関連の操作を担当するサービス契約。
/// </summary>
public interface IItemReplyService
{
    Task<Item?> AddReplyToRequestAsync(int requestId, string userId, string message);
    Task<IReadOnlyList<Item>> GetItemRepliesAsync(int parentItemId, string? currentUserId = null, bool isAdmin = false);
    /// <summary>
    ///     アイテムに対するリプライの件数を取得する。
    /// </summary>
    Task<int> GetItemReplyCountAsync(int parentItemId);
    /// <summary>
    ///     アイテムへのリプライを追加する。
    /// </summary>
    /// <param name="parentItemId">返信先アイテムID</param>
    /// <param name="content">返信内容</param>
    /// <param name="userId">返信者ユーザーID</param>
    /// <param name="targetUserIds">明示的なメンション対象ユーザーID一覧</param>
    /// <param name="isPrivate">プライベートモード（非公開）として保存するかどうか</param>
    /// <param name="targetUserGroupId">プライベート時の公開対象ユーザーグループID（nullの場合はフォロワー限定）</param>
    /// <returns>作成されたリプライアイテム</returns>
    Task<Item?> AddItemReplyAsync(
        int parentItemId,
        string content,
        string userId,
        IEnumerable<string>? targetUserIds = null,
        bool isPrivate = false,
        int? targetUserGroupId = null);

    Task<bool> ToggleConversationOptOutAsync(int rootItemId, string userId);
    Task<bool> IsUserOptedOutAsync(int rootItemId, string userId);
    Task<HashSet<string>> GetOptedOutUsersAsync(int rootItemId);
}