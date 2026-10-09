namespace SRNSMudApp.Models;

using SRNSMudApp.Data;

/// <summary>
///     類似タグ候補とその類似度スコアを表すレコード。
/// </summary>
/// <param name="Tag">候補となる既存タグエンティティ。</param>
/// <param name="Similarity">類似度スコア（0.0f 〜 1.0f）。</param>
public sealed record SimilarTagCandidate(Tag Tag, float Similarity);