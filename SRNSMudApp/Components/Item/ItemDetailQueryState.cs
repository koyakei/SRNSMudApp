namespace SRNSMudApp.Components.Item;

/// <summary>
///     ItemDetail ページの URL クエリ状態を表す値オブジェクト。
///     URL 形式は以下のキーで構成される:
///     <list type="bullet">
///         <item><c>tab</c>: アクティブタブ (<c>details</c> | <c>requests</c> | <c>history</c>、既定 <c>details</c>)</item>
///         <item><c>requestId</c>: 選択中の関連リクエスト ID (単一)</item>
///         <item><c>f</c>: フィルタ (<c>&lt;tagId&gt;</c>、<c>&lt;tagId&gt;@&lt;userName&gt;</c>、<c>name:&lt;tagName&gt;</c>、<c>name:&lt;tagName&gt;@&lt;userName&gt;</c>)</item>
///     </list>
///     URL クエリから復元された ItemDetail の表示状態を表す値オブジェクト。
/// </summary>
public sealed record ItemDetailQueryState
{
    /// <summary>アクティブタブの正規化前文字列 (未知の値はそのまま保持される)。</summary>
    public string? ActiveTab { get; init; }

    /// <summary>選択中の関連リクエスト ID。</summary>
    public int? SelectedRequestId { get; init; }

    /// <summary>適用中のタグフィルタ一覧 (ItemList と共通仕様)。</summary>
    public IReadOnlyList<FilterEntry> Filters { get; init; } = [];
}