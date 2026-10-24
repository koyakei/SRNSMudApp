using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;

namespace SRNSMudApp.Services;

/// <summary>
///     タグ付けコントラクト（Gratis / Mutual / Trigger / Bounty）の提案・承認・キャンセルを調整するサービスインターフェース。
/// </summary>
public interface ITaggingContractService
{
    /// <summary>
    ///     Gratis コントラクトを提案する。タグの <c>AutoAcceptIncomingTaggingRequests</c> が有効な場合は即時承認される。
    /// </summary>
    Task<Result<TaggingRequestEntity>> ProposeGratisContractAsync(
        string requesterUserId,
        string tagOwnerUserId,
        int targetItemId,
        int requestedTagId,
        TaggingRequestType requestType = TaggingRequestType.Add,
        int proposedWeight = 1,
        string? message = null);

    /// <summary>
    ///     TagEdge に対する Gratis コントラクトを提案する。タグの <c>AutoAcceptIncomingTaggingRequests</c> が有効な場合は即時承認される。
    /// </summary>
    Task<Result<TaggingRequestEntity>> ProposeGratisEdgeContractAsync(
        string requesterUserId,
        string tagOwnerUserId,
        int tagEdgeId,
        int requestedTagId,
        TaggingRequestType requestType = TaggingRequestType.Add,
        int proposedWeight = 1,
        string? message = null);

    /// <summary>
    ///     Mutual コントラクトを提案する。タグの <c>AutoAcceptIncomingTaggingRequests</c> が有効な場合は即時承認される。
    /// </summary>
    Task<Result<TaggingRequestEntity>> ProposeMutualContractAsync(
        string requesterUserId,
        string tagOwnerUserId,
        int targetItemId,
        int requestedTagId,
        int offeredTargetItemId,
        int offeredTagId,
        int consumedRightAssetId,
        TaggingRequestType requestType = TaggingRequestType.Add,
        int proposedWeight = 1);

    /// <summary>
    ///     指定アイテムに紐づくコントラクト一覧を取得する。
    /// </summary>
    Task<IReadOnlyList<TaggingRequestEntity>> GetRequestsByItemIdAsync(int itemId);

    /// <summary>
    ///     指定 TagEdge に紐づくコントラクト一覧を取得する。
    /// </summary>
    Task<IReadOnlyList<TaggingRequestEntity>> GetRequestsByEdgeIdAsync(int edgeId);

    /// <summary>
    ///     指定コントラクトを承認・実行する。実行処理は <see cref="Contracts.IContractExecutor" /> に委譲する。
    /// </summary>
    Task<Result<string>> AcceptContractAsync(int contractId, string currentUserId, int? fulfillerAssetId = null);

    /// <summary>
    ///     指定コントラクトをキャンセル（取り下げ）する。状態は <see cref="TradeStatus.Canceled" /> に遷移する。
    /// </summary>
    Task<Result<string>> CancelContractAsync(int contractId, string currentUserId);

    /// <summary>
    ///     指定コントラクトを拒否（却下）する。状態は <see cref="TradeStatus.Rejected" /> に遷移する。
    /// </summary>
    Task<Result<string>> RejectContractAsync(int contractId, string currentUserId, string? rejectionReason = null);
}