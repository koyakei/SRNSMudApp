using SRNSMudApp.Data;

namespace SRNSMudApp.Services;

/// <summary>
///     タイムラインイベント（TimelineEvent）の記録を担当するサービスインターフェース。
///     ItemTagService からタイムライン記録の責務を分離し、単一責任の原則（SRP）を担保する。
/// </summary>
public interface ITimelineRecorder
{
    /// <summary>
    ///     タグとアイテムの関連付け追加イベントを記録する。
    /// </summary>
    void RecordTagRelationAdded(ApplicationDbContext context, string userId, int itemId, int tagId, int weight = 1);

    /// <summary>
    ///     タグとアイテムの関連付け削除イベントを記録する。
    /// </summary>
    void RecordTagRelationDeleted(ApplicationDbContext context, string userId, int itemId, int tagId, int previousWeight);

    /// <summary>
    ///     タグとアイテムのウェイト更新イベントを記録する。
    /// </summary>
    void RecordTagRelationUpdated(ApplicationDbContext context, string userId, int itemId, int tagId, int previousWeight, int newWeight);
}