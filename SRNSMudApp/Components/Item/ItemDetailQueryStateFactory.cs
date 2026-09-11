using System.Globalization;

using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;

namespace SRNSMudApp.Components.Item;

/// <summary>
///     ItemDetail のURLクエリと表示状態の変換を担当するファクトリ。
///     URL形式の知識をコンポーネントと状態値オブジェクトから分離する。
/// </summary>
public static class ItemDetailQueryStateFactory
{
    private const string TabKey = "tab";
    private const string RequestIdKey = "requestId";
    private const string FilterKey = TagFilterQueryCodec.FilterKey;

    /// <summary>URIのクエリ部分を解析して状態を生成する。</summary>
    public static ItemDetailQueryState ParseFromUri(Uri uri)
    {
        Dictionary<string, StringValues> query = QueryHelpers.ParseQuery(uri.Query);

        return new ItemDetailQueryState
        {
            ActiveTab = query.TryGetValue(TabKey, out StringValues tabValues) &&
                        tabValues.Count > 0 &&
                        !string.IsNullOrEmpty(tabValues[0])
                ? tabValues[0]
                : null,
            SelectedRequestId = ParseRequestId(query),
            Filters = [.. TagFilterQueryCodec.ParseFilters(query)]
        };
    }

    /// <summary>タブ文字列をタブインデックスへ変換する。</summary>
    public static int ToTabIndex(string? tab) => tab switch
    {
        "tags" or "requests" => 1,
        "history" => 2,
        _ => 0
    };

    /// <summary>タブインデックスを正規化されたタブ文字列へ変換する。</summary>
    public static string FromTabIndex(int index) => index switch
    {
        1 => "tags",
        2 => "history",
        _ => "details"
    };

    /// <summary>タブインデックス、選択リクエスト、フィルタから状態を生成する。</summary>
    public static ItemDetailQueryState Create(
        int tabIndex,
        int? selectedRequestId,
        IReadOnlyList<FilterEntry>? filters = null) =>
        new()
        {
            ActiveTab = FromTabIndex(tabIndex),
            SelectedRequestId = selectedRequestId,
            Filters = filters ?? []
        };

    /// <summary>状態のフィルタを画面の検索文字列へ変換する。</summary>
    public static string? ToSearchQuery(ItemDetailQueryState state, IEnumerable<Data.Tag> allTags) =>
        state.Filters.Count > 0 ? TagFilterQueryCodec.ToSearchString(state.Filters[0], allTags) : null;

    /// <summary>検索文字列をURLクエリ状態へ変換する。</summary>
    public static ItemDetailQueryState FromSearchQuery(
        string? activeTab,
        int? selectedRequestId,
        string? searchQuery)
    {
        FilterEntry? filter = TagFilterQueryCodec.FromSearchString(searchQuery);
        return new ItemDetailQueryState
        {
            ActiveTab = activeTab,
            SelectedRequestId = selectedRequestId,
            Filters = filter is not null ? [filter] : []
        };
    }

    /// <summary>NavigationManager用のクエリパラメータを生成する。</summary>
    public static Dictionary<string, object?> BuildParameters(ItemDetailQueryState state) =>
        new()
        {
            { TabKey, state.ActiveTab },
            { RequestIdKey, state.SelectedRequestId },
            { FilterKey, state.Filters.Count > 0 ? state.Filters.Select(TagFilterQueryCodec.EncodeFilter).ToArray() : null }
        };

    private static int? ParseRequestId(Dictionary<string, StringValues> query) =>
        query.TryGetValue(RequestIdKey, out StringValues values) &&
        values.Count > 0 &&
        int.TryParse(values[0], CultureInfo.InvariantCulture, out var id) &&
        id > 0
            ? id
            : null;
}