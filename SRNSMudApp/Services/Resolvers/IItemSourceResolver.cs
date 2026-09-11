using SRNSMudApp.Data;

namespace SRNSMudApp.Services.Resolvers;

/// <summary>
///     アイテムの元アイテムを解決するための優先順位付き戦略を表す。
/// </summary>
public interface IItemSourceResolver
{
    /// <summary>
    ///     指定したアイテムの元アイテムを解決する。
    /// </summary>
    /// <param name="itemId">元アイテムを解決する対象のアイテム ID。</param>
    /// <param name="cancellationToken">キャンセレーショントークン。</param>
    /// <returns>解決された元アイテム。担当する解決方法に該当しない場合は <see langword="null" />。</returns>
    Task<Item?> ResolveSourceAsync(int itemId, CancellationToken cancellationToken = default);
}