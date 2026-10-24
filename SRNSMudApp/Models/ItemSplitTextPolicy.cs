#region

using System.Text.RegularExpressions;

using SRNSMudApp.Models.Unions;
using SRNSMudApp.Resources;

#endregion

namespace SRNSMudApp.Models;

/// <summary>
///     アイテム本文内での選択テキストの一致情報。
/// </summary>
/// <param name="Index">本文内の開始インデックス。</param>
/// <param name="TextToReplace">置換対象となる本文中のテキスト（改行コード含む）。</param>
public readonly record struct ItemSplitTextMatch(int Index, string TextToReplace);

/// <summary>
///     アイテム分割におけるテキスト検索、改行コード正規化、既存リンク交差判定、文字数制限検証を
///     データベース非依存で提供する純粋ドメインポリシー。
/// </summary>
public static class ItemSplitTextPolicy
{
    /// <summary>
    ///     置換後に挿入されるアイテムリンクの想定最大長（"/ItemDetail/2147483647".Length）。
    /// </summary>
    public const int MaxReplacementLength = 22;

    /// <summary>
    ///     アイテム本文の最大文字数制限。
    /// </summary>
    public const int MaxContentLength = 1000;

    /// <summary>
    ///     テキストの改行コードを正規化し、LF 形式および CRLF 形式の文字列タプルを生成する。
    /// </summary>
    /// <param name="text">対象テキスト。</param>
    /// <returns>LF 形式と CRLF 形式のタプル。</returns>
    public static (string Lf, string Crlf) NormalizeLineEndings(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string lf = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        string crlf = lf.Replace("\n", "\r\n", StringComparison.Ordinal);
        return (lf, crlf);
    }

    /// <summary>
    ///     本文内から指定された選択テキストを検索する。改行コード（CRLF / LF）の差異を正規化して検索する。
    /// </summary>
    /// <param name="content">アイテム本文。</param>
    /// <param name="selectedText">検索する選択テキスト。</param>
    /// <returns>見つかった開始位置と本文内で一致したテキスト。見つからない場合は (-1, selectedText)。</returns>
    public static (int Index, string TextToReplace) FindSelectedText(string content, string selectedText)
    {
        if (string.IsNullOrEmpty(content) || string.IsNullOrEmpty(selectedText))
        {
            return (-1, selectedText ?? string.Empty);
        }

        int index = content.IndexOf(selectedText, StringComparison.Ordinal);
        if (index >= 0)
        {
            return (index, selectedText);
        }

        var (lf, crlf) = NormalizeLineEndings(selectedText);
        index = content.IndexOf(crlf, StringComparison.Ordinal);
        if (index >= 0)
        {
            return (index, crlf);
        }

        index = content.IndexOf(lf, StringComparison.Ordinal);
        if (index >= 0)
        {
            return (index, lf);
        }

        return (-1, selectedText);
    }

    /// <summary>
    ///     置換対象範囲が、本文中の既存リンク（外部 URL または内部リンク）と交差して分断しているかを判定する。
    ///     リンク全体を包含して置換する場合は分断ではないため交差とはみなさない。
    /// </summary>
    /// <param name="content">アイテム本文。</param>
    /// <param name="targetIndex">置換対象テキストの開始インデックス。</param>
    /// <param name="targetLength">置換対象テキストの文字数。</param>
    /// <returns>リンクと交差して分断している場合は true。それ以外は false。</returns>
    public static bool IntersectsExistingLink(string content, int targetIndex, int targetLength)
    {
        if (string.IsNullOrEmpty(content) || targetIndex < 0 || targetLength <= 0)
        {
            return false;
        }

        ReadOnlySpan<char> span = content.AsSpan();
        int targetEnd = targetIndex + targetLength;

        static bool Intersects(Regex regex, ReadOnlySpan<char> text, int start, int end)
        {
            foreach (ValueMatch match in regex.EnumerateMatches(text))
            {
                int matchEnd = match.Index + match.Length;
                if (Math.Max(start, match.Index) < Math.Min(end, matchEnd)
                    && (start > match.Index || end < matchEnd))
                {
                    return true;
                }
            }

            return false;
        }

        return Intersects(ContentParser.UrlRegex(), span, targetIndex, targetEnd)
            || Intersects(ContentParser.InternalLinkRegex(), span, targetIndex, targetEnd);
    }

    /// <summary>
    ///     分割リクエスト作成時または直接分割時のテキスト選択位置を検索し、リンク交差および置換後の文字数制限を検証する。
    /// </summary>
    /// <param name="content">アイテム本文。</param>
    /// <param name="selectedText">選択されたテキスト。</param>
    /// <returns>検証成功時は見つかった位置情報、失敗時はエラーメッセージ。</returns>
    public static Result<ItemSplitTextMatch> ValidateAndLocate(string content, string selectedText)
    {
        (int index, string textToReplace) = FindSelectedText(content ?? string.Empty, selectedText);
        if (index < 0)
        {
            return Result.Fail<ItemSplitTextMatch>(ErrorMessages.SplitSelectedTextNotInContent);
        }

        if (IntersectsExistingLink(content!, index, textToReplace.Length))
        {
            return Result.Fail<ItemSplitTextMatch>(ErrorMessages.SplitIntersectsExistingLink);
        }

        if (content!.Length - textToReplace.Length + MaxReplacementLength > MaxContentLength)
        {
            return Result.Fail<ItemSplitTextMatch>(ErrorMessages.SplitResultExceedsMaxLength);
        }

        return Result.Ok(new ItemSplitTextMatch(index, textToReplace));
    }

    /// <summary>
    ///     分割リクエスト承認時のテキスト選択位置を検索し、元アイテム本文変更検知・リンク交差・文字数制限を検証する。
    /// </summary>
    /// <param name="content">アイテム本文。</param>
    /// <param name="selectedText">リクエスト作成時に選択されたテキスト。</param>
    /// <returns>検証成功時は見つかった位置情報、失敗時はエラーメッセージ。</returns>
    public static Result<ItemSplitTextMatch> ValidateAndLocateForApprove(string content, string selectedText)
    {
        (int index, string textToReplace) = FindSelectedText(content ?? string.Empty, selectedText);
        if (index < 0)
        {
            return Result.Fail<ItemSplitTextMatch>(ErrorMessages.SplitOriginalItemContentChanged);
        }

        if (IntersectsExistingLink(content!, index, textToReplace.Length))
        {
            return Result.Fail<ItemSplitTextMatch>(ErrorMessages.SplitIntersectsExistingLink);
        }

        if (content!.Length - textToReplace.Length + MaxReplacementLength > MaxContentLength)
        {
            return Result.Fail<ItemSplitTextMatch>(ErrorMessages.SplitResultExceedsMaxLength);
        }

        return Result.Ok(new ItemSplitTextMatch(index, textToReplace));
    }
}