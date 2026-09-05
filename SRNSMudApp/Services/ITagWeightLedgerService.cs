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
        string userId);

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
}