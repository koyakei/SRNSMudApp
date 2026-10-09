namespace SRNSMudApp.Services;

using SRNSMudApp.Data;
using SRNSMudApp.Models;

/// <summary>
///     入力文字列と既存タグコレクションを比較し、類似する候補タグを検出するサービスインターフェース。
/// </summary>
public interface ITagSimilarityService
{
    /// <summary>
    ///     入力されたタグ名と類似する既存タグの候補リストを類似度降順で取得する。
    /// </summary>
    /// <param name="candidateTags">検索対象となる既存タグのコレクション。</param>
    /// <param name="query">入力されたタグ名。</param>
    /// <param name="minSimilarity">候補として判定する最小類似度閾値（デフォルト: 0.50f）。</param>
    /// <param name="maxResults">取得する最大候補件数（デフォルト: 5件）。</param>
    /// <returns>類似度降順に整列された候補リスト。</returns>
    IReadOnlyList<SimilarTagCandidate> FindSimilarTags(
        IEnumerable<Tag> candidateTags,
        string? query,
        float minSimilarity = 0.50f,
        int maxResults = 5);
}