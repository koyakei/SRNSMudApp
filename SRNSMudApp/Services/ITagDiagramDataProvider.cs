using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;

namespace SRNSMudApp.Services;

/// <summary>
///     Tag Diagram コンポーネント用のデータアクセスおよび操作を分離するインターフェース。
///     Razor コンポーネントからの直接的な DbContext 依存を断ち、テスト容易性を確保する。
/// </summary>
public interface ITagDiagramDataProvider
{
    /// <summary>
    ///     ダイアグラム上に表示可能な全タグを取得する。
    /// </summary>
    Task<List<Tag>> LoadAllTagsAsync();

    /// <summary>
    ///     全 Edge（関連タグおよび紐付けタグを含む）を取得する。
    /// </summary>
    Task<IReadOnlyList<TagEdge>> LoadAllEdgesAsync();

    /// <summary>
    ///     指定ユーザーが指定タグに対して所有している未消費の RightAsset 一覧を取得する。
    /// </summary>
    /// <param name="userId">ユーザー ID。</param>
    /// <param name="targetTagId">対象タグ ID。</param>
    Task<List<RightAsset>> GetAvailableRightAssetsAsync(string userId, int targetTagId);

    /// <summary>
    ///     SourceTag と TargetTag を結ぶ Edge を作成する。
    /// </summary>
    Task<Result<TagEdge>> CreateEdgeAsync(int sourceTagId, int targetTagId, string ownerId);

    /// <summary>
    ///     Edge を削除する。
    /// </summary>
    Task<Result<bool>> DeleteEdgeAsync(int edgeId, string ownerId);

    /// <summary>
    ///     Edge にタグを紐付ける（所有 RightAsset を消費）。
    /// </summary>
    Task<Result<TagEdgeTagAttachment>> AttachTagToEdgeAsync(
        int edgeId, int tagId, int rightAssetId, string currentUserId, int weight = 1);

    /// <summary>
    ///     Edge からタグ紐付けを解除する。
    /// </summary>
    Task<Result<bool>> DetachTagFromEdgeAsync(int attachmentId, string currentUserId);
}