namespace SRNSMudApp.Models;

/// <summary>
///     三相リアクション（真実・善・美）のタグ名定数およびヘルパー。
/// </summary>
public static class ReactionTagNames
{
    public const string Shinji = "真実";
    public const string Zen = "善";
    public const string Bi = "美";

    public static readonly IReadOnlyList<string> All = [Shinji, Zen, Bi];

    public static bool IsReactionTagName(string? tagName) =>
        tagName is Shinji or Zen or Bi;
}