using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;

namespace SRNSMudApp.Services;

public interface ITagEdgeService
{
    /// <summary>SourceTag→TargetTag の Edge を作成する。同一組が既に存在する場合は失敗する。</summary>
    Task<Result<TagEdge>> CreateEdgeAsync(int sourceTagId, int targetTagId, string ownerId);

    /// <summary>Edge を削除する（紐付けられた TagEdgeTagAttachment も CASCADE で削除される）。</summary>
    Task<Result<bool>> DeleteEdgeAsync(int edgeId, string ownerId);

    /// <summary>
    ///     Edge に意味付けタグを紐付ける。
    ///     指定された RightAsset（未消費・OwnerId が currentUserId と一致・TargetTagId が tagId と一致）を
    ///     1 消費し、Amount が 0 になれば Burn する。成功時は Tag.CachedWeight を weight 分加算し、
    ///     TagWeightLedger に記録する。
    /// </summary>
    Task<Result<TagEdgeTagAttachment>> AttachTagToEdgeAsync(
        int edgeId, int tagId, int rightAssetId, string currentUserId, int weight = 1);

    /// <summary>
    ///     紐付けを解除する。Tag.CachedWeight を減算し TagWeightLedger に記録するが、
    ///     消費済みの RightAsset は返却しない（権利消費は不可逆という仕様）。
    /// </summary>
    Task<Result<bool>> DetachTagFromEdgeAsync(int attachmentId, string currentUserId);

    /// <summary>指定タグが Source または Target として関与する Edge 一覧を取得する。</summary>
    Task<IReadOnlyList<TagEdge>> GetEdgesForTagAsync(int tagId);

    /// <summary>指定 Edge に紐付けられている意味付けタグ一覧を取得する。</summary>
    Task<IReadOnlyList<TagEdgeTagAttachment>> GetAttachmentsForEdgeAsync(int edgeId);

    /// <summary>全 Edge を関連タグおよび紐付けタグを含めて取得する。</summary>
    Task<IReadOnlyList<TagEdge>> GetAllEdgesAsync();
}