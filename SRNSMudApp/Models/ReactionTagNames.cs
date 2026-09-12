namespace SRNSMudApp.Models;

/// <summary>
///     三相リアクション（真実・善・美）のタグ名定数およびヘルパー。
/// </summary>
public static class ReactionTagNames
{
    /// <summary>「真実」リアクションタグ名。</summary>
    public const string Shinji = "真実";

    /// <summary>「善」リアクションタグ名。</summary>
    public const string Zen = "善";

    /// <summary>「美」リアクションタグ名。</summary>
    public const string Bi = "美";

    /// <summary>すべての三相リアクションタグ名の一覧。</summary>
    public static readonly IReadOnlyList<string> All = [Shinji, Zen, Bi];

    /// <summary>
    ///     指定されたタグ名が三相リアクションタグのいずれかであるかを判定する。
    /// </summary>
    /// <param name="tagName">判定対象のタグ名。</param>
    /// <returns>三相リアクションタグ名に合致する場合は true、それ以外は false。</returns>
    public static bool IsReactionTagName(string? tagName) =>
        tagName is Shinji or Zen or Bi;
}