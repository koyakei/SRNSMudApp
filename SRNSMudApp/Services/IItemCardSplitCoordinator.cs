using SRNSMudApp.Data;

namespace SRNSMudApp.Services;

/// <summary>
///     ItemCard におけるテキスト分割（直接分割および分割リクエスト送信・承認・却下・取消）を調整するコーディネーター。
/// </summary>
public interface IItemCardSplitCoordinator
{
    /// <summary>
    ///     選択されたテキストを直接別アイテムに分割し、本文内にリンクを挿入する。
    /// </summary>
    /// <param name="item">分割元となるアイテム。</param>
    /// <param name="currentUserId">操作を実行する現在ログイン中のユーザー ID。</param>
    /// <returns>分割が成功した場合は true、それ以外は false。</returns>
    Task<bool> SplitSelectionAsync(Item item, string currentUserId);

    /// <summary>
    ///     他ユーザーのアイテムに対してテキスト分割リクエストを送信する。
    /// </summary>
    /// <param name="item">リクエスト対象のアイテム。</param>
    /// <param name="currentUserId">リクエストを送信する現在ログイン中のユーザー ID。</param>
    /// <returns>リクエストが作成・送信された場合は <see cref="ItemSplitRequest" />、キャンセルの場合は null。</returns>
    Task<ItemSplitRequest?> RequestSplitSelectionAsync(Item item, string currentUserId);

    /// <summary>
    ///     保留中の分割リクエストを承認し、本文内にリンクを挿入する。
    /// </summary>
    /// <param name="item">分割元アイテム。</param>
    /// <param name="request">承認対象の分割リクエスト。</param>
    /// <param name="currentUserId">承認を実行するオーナーユーザーの ID。</param>
    /// <returns>承認処理が成功した場合は true、それ以外は false。</returns>
    Task<bool> ApproveSplitRequestAsync(Item item, ItemSplitRequest request, string currentUserId);

    /// <summary>
    ///     却下理由ダイアログを表示して分割リクエストを却下する。
    /// </summary>
    /// <param name="item">対象アイテム。</param>
    /// <param name="request">却下対象の分割リクエスト。</param>
    /// <param name="currentUserId">却下を実行するオーナーユーザーの ID。</param>
    /// <returns>却下処理が完了した場合は true、キャンセル等の場合は false。</returns>
    Task<bool> RejectSplitRequestAsync(Item item, ItemSplitRequest request, string currentUserId);

    /// <summary>
    ///     自身が送信した分割リクエストを取り下げる。
    /// </summary>
    /// <param name="request">取り下げる分割リクエスト。</param>
    /// <param name="currentUserId">取り下げを実行するリクエスト作成者のユーザー ID。</param>
    /// <returns>取り下げが成功した場合は true、それ以外は false。</returns>
    Task<bool> CancelSplitRequestAsync(ItemSplitRequest request, string currentUserId);
}