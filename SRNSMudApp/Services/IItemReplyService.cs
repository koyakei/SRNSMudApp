using SRNSMudApp.Data;

namespace SRNSMudApp.Services;

/// <summary>
///     アイテムへのリプライやメッセージング関連の操作を担当するサービス契約。
/// </summary>
public interface IItemReplyService
{
    Task<Item?> AddReplyToRequestAsync(int requestId, string userId, string message);
    Task<IReadOnlyList<Item>> GetItemRepliesAsync(int parentItemId);
    /// <summary>
    ///     アイテムに対するリプライの件数を取得する。
    /// </summary>
    Task<int> GetItemReplyCountAsync(int parentItemId);
    Task<Item?> AddItemReplyAsync(int parentItemId, string content, string userId, IEnumerable<string>? targetUserIds = null);
}

