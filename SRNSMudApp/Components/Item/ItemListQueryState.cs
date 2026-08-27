using System.Globalization;

using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;

namespace SRNSMudApp.Components.Item;

/// <summary>URL クエリ上のタグフィルタ 1 件分 (タグ ID またはタグ名 + 任意ユーザー名)。</summary>
public sealed record FilterEntry(int? TagId, string? TagName, string? UserName)
{
    public static FilterEntry FromId(int tagId, string? userName = null) => new(tagId, null, userName);
    public static FilterEntry FromName(string tagName, string? userName = null) => new(null, tagName, userName);
}

/// <summary>
///     ItemList ページの URL クエリ状態を表す値オブジェクト。
///     URL 形式は以下の繰り返し可能なキーで構成される:
///     <list type="bullet">
///         <item><c>f</c>: フィルタ (<c>&lt;tagId&gt;</c>、<c>&lt;tagId&gt;@&lt;userName&gt;</c>、<c>name:&lt;tagName&gt;</c>、<c>name:&lt;tagName&gt;@&lt;userName&gt;</c>)</item>
///         <item><c>sort</c>: ソート (<c>&lt;tagId&gt;:&lt;asc|desc&gt;</c>、出現順 = 優先度)</item>
///         <item><c>item</c>: 選択アイテム ID (将来の一括操作に備えた予約域)</item>
///         <item><c>focus</c>: スクロール対象アイテム ID (単一)</item>
///         <item><c>focusTag</c>: スクロール対象タグ ID (単一)</item>
///     </list>
///     URL のパースと再構築をこのクラスに集約することで、
///     コンポーネントから URL 形式の知識を分離し単体テスト可能にする。
/// </summary>
public sealed record ItemListQueryState
{
    /// <summary>適用中のタグフィルタ一覧。</summary>
    public IReadOnlyList<FilterEntry> Filters { get; init; } = [];

    /// <summary>適用中のソート条件一覧 (出現順 = 優先度)。</summary>
    public IReadOnlyList<SortEntry> SortEntries { get; init; } = [];

    /// <summary>選択中のアイテム ID 一覧。</summary>
    public IReadOnlyList<int> SelectedItemIds { get; init; } = [];

    /// <summary>スクロール対象のアイテム ID。</summary>
    public int? FocusItemId { get; init; }

    /// <summary>スクロール対象のタグ ID。</summary>
    public int? FocusTagId { get; init; }

    private const string FilterKey = "f";
    private const string SortKey = "sort";
    private const string ItemKey = "item";
    private const string FocusKey = "focus";
    private const string FocusTagKey = "focusTag";

    private const string NamePrefix = "name:";

    /// <summary>
    ///     URI のクエリ部分をパースして <see cref="ItemListQueryState" /> を生成する。
    ///     不正な値は黙って無視する。
    /// </summary>
    public static ItemListQueryState ParseFromUri(Uri uri)
    {
        Dictionary<string, StringValues> query = QueryHelpers.ParseQuery(uri.Query);

        return new ItemListQueryState
        {
            Filters = [.. ParseFilters(query)],
            SortEntries = [.. ParseSortEntries(query)],
            SelectedItemIds = [.. ParseInts(query, ItemKey)],
            FocusItemId = ParseSingleInt(query, FocusKey),
            FocusTagId = ParseSingleInt(query, FocusTagKey)
        };
    }

    /// <summary>
    ///     NavigationManager.GetUriWithQueryParameters に渡すパラメータ辞書を生成する。
    ///     空のコレクションは null (キー削除) に変換する。
    /// </summary>
    public Dictionary<string, object?> BuildParameters()
    {
        return new Dictionary<string, object?>
        {
            { FilterKey, Filters.Count > 0 ? Filters.Select(EncodeFilter).ToArray() : null },
            { SortKey, SortEntries.Count > 0 ? SortEntries.Select(EncodeSort).ToArray() : null },
            { ItemKey, SelectedItemIds.Count > 0 ? SelectedItemIds.ToArray() : null },
            { FocusKey, FocusItemId },
            { FocusTagKey, FocusTagId }
        };
    }

    private static IEnumerable<FilterEntry> ParseFilters(Dictionary<string, StringValues> query)
    {
        if (!query.TryGetValue(FilterKey, out StringValues values))
        {
            yield break;
        }

        foreach (var value in values)
        {
            if (string.IsNullOrEmpty(value))
            {
                continue;
            }

            var separatorIndex = value.IndexOf('@', StringComparison.Ordinal);
            var mainPart = separatorIndex < 0 ? value : value[..separatorIndex];
            var userName = separatorIndex < 0 ? null : value[(separatorIndex + 1)..];

            if (mainPart.StartsWith(NamePrefix, StringComparison.OrdinalIgnoreCase))
            {
                var tagName = mainPart[NamePrefix.Length..];
                if (!string.IsNullOrWhiteSpace(tagName))
                {
                    yield return FilterEntry.FromName(tagName, userName);
                }
            }
            else if (int.TryParse(mainPart, out var tagId) && tagId > 0)
            {
                yield return FilterEntry.FromId(tagId, userName);
            }
        }
    }

    private static IEnumerable<SortEntry> ParseSortEntries(Dictionary<string, StringValues> query)
    {
        if (!query.TryGetValue(SortKey, out StringValues values))
        {
            yield break;
        }

        foreach (var value in values)
        {
            if (string.IsNullOrEmpty(value))
            {
                continue;
            }

            var parts = value.Split(':');
            if (parts.Length == 2 && int.TryParse(parts[0], out var tagId))
            {
                switch (parts[1])
                {
                    case "asc":
                        yield return new SortEntry(tagId, SortOrder.Asc);
                        break;
                    case "desc":
                        yield return new SortEntry(tagId, SortOrder.Desc);
                        break;
                    default:
                        break;
                }
            }
        }
    }

    private static IEnumerable<int> ParseInts(Dictionary<string, StringValues> query, string key)
    {
        if (!query.TryGetValue(key, out StringValues values))
        {
            yield break;
        }

        foreach (var value in values)
        {
            if (int.TryParse(value, out var id) && id > 0)
            {
                yield return id;
            }
        }
    }

    private static int? ParseSingleInt(Dictionary<string, StringValues> query, string key)
    {
        return query.TryGetValue(key, out StringValues values) &&
               values.Count > 0 &&
               int.TryParse(values[0], out var id) &&
               id > 0
            ? id
            : null;
    }

    private static string EncodeFilter(FilterEntry filter)
    {
        if (filter.TagId.HasValue)
        {
            return string.IsNullOrEmpty(filter.UserName)
                ? filter.TagId.Value.ToString(CultureInfo.InvariantCulture)
                : $"{filter.TagId.Value}@{filter.UserName}";
        }

        return string.IsNullOrEmpty(filter.UserName)
            ? $"{NamePrefix}{filter.TagName}"
            : $"{NamePrefix}{filter.TagName}@{filter.UserName}";
    }

    private static string EncodeSort(SortEntry entry) =>
        $"{entry.TagId}:{(entry.Order == SortOrder.Asc ? "asc" : "desc")}";
}