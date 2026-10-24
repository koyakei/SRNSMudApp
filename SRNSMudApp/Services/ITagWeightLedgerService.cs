using SRNSMudApp.Data;

namespace SRNSMudApp.Services;

/// <summary>
///     タグの CachedWeight 更新およびウェイト台帳（TagWeightLedger）の記録を担当するサービスインターフェース。
///     ItemTagService から台帳記録の責務を分離し、単一責任の原則（SRP）を担保する。
/// </summary>
public interface ITagWeightLedgerService
{
    /// <summary>
    ///     アイテムへのタグ付けに伴うウェイト変更をキャッシュに反映し、台帳レコードをコンテキストに追加する。
    /// </summary>
    void RecordItemTagWeightChange(
        ApplicationDbContext context,
        Tag tag,
        int itemId,
        string sourceType,
        int? sourceId,
        int delta,
        string reason,
        string userId,
        TagRelation? sourceRelation = null);

    /// <summary>
    ///     タグ間の関連付け（TagRelationToTag）に伴うウェイト変更をキャッシュに反映し、台帳レコードをコンテキストに追加する。
    /// </summary>
    void RecordTagToTagWeightChange(
        ApplicationDbContext context,
        Tag tag,
        int targetTagId,
        string sourceType,
        int? sourceId,
        int delta,
        string reason,
        string userId);

    /// <summary>
    ///     指定されたタグ重み台帳レコードに対してコメントを付与する。
    ///     台帳レコードに CommentItem が存在しない場合は新規作成し、存在する場合はリプライとして追加する。
    /// </summary>
    /// <param name="ledgerId">対象の台帳レコードID。</param>
    /// <param name="content">コメント内容。</param>
    /// <param name="userId">投稿者ユーザーID。</param>
    /// <returns>作成されたコメントアイテム。</returns>
    Task<Item> AddCommentAsync(int ledgerId, string content, string userId);

    /// <summary>
    ///     指定されたタグ重み台帳レコードに対してタグを付与する。
    ///     台帳レコードに CommentItem が存在しない場合は基準アイテムを自動生成した上でタグを紐付ける。
    /// </summary>
    /// <param name="ledgerId">対象の台帳レコードID。</param>
    /// <param name="tagId">付与するタグID。</param>
    /// <param name="userId">操作ユーザーID。</param>
    /// <returns>作成されたタグ関連付けエンティティ。</returns>
    Task<TagRelation> AddTagAsync(int ledgerId, int tagId, string userId);

    /// <summary>
    ///     指定されたタグ重み台帳レコードに紐づく CommentItem を取得する。存在しない場合は新規作成して紐付ける。
    /// </summary>
    /// <param name="ledgerId">対象の台帳レコードID。</param>
    /// <param name="userId">操作ユーザーID。</param>
    /// <returns>台帳レコードに紐づくアイテム。</returns>
    Task<Item> EnsureCommentItemAsync(int ledgerId, string userId);

    /// <summary>
    ///     指定されたタグ重み台帳レコードが DB に永続化されていることを保証する。
    ///     Id が 0（仮想補完されたレコード等）の場合は DB に INSERT して採番されたエンティティを返す。
    /// </summary>
    /// <param name="ledger">台帳レコード。</param>
    /// <returns>永続化された台帳レコード。</returns>
    Task<TagWeightLedger> EnsureLedgerRecordAsync(TagWeightLedger ledger);

    /// <summary>
    ///     指定されたタグ重み台帳レコードに対してコメントを付与する。
    ///     Id が 0 の場合は自動的に台帳レコードを DB に永続化した上でコメントを付与する。
    /// </summary>
    Task<Item> AddCommentAsync(TagWeightLedger ledger, string content, string userId);

    /// <summary>
    ///     指定されたタグ重み台帳レコードに対してタグを付与する。
    ///     Id が 0 の場合は自動的に台帳レコードを DB に永続化した上でタグを付与する。
    /// </summary>
    Task<TagRelation> AddTagAsync(TagWeightLedger ledger, int tagId, string userId);

    /// <summary>
    ///     指定されたタグ重み台帳レコードに紐づく CommentItem を取得する。存在しない場合は新規作成して紐付ける。
    ///     Id が 0 の場合は自動的に台帳レコードを DB に永続化した上で CommentItem を確保する。
    /// </summary>
    Task<Item> EnsureCommentItemAsync(TagWeightLedger ledger, string userId);
}