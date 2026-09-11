using SRNSMudApp.Data;

namespace SRNSMudApp.Services;

/// <summary>
///     アイテムの引用（引用リツイート）および引用されたアイテム一覧の取得を担当するサービスインターフェース。
/// </summary>
public interface IItemQuoteService
{
    /// <summary>
    ///     指定したアイテムを引用した新しいアイテムを投稿する。
    /// </summary>
    /// <param name="quotedItemId">引用元のアイテムID。</param>
    /// <param name="content">引用投稿の本文。</param>
    /// <param name="userId">投稿者のユーザーID。</param>
    /// <param name="initialTagIds">初期付与するタグIDのコレクション（任意）。</param>
    /// <param name="cancellationToken">キャンセレーショントークン。</param>
    /// <returns>作成されたアイテム。引用元が存在しない場合は <see langword="null" />。</returns>
    Task<Item?> CreateQuoteItemAsync(
        int quotedItemId,
        string content,
        string userId,
        IReadOnlyCollection<int>? initialTagIds = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     指定した元アイテムを引用しているアイテムの一覧を取得する。
    /// </summary>
    /// <param name="quotedItemId">元アイテムID。</param>
    /// <param name="cancellationToken">キャンセレーショントークン。</param>
    /// <returns>引用しているアイテムのリスト。該当するアイテムがない場合は空のリスト。</returns>
    Task<IReadOnlyList<Item>> GetQuotedByItemsAsync(
        int quotedItemId,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     指定した元アイテムが引用されている件数を取得する。
    /// </summary>
    /// <param name="quotedItemId">元アイテムID。</param>
    /// <param name="cancellationToken">キャンセレーショントークン。</param>
    /// <returns>引用件数。該当する引用がない場合は 0。</returns>
    Task<int> GetQuoteCountAsync(
        int quotedItemId,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     引用プレビュー表示用に引用元アイテムを取得する。
    /// </summary>
    /// <param name="quotedItemId">引用元アイテムID。</param>
    /// <param name="cancellationToken">キャンセレーショントークン。</param>
    /// <returns>アイテム。存在しない場合は null。</returns>
    Task<Item?> GetQuotedItemAsync(
        int quotedItemId,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     指定したアイテムの引用元（または分割元）アイテムを取得する。
    ///     QuotedItemId の参照のほか、分割リクエストや本文内リンクからのフォールバック解決も行う。
    /// </summary>
    /// <param name="itemId">対象アイテムID。</param>
    /// <param name="cancellationToken">キャンセレーショントークン。</param>
    /// <returns>引用元または分割元のアイテム。存在しない場合は null。</returns>
    Task<Item?> GetSourceItemAsync(
        int itemId,
        CancellationToken cancellationToken = default);
}