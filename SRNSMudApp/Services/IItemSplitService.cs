using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;

namespace SRNSMudApp.Services;

/// <summary>
///     他ユーザーの Item に対する「選択テキストを別アイテムに分割」リクエストの申請、承認、却下、取り下げを管理するドメインサービス。
/// </summary>
public interface IItemSplitService
{
    /// <summary>
    ///     他ユーザーの Item に対し、選択テキストを別アイテムに分割するリクエストを申請する。
    /// </summary>
    /// <param name="originalItemId">分割元アイテムの ID。</param>
    /// <param name="selectedText">分割対象として選択された本文。</param>
    /// <param name="requesterUserId">リクエストを申請するユーザーの ID。</param>
    /// <param name="cancellationToken">キャンセレーショントークン。</param>
    /// <returns>作成された分割リクエスト、または失敗理由。</returns>
    Task<Result<ItemSplitRequest>> RequestSplitAsync(int originalItemId, string selectedText, string requesterUserId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     アイテム所有者による選択テキストの直接分割を実行する。
    ///     新規アイテムを作成し、元アイテムの本文内の選択テキストを新規アイテムへのリンクに置換する。
    /// </summary>
    /// <param name="originalItemId">分割元アイテムの ID。</param>
    /// <param name="selectedText">分割対象として選択された本文。</param>
    /// <param name="ownerUserId">分割元アイテムの所有者ユーザー ID。</param>
    /// <param name="cancellationToken">キャンセレーショントークン。</param>
    /// <returns>作成されたアイテム、または失敗理由。</returns>
    Task<Result<Item>> SplitDirectlyAsync(
        int originalItemId,
        string selectedText,
        string ownerUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     アイテム所有者が分割リクエストを承認する。
    ///     新規アイテムが作成され、元アイテムの本文内の選択テキストが新規アイテムへのリンクに置換される。
    /// </summary>
    /// <param name="splitRequestId">承認対象の分割リクエスト ID。</param>
    /// <param name="ownerUserId">分割元アイテムの所有者ユーザー ID。</param>
    /// <param name="cancellationToken">キャンセレーショントークン。</param>
    /// <returns>作成されたアイテム、または失敗理由。</returns>
    Task<Result<Item>> ApproveSplitAsync(int splitRequestId, string ownerUserId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     アイテム所有者が分割リクエストを却下する。
    /// </summary>
    Task<Result<bool>> RejectSplitAsync(int splitRequestId, string ownerUserId, string? rejectReason, CancellationToken cancellationToken = default);

    /// <summary>
    ///     リクエスト送信者が申請中の分割リクエストを取り下げる。
    /// </summary>
    Task<Result<bool>> CancelSplitAsync(int splitRequestId, string requesterUserId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     指定されたアイテムに対する保留中（Proposed）の分割リクエスト一覧を取得する。
    /// </summary>
    Task<IReadOnlyList<ItemSplitRequest>> GetPendingSplitRequestsForOriginalItemAsync(int itemId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     指定された分割リクエストを関連エンティティ込みで取得する。
    /// </summary>
    Task<ItemSplitRequest?> GetSplitRequestByIdAsync(int splitRequestId, CancellationToken cancellationToken = default);
}