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

        List<string> results = [];
        MatchCollection matches = UrlRegex().Matches(text);
        foreach (Match match in matches)
        {
            if (!results.Contains(match.Value))
            {
                results.Add(match.Value);
            }
        }

        MatchCollection internalMatches = InternalLinkRegex().Matches(text);
        foreach (Match match in internalMatches)
        {
            if (!results.Contains(match.Value))
            {
                results.Add(match.Value);
            }
        }

        return results;
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
        List<Match> matches = [];
        matches.AddRange(UrlRegex().Matches(text).Cast<Match>());
        matches.AddRange(InternalLinkRegex().Matches(text).Cast<Match>());

        matches = matches.OrderBy(m => m.Index).ToList();

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