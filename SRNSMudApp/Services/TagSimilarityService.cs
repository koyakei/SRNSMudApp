namespace SRNSMudApp.Services;

using SRNSMudApp.Data;
using SRNSMudApp.Models;

/// <summary>
///     正規化レーベンシュタイン距離および部分一致に基づき類似タグを検出するサービス。
/// </summary>
public class TagSimilarityService : ITagSimilarityService
{
    public IReadOnlyList<SimilarTagCandidate> FindSimilarTags(
        IEnumerable<Tag> candidateTags,
        string? query,
        float minSimilarity = 0.50f,
        int maxResults = 5)
    {
        if (candidateTags is null || string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        string trimmedQuery = query.Trim();
        if (trimmedQuery.Length == 0)
        {
            return [];
        }

        // ルートタグ除外、類似度計算、部分一致スコア底上げを行い、類似度降順・タグ名昇順で取得
        return [.. candidateTags
            .Where(tag => !string.IsNullOrEmpty(tag.Name) && tag.Name != Tag.RootTagName)
            .Select(tag =>
            {
                float similarity = StringSimilarity.Calculate(trimmedQuery.AsSpan(), tag.Name.AsSpan());

                // 部分一致（包含関係）を考慮してスコアを底上げ
                bool isSubstring = tag.Name.Contains(trimmedQuery, StringComparison.OrdinalIgnoreCase) ||
                                   trimmedQuery.Contains(tag.Name, StringComparison.OrdinalIgnoreCase);

                if (isSubstring)
                {
                    float lengthRatio = (float)Math.Min(trimmedQuery.Length, tag.Name.Length) /
                                        Math.Max(trimmedQuery.Length, tag.Name.Length);
                    similarity = Math.Max(similarity, 0.60f + (lengthRatio * 0.35f));
                }

                return new SimilarTagCandidate(tag, similarity);
            })
            .Where(c => c.Similarity >= minSimilarity)
            .OrderByDescending(c => c.Similarity)
            .ThenBy(c => c.Tag.Name.Length)
            .ThenBy(c => c.Tag.Name, StringComparer.OrdinalIgnoreCase)
            .Take(maxResults)];
    }
}