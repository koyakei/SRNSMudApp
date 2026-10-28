namespace SRNSMudApp.Models;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

/// <summary>
///     テキストを URL / 通常文字列に分割したセグメント。
/// </summary>
public record ContentSegment(string Text, bool IsUrl);

/// <summary>
///     コンテンツからの URL 抽出およびセグメント分割を行うドメインユーティリティ。
/// </summary>
public static partial class ContentParser
{
    [GeneratedRegex(@"https?:\/\/(?:localhost(?:\:\d+)?|(?:www\.)?[-a-zA-Z0-9@:%._\+~#=]{1,256}\.[a-zA-Z0-9()]{1,6})\b(?:[-a-zA-Z0-9()@:%_\+.~#?&//=]*)")]
    public static partial Regex UrlRegex();

    [GeneratedRegex(@"\/(?:ItemDetail|TagDetail)\/\d+|\/User\/UserDetail\/[a-zA-Z0-9\-_]+", RegexOptions.IgnoreCase)]
    public static partial Regex InternalLinkRegex();

    /// <summary>
    ///     テキストから URL を抽出して返す。重複は除去される。
    /// </summary>
    public static IReadOnlyList<string> ExtractUrls(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        // 外部URLと内部リンクを抽出し、出現順を維持したまま重複を除去して返す
        return [.. UrlRegex().Matches(text)
            .Concat(InternalLinkRegex().Matches(text))
            .Select(m => m.Value)
            .Distinct()];
    }

    /// <summary>
    ///     テキストを URL と通常の文字列のセグメントに分割して返す。
    /// </summary>
    public static IReadOnlyList<ContentSegment> GetContentSegments(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        List<ContentSegment> results = [];
        // 冗長なCast<Match>()を排除し、マッチ結果を結合して出現インデックス順に整列
        List<Match> matches = [.. UrlRegex().Matches(text)
            .Concat(InternalLinkRegex().Matches(text))
            .OrderBy(m => m.Index)];

        List<Match> validMatches = [];
        int currentEnd = 0;
        foreach (Match m in matches)
        {
            if (m.Index >= currentEnd)
            {
                validMatches.Add(m);
                currentEnd = m.Index + m.Length;
            }
        }

        int lastIndex = 0;
        foreach (Match match in validMatches)
        {
            if (match.Index > lastIndex)
            {
                results.Add(new ContentSegment(text.Substring(lastIndex, match.Index - lastIndex), false));
            }

            results.Add(new ContentSegment(match.Value, true));
            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < text.Length)
        {
            results.Add(new ContentSegment(text.Substring(lastIndex), false));
        }

        return results;
    }
}