#region

using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;

#endregion

namespace SRNSMudApp.Components.Item;

/// <summary>
///     ItemList ページの URL クエリパラメータ（tags / sort / focusItem）を表す値オブジェクト。
///     URLのパースと再構築ロジックをコンポーネントから分離することで、単体テストを可能にする。
/// </summary>
public sealed class ItemListQueryState
{
    // ────────────────────────────────────────────────────────────
    // ネストした型
    // ────────────────────────────────────────────────────────────



    /// <summary>フィルタとして選択されているタグIDのリスト。</summary>
    public IReadOnlyList<int> TagIds { get; init; } = [];

    /// <summary>ソート条件のリスト（タグID : 昇順/降順）。</summary>
    public IReadOnlyList<SortEntry> SortEntries { get; init; } = [];

    /// <summary>フォーカス対象のアイテムID。URLに focusItem=xx が含まれている場合にセットされる。</summary>
    public int? FocusItemId { get; init; }

    // ────────────────────────────────────────────────────────────
    // ファクトリ
    // ────────────────────────────────────────────────────────────

    /// <summary>
    ///     URI 文字列のクエリ部分をパースして <see cref="ItemListQueryState" /> を生成する。
    /// </summary>
    public static ItemListQueryState ParseFromUri(string uriString)
    {
        var uri = new Uri(uriString, UriKind.RelativeOrAbsolute);
        var queryStr = uri.IsAbsoluteUri ? uri.Query : ExtractQuery(uriString);
        Dictionary<string, StringValues> query = QueryHelpers.ParseQuery(queryStr);

        // tags
        List<int> tagIds = [];
        if (query.TryGetValue("tags", out StringValues tagsValues))
        {
            foreach (var v in tagsValues)
            {
                if (int.TryParse(v, out var id) && id > 0)
                {
                    tagIds.Add(id);
                }
            }
        }

        // sort
        List<SortEntry> sortEntries = [];
        if (query.TryGetValue("sort", out StringValues sortStr))
        {
            var parts = sortStr.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var kvp = part.Split(':');
                if (kvp.Length == 2 && int.TryParse(kvp[0], out var tagId))
                {
                    SortOrder order = kvp[1] switch
                    {
                        "asc" => SortOrder.Asc,
                        "desc" => SortOrder.Desc,
                        _ => SortOrder.Desc
                    };
                    sortEntries.Add(new SortEntry(tagId, order));
                }
            }
        }

        // focusItem
        int? focusItemId = null;
        if (query.TryGetValue("focusItem", out StringValues focusStr) && int.TryParse(focusStr, out var fid))
        {
            focusItemId = fid;
        }

        return new ItemListQueryState { TagIds = tagIds, SortEntries = sortEntries, FocusItemId = focusItemId };
    }

    // ────────────────────────────────────────────────────────────
    // クエリ文字列の生成
    // ────────────────────────────────────────────────────────────

    /// <summary>
    ///     NavigationManager.GetUriWithQueryParameters に渡す Dictionary を生成する。
    /// </summary>
    public Dictionary<string, object?> BuildParameters()
    {
        var sortParam = string.Join(",",
            SortEntries.Select(e => $"{e.TagId}:{(e.Order == SortOrder.Desc ? "desc" : "asc")}"));

        return new Dictionary<string, object?>
        {
            { "tags", TagIds.Count > 0 ? (object)TagIds.ToArray() : null },
            { "sort", string.IsNullOrEmpty(sortParam) ? null : sortParam }
        };
    }

    // ────────────────────────────────────────────────────────────
    // ヘルパー
    // ────────────────────────────────────────────────────────────

    private static string ExtractQuery(string uriString)
    {
        var idx = uriString.IndexOf('?', StringComparison.Ordinal);
        return idx >= 0 ? uriString[idx..] : string.Empty;
    }

    public static ItemListQueryState ParseFromUri(Uri uriString) => throw new NotImplementedException();

}