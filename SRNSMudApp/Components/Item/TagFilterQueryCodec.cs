using System.Globalization;

using Microsoft.Extensions.Primitives;

using SRNSMudApp.Models;

namespace SRNSMudApp.Components.Item;

/// <summary>URL クエリ上のタグフィルタ 1 件分 (タグ ID またはタグ名 + 任意ユーザー名)。</summary>
public sealed record FilterEntry(int? TagId, string? TagName, string? UserName)
{
    public static FilterEntry FromId(int tagId, string? userName = null) => new(tagId, null, userName);
    public static FilterEntry FromName(string tagName, string? userName = null) => new(null, tagName, userName);
}

/// <summary>
///     タグフィルタの URL クエリ（<c>f</c> パラメータ）のパースとエンコード、
///     および検索文字列（<see cref="TagSearchQuery" />）との相互変換を担当するユーティリティ。
///     ItemList と ItemDetail で共通の URL クエリ仕様を提供する。
/// </summary>
public static class TagFilterQueryCodec
{
    public const string FilterKey = "f";
    public const string NamePrefix = "name:";

    private const string SearchFallbackKey = "search";
    private const string QFallbackKey = "q";

    /// <summary>
    ///     クエリパラメータ辞書からタグフィルタ一覧をパースする。
    ///     <c>f</c> パラメータを優先し、存在しない場合は <c>search</c> / <c>q</c> をフォールバックとして解析する。
    /// </summary>
    public static IEnumerable<FilterEntry> ParseFilters(Dictionary<string, StringValues> query)
    {
        if (query.TryGetValue(FilterKey, out StringValues values) && values.Count > 0)
        {
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
                else if (int.TryParse(mainPart, CultureInfo.InvariantCulture, out var tagId) && tagId > 0)
                {
                    yield return FilterEntry.FromId(tagId, userName);
                }
            }

            yield break;
        }

        // フォールバック: search または q パラメータ
        if (query.TryGetValue(SearchFallbackKey, out StringValues searchValues) &&
            searchValues.Count > 0 &&
            !string.IsNullOrWhiteSpace(searchValues[0]))
        {
            FilterEntry? entry = FromSearchString(searchValues[0]);
            if (entry != null)
            {
                yield return entry;
            }
            yield break;
        }

        if (query.TryGetValue(QFallbackKey, out StringValues qValues) &&
            qValues.Count > 0 &&
            !string.IsNullOrWhiteSpace(qValues[0]))
        {
            FilterEntry? entry = FromSearchString(qValues[0]);
            if (entry != null)
            {
                yield return entry;
            }
        }
    }

    /// <summary>
    ///     <see cref="FilterEntry" /> を URL クエリパラメータ文字列へエンコードする。
    /// </summary>
    public static string EncodeFilter(FilterEntry filter) =>
        filter.TagId.HasValue
            ? string.IsNullOrEmpty(filter.UserName)
                ? filter.TagId.Value.ToString(CultureInfo.InvariantCulture)
                : $"{filter.TagId.Value}@{filter.UserName}"
            : string.IsNullOrEmpty(filter.UserName)
                ? $"{NamePrefix}{filter.TagName}"
                : $"{NamePrefix}{filter.TagName}@{filter.UserName}";

    /// <summary>
    ///     検索テキスト（例: "タグ名", "タグ名 @ユーザー名"）から <see cref="FilterEntry" /> を生成する。
    /// </summary>
    public static FilterEntry? FromSearchString(string? searchString)
    {
        return TagSearchQuery.Parse(searchString) switch
        {
            TagNameSearch tagNameSearch => FilterEntry.FromName(tagNameSearch.TagName),
            TagWithUserSearch tagWithUserSearch => FilterEntry.FromName(tagWithUserSearch.TagName, tagWithUserSearch.UserName),
            IncompleteSearch incompleteSearch => FilterEntry.FromName(incompleteSearch.TagName),
            _ => null
        };
    }

    /// <summary>
    ///     <see cref="FilterEntry" /> を検索バー用文字列へ変換する。
    ///     TagId のみの場合は必要に応じて <paramref name="allTags" /> からタグ名を解決する。
    /// </summary>
    public static string ToSearchString(FilterEntry filter, IEnumerable<Data.Tag>? allTags = null)
    {
        var tagName = filter.TagName;
        if (string.IsNullOrEmpty(tagName) && filter.TagId.HasValue && allTags != null)
        {
            tagName = allTags.FirstOrDefault(t => t.Id == filter.TagId.Value)?.Name;
        }

        if (string.IsNullOrEmpty(tagName) && filter.TagId.HasValue)
        {
            tagName = filter.TagId.Value.ToString(CultureInfo.InvariantCulture);
        }

        return string.IsNullOrEmpty(tagName)
            ? string.Empty
            : string.IsNullOrEmpty(filter.UserName)
                ? tagName
                : $"{tagName} @{filter.UserName}";
    }
}