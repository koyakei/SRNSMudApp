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
    Task<bool> SplitSelectionAsync(Item item, string currentUserId);

    /// <summary>
    ///     他ユーザーのアイテムに対してテキスト分割リクエストを送信する。
    /// </summary>
    Task<ItemSplitRequest?> RequestSplitSelectionAsync(Item item, string currentUserId);

    /// <summary>
    ///     保留中の分割リクエストを承認し、本文内にリンクを挿入する。
    /// </summary>
    Task<bool> ApproveSplitRequestAsync(Item item, ItemSplitRequest request, string currentUserId);

    /// <summary>
    ///     却下理由ダイアログを表示して分割リクエストを却下する。
    /// </summary>
    Task<bool> RejectSplitRequestAsync(Item item, ItemSplitRequest request, string currentUserId);

    /// <summary>
    ///     自身が送信した分割リクエストを取り下げる。
    /// </summary>
    Task<bool> CancelSplitRequestAsync(ItemSplitRequest request, string currentUserId);
}