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

    /// <summary>
    ///     他ユーザーのアイテム本文の一部が選択されている場合に、そのテキストを引用したアイテムを投稿し、
    ///     その引用アイテムに対して指定されたリアクションタグを投票した上で、分割リクエストを送信する。
    ///     分割リクエスト承認時は、引用アイテムに付与されたリアクションタグや関係ごと新アイテムへ置き換えられる。
    /// </summary>
    /// <param name="item">対象の親アイテム。</param>
    /// <param name="currentUserId">操作を実行するユーザー ID。</param>
    /// <param name="reactionTagName">投票するリアクションタグ名（例: 真、善、美）。</param>
    /// <param name="targetWeight">投票するウェイト（1 または -1）。</param>
    /// <param name="reactionTagId">現在のユーザーの該当リアクションタグ ID（未解決時は null）。</param>
    /// <param name="allTags">システムタグ等を含む全タグ一覧。</param>
    /// <param name="ensureSystemTagsAsync">システムタグ未作成時に作成を保証するコールバック。</param>
    /// <param name="selectedText">選択されたテキスト。null または空の場合は JS 経由で取得を試みる。</param>
    /// <returns>処理が完了した場合は結果情報、キャンセルまたは失敗した場合は null。</returns>
    Task<ItemSplitWithReactionResult?> RequestSplitWithReactionAsync(
        Item item,
        string currentUserId,
        string reactionTagName,
        int targetWeight,
        int? reactionTagId,
        IReadOnlyList<Tag> allTags,
        Func<Task>? ensureSystemTagsAsync,
        string? selectedText = null);

    /// <summary>
    ///     他ユーザーのアイテム本文の一部が選択されているかを検証し、分割対象として有効なテキストを取得する。
    ///     未ログイン、自身所有アイテム、テキスト未選択、またはアイテム本文に含まれていない場合は null を返す。
    /// </summary>
    /// <param name="item">検証対象のアイテム。</param>
    /// <param name="currentUserId">現在ログイン中のユーザー ID。</param>
    /// <returns>有効な選択テキスト。無効または対象外の場合は null。</returns>
    Task<string?> GetValidSelectedTextForSplitAsync(Item item, string? currentUserId);
}

/// <summary>
///     テキスト選択時のリアクション投票・引用投稿・分割リクエスト送信の結果。
/// </summary>
/// <param name="SplitRequest">作成されたアイテム分割リクエスト。</param>
/// <param name="VoteResult">作成された引用アイテムに対するリアクション投票結果。</param>
/// <param name="QuoteItem">作成された引用アイテム。</param>
public sealed record ItemSplitWithReactionResult(
    ItemSplitRequest SplitRequest,
    ItemVoteResult VoteResult,
    Item QuoteItem);