using SRNSMudApp.Data;

namespace SRNSMudApp.Services;

/// <summary>
///     ItemCard コンポーネントから発生するタグ関連操作のサービス契約。
///     このインターフェイスを通じてモックを差し込めるため、
///     bUnit によるコンポーネントテストで DB 依存を排除できる。
/// </summary>
public interface IItemTagService
{
    /// <summary>
    ///     アイテムに指定タグの TagRelation を追加する。
    /// </summary>
    /// <returns>成功した場合は null、失敗した場合はエラーメッセージ。</returns>
    Task<string?> AddTagToItemAsync(int itemId, int tagId, string currentUserId);

    /// <summary>
    ///     TagRelation を削除する。
    /// </summary>
    /// <returns>成功した場合は null、失敗した場合はエラーメッセージ。</returns>
    Task<string?> RemoveTagRelationAsync(int relationId, string currentUserId);

    /// <summary>
    ///     TagRelation の Weight を delta 分だけ更新する。
    ///     操作権限がない場合は null を返す（呼び出し元でコントラクト提案ダイアログを開くことを示す）。
    /// </summary>
    /// <returns>
    ///     null: 権限なし（コントラクト提案が必要）
    ///     空文字列: 成功
    ///     エラーメッセージ: 失敗
    /// </returns>
    Task<UpdateWeightResult> UpdateTagWeightAsync(int relationId, int delta, string currentUserId);

    /// <summary>
    ///     TagRelation の Weight を指定値に変更する。
    /// </summary>
    Task<string?> SetTagWeightAsync(int relationId, int newWeight, string currentUserId);

    /// <summary>
    ///     TagRelation の TagId を別タグに変更する。
    /// </summary>
    Task<string?> ChangeItemTagAsync(int relationId, int newTagId, int itemId, string currentUserId);

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

    Task<IReadOnlyList<TaggingRequestEntity>> GetTaggingRequestsForItemAsync(int itemId);
    Task<Item?> AddReplyToRequestAsync(int requestId, string userId, string message);
    Task<IReadOnlyList<Item>> GetItemRepliesAsync(int parentItemId);
    Task<Item?> AddItemReplyAsync(int parentItemId, string content, string userId);
}

/// <summary>Weight 更新の結果を表す。</summary>
public enum UpdateWeightResult
{
    /// <summary>成功。</summary>
    Success,

    /// <summary>権限なし（コントラクト提案が必要）。</summary>
    NoPermission,

    /// <summary>対象エンティティが見つからない。</summary>
    NotFound
}