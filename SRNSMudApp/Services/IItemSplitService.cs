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
    Task<Result<ItemSplitRequest>> RequestSplitAsync(int originalItemId, string selectedText, string requesterUserId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     アイテム所有者が分割リクエストを承認する。
    ///     新規アイテムが作成され、元アイテムの本文内の選択テキストが新規アイテムへのリンクに置換される。
    /// </summary>
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