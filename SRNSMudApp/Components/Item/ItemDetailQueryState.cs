using System.Globalization;

using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;

namespace SRNSMudApp.Components.Item;

/// <summary>
///     ItemDetail ページの URL クエリ状態を表す値オブジェクト。
///     URL 形式は以下のキーで構成される:
///     <list type="bullet">
///         <item><c>tab</c>: アクティブタグ (<c>details</c> | <c>requests</c> | <c>history</c>、既定 <c>details</c>)</item>
///         <item><c>requestId</c>: 選択中の関連リクエスト ID (単一)</item>
///     </list>
///     URL のパースと再構築をこのクラスに集約することで、
///     コンポーネントから URL 形式の知識を分離し単体テスト可能にする。
/// </summary>
public sealed record ItemDetailQueryState
{
    private const string TabKey = "tab";
    private const string RequestIdKey = "requestId";

    /// <summary>アクティブタブの正規化前文字列 (未知の値はそのまま保持される)。</summary>
    public string? ActiveTab { get; init; }

    /// <summary>選択中の関連リクエスト ID。</summary>
    public int? SelectedRequestId { get; init; }

    /// <summary>
    ///     URI のクエリ部分をパースして <see cref="ItemDetailQueryState" /> を生成する。
    ///     不正な値は黙って無視する。
    /// </summary>
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
            SelectedRequestId = ParseRequestId(query)
        };
    }

    /// <summary>
    ///     タブ文字列をタブインデックスへ変換する。未知の値は既定タブ (0) へフォールバックする。
    /// </summary>
    public static int ToTabIndex(string? tab) => tab switch
    {
        "requests" => 1,
        "history" => 2,
        _ => 0
    };

    /// <summary>
    ///     タブインデックスを正規化されたタブ文字列へ変換する。
    /// </summary>
    public static string FromTabIndex(int index) => index switch
    {
        1 => "requests",
        2 => "history",
        _ => "details"
    };

    /// <summary>
    ///     タブインデックスと選択リクエスト ID から正規化された状態を生成する。
    /// </summary>
    public static ItemDetailQueryState Create(int tabIndex, int? selectedRequestId) =>
        new()
        {
            ActiveTab = FromTabIndex(tabIndex),
            SelectedRequestId = selectedRequestId
        };

    /// <summary>
    ///     NavigationManager.GetUriWithQueryParameters に渡すパラメータ辞書を生成する。
    ///     null のキーは URL から削除される。
    /// </summary>
    public Dictionary<string, object?> BuildParameters() =>
        new()
        {
            { TabKey, ActiveTab },
            { RequestIdKey, SelectedRequestId }
        };

    private static int? ParseRequestId(Dictionary<string, StringValues> query)
    {
        return query.TryGetValue(RequestIdKey, out StringValues values) &&
               values.Count > 0 &&
               int.TryParse(values[0], CultureInfo.InvariantCulture, out var id) &&
               id > 0
            ? id
            : null;
    }
}