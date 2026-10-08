// Services/TagVisibilityExtensions.cs
#region

using SRNSMudApp.Data;

#endregion

namespace SRNSMudApp.Services;

/// <summary>
///     タグの可視性制御をクエリおよびエンティティに適用する拡張メソッド。
///     BAN されたユーザーがオーナーのタグは不可視とする。
/// </summary>
public static class TagVisibilityExtensions
{
    /// <summary>
    ///     オーナーが BAN されていないタグのみにフィルタリングする。
    ///     （Owner が null の場合、または Owner.IsBanned が false の場合のみ可視）
    /// </summary>
    /// <param name="query">対象のタグクエリ。</param>
    /// <returns>可視なタグのみに絞り込まれたクエリ。</returns>
    public static IQueryable<Tag> WhereVisibleToUser(this IQueryable<Tag> query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return query.Where(t => t.Owner == null || !t.Owner.IsBanned);
    }

    /// <summary>
    ///     メモリ上の単一タグに対して、タグが可視（オーナーが BAN されていない）かを判定する。
    /// </summary>
    /// <param name="tag">対象のタグ。</param>
    /// <returns>オーナーが BAN されていない場合は true、それ以外は false。</returns>
    public static bool IsTagVisibleToUser(this Tag tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        return tag.Owner == null || !tag.Owner.IsBanned;
    }
}