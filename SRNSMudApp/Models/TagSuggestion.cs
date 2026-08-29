namespace SRNSMudApp.Models;

/// <summary>
///     タグ検索バーのサジェスト候補 1 件分を表すモデル。
/// </summary>
public sealed record TagSuggestion(int? TagId, string TagName, string? UserName)
{
    /// <summary>
    ///     UI 表示用テキスト。UserName がある場合は "TagName @UserName"、ない場合は "TagName"。
    /// </summary>
    public string DisplayText => string.IsNullOrWhiteSpace(UserName)
        ? TagName
        : $"{TagName} @{UserName}";

    public override string ToString() => DisplayText;
}