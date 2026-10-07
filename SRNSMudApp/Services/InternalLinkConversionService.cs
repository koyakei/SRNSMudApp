#pragma warning disable CA1848

#region

using System.Diagnostics.CodeAnalysis;
using System.Text;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using SRNSMudApp.Data;
using SRNSMudApp.Models;

#endregion

namespace SRNSMudApp.Services;

/// <summary>
///     テキスト中のタグ名をレーベンシュタイン類似度で検出し、
///     /TagDetail/{id} 形式の内部リンクへの変換候補を生成するサービス実装。
///     既存の内部リンクや外部URLを含む部分はスキップし、
///     重複・オーバーラップする候補は最もスコアの高いものを優先する。
/// </summary>
public class InternalLinkConversionService(
    ITagSearchQueryService tagSearchQueryService,
    ILogger<InternalLinkConversionService>? logger = null) : IInternalLinkConversionService
{
    private readonly ITagSearchQueryService _tagSearchQueryService =
        tagSearchQueryService ?? throw new ArgumentNullException(nameof(tagSearchQueryService));
    private readonly ILogger<InternalLinkConversionService> _logger =
        logger ?? NullLogger<InternalLinkConversionService>.Instance;

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "候補検出の失敗はアイテム保存をブロックしないため、空結果でフォールバックする")]
    public async Task<InternalLinkConversionResult> DetectLinkCandidatesAsync(
        string content,
        float autoReplaceThreshold = LinkConversionCandidate.DefaultAutoReplaceThreshold,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return new InternalLinkConversionResult([], []);
        }

        try
        {
            // Provider / Query サービスからリンク候補対象タグ一覧を取得（キャッシュ対応）
            List<Tag> candidateTags = await _tagSearchQueryService
                .GetCandidateTagsForLinkConversionAsync(cancellationToken)
                .ConfigureAwait(false);

            if (candidateTags.Count == 0)
            {
                return new InternalLinkConversionResult([], []);
            }

            // 既存のURLや内部リンク部分を特定し、検索対象外にする
            IReadOnlyList<ContentSegment> segments = ContentParser.GetContentSegments(content);
            List<(int Start, int End)> urlRanges = BuildUrlRanges(content, segments);

            // 各タグ名でテキスト内の一致候補を検出
            List<LinkConversionCandidate> allCandidates = [];

            foreach (Tag tag in candidateTags)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return new InternalLinkConversionResult([], []);
                }

                DetectMatchesForTag(content, tag, autoReplaceThreshold, urlRanges, allCandidates);
            }

            // オーバーラップ除去（スコア高い順に優先）
            List<LinkConversionCandidate> resolved = ResolveOverlaps(allCandidates);

            // 自動置換と手動候補に分類
            List<LinkConversionCandidate> auto = [];
            List<LinkConversionCandidate> manual = [];

            foreach (LinkConversionCandidate candidate in resolved)
            {
                if (candidate.IsAutoReplace)
                {
                    auto.Add(candidate);
                }
                else
                {
                    manual.Add(candidate);
                }
            }

            return new InternalLinkConversionResult(auto, manual);
        }
        catch (OperationCanceledException)
        {
            return new InternalLinkConversionResult([], []);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "内部リンク候補検出に失敗しました: {Message}", ex.Message);
            return new InternalLinkConversionResult([], []);
        }
    }

    public string ApplyReplacements(string content, IReadOnlyList<LinkConversionCandidate> candidates)
    {
        if (candidates.Count == 0)
        {
            return content;
        }

        // StartIndex 降順で適用し、位置ずれを防ぐ
        List<LinkConversionCandidate> sorted = [.. candidates.OrderByDescending(c => c.StartIndex)];

        var sb = new StringBuilder(content);
        foreach (LinkConversionCandidate candidate in sorted)
        {
            string replacement = $"/TagDetail/{candidate.TagId}";
            sb.Remove(candidate.StartIndex, candidate.Length);
            sb.Insert(candidate.StartIndex, replacement);
        }

        return sb.ToString();
    }

    /// <summary>
    ///     テキスト中の URL・内部リンク位置を集める。
    ///     これらの範囲内のテキストはタグ名マッチングの対象外とする。
    /// </summary>
    private static List<(int Start, int End)> BuildUrlRanges(
        string content,
        IReadOnlyList<ContentSegment> segments)
    {
        List<(int Start, int End)> ranges = [];
        int position = 0;

        foreach (ContentSegment segment in segments)
        {
            int segStart = content.IndexOf(segment.Text, position, StringComparison.Ordinal);
            if (segStart < 0)
            {
                continue;
            }

            if (segment.IsUrl)
            {
                ranges.Add((segStart, segStart + segment.Text.Length));
            }

            position = segStart + segment.Text.Length;
        }

        return ranges;
    }

    /// <summary>
    ///     指定位置が URL 範囲内にあるかを判定する。
    /// </summary>
    private static bool IsWithinUrlRange(int start, int end, List<(int Start, int End)> urlRanges) =>
        urlRanges.Exists(r => start < r.End && end > r.Start);

    /// <summary>
    ///     1つのタグ名に対してテキスト中の一致箇所を検出する。
    ///     完全部分文字列一致 → 類似度 1.0、正規化一致、
    ///     さらにスライディングウィンドウでファジーマッチングを行う。
    /// </summary>
    private static void DetectMatchesForTag(
        string content,
        Tag tag,
        float autoReplaceThreshold,
        List<(int Start, int End)> urlRanges,
        List<LinkConversionCandidate> results)
    {
        string tagName = tag.Name;

        // 完全部分文字列一致を試行（大文字小文字区別なし）
        int searchStart = 0;
        while (searchStart < content.Length)
        {
            int idx = content.IndexOf(tagName, searchStart, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                break;
            }

            if (!IsWithinUrlRange(idx, idx + tagName.Length, urlRanges))
            {
                float similarity = content.AsSpan(idx, tagName.Length)
                    .SequenceEqual(tagName.AsSpan())
                    ? 1.0f  // 完全一致（大文字小文字も一致）
                    : 0.95f; // 大文字小文字のみ異なる

                results.Add(new LinkConversionCandidate(
                    content.Substring(idx, tagName.Length),
                    tag.Id,
                    tagName,
                    similarity,
                    idx,
                    tagName.Length,
                    similarity >= autoReplaceThreshold));
            }

            searchStart = idx + tagName.Length;
        }

        // スライディングウィンドウによるファジーマッチング
        // （完全一致が見つかった範囲はスキップ）
        if (tagName.Length >= 3)
        {
            DetectFuzzyMatches(content, tag, autoReplaceThreshold, urlRanges, results);
        }
    }

    /// <summary>
    ///     タグ名の長さ±1文字のウィンドウでスライドし、
    ///     レーベンシュタイン類似度が閾値を超える箇所を候補として追加する。
    /// </summary>
    private static void DetectFuzzyMatches(
        string content,
        Tag tag,
        float autoReplaceThreshold,
        List<(int Start, int End)> urlRanges,
        List<LinkConversionCandidate> results)
    {
        string tagName = tag.Name;
        int minWindow = Math.Max(tagName.Length - 1, 2);
        int maxWindow = tagName.Length + 1;

        for (int windowLen = minWindow; windowLen <= maxWindow; windowLen++)
        {
            for (int i = 0; i <= content.Length - windowLen; i++)
            {
                if (IsWithinUrlRange(i, i + windowLen, urlRanges))
                {
                    continue;
                }

                // 既に完全一致として検出済みの範囲をスキップ
                if (results.Exists(r =>
                        r.TagId == tag.Id && r.StartIndex == i && r.Length == windowLen))
                {
                    continue;
                }

                ReadOnlySpan<char> window = content.AsSpan(i, windowLen);
                float similarity = StringSimilarity.Calculate(window, tagName.AsSpan());

                if (similarity >= StringSimilarity.MinCandidateThreshold && similarity < 0.95f)
                {
                    results.Add(new LinkConversionCandidate(
                        content.Substring(i, windowLen),
                        tag.Id,
                        tagName,
                        similarity,
                        i,
                        windowLen,
                        similarity >= autoReplaceThreshold));
                }
            }
        }
    }

    /// <summary>
    ///     オーバーラップする候補を解決する。スコアが高い候補を優先し、
    ///     範囲が重なる低スコア候補を除去する。
    /// </summary>
    private static List<LinkConversionCandidate> ResolveOverlaps(List<LinkConversionCandidate> candidates)
    {
        // スコア降順 → 開始位置昇順でソート
        List<LinkConversionCandidate> sorted = [.. candidates
            .OrderByDescending(c => c.Similarity)
            .ThenBy(c => c.StartIndex)];

        List<LinkConversionCandidate> resolved = [];

        foreach (LinkConversionCandidate candidate in sorted)
        {
            bool overlaps = resolved.Exists(r =>
                candidate.StartIndex < r.StartIndex + r.Length &&
                candidate.StartIndex + candidate.Length > r.StartIndex);

            if (!overlaps)
            {
                resolved.Add(candidate);
            }
        }

        return [.. resolved.OrderBy(c => c.StartIndex)];
    }
}