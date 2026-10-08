#pragma warning disable CA1848

using System.Diagnostics.CodeAnalysis;
using System.Numerics.Tensors;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using SRNSMudApp.Data;
using SRNSMudApp.Models;

namespace SRNSMudApp.Services;

/// <summary>
///     コンテンツテキストのベクトル埋め込みと既存タグの埋め込みベクトルのコサイン類似度を比較し、
///     関連付けられそうなタグを推薦するサービス実装。
/// </summary>
public class TagSuggestionService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    ITagEmbeddingService tagEmbeddingService,
    ILogger<TagSuggestionService>? logger = null) : ITagSuggestionService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
    private readonly ITagEmbeddingService _tagEmbeddingService =
        tagEmbeddingService ?? throw new ArgumentNullException(nameof(tagEmbeddingService));
    private readonly ILogger<TagSuggestionService> _logger =
        logger ?? NullLogger<TagSuggestionService>.Instance;

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "類似度計算エラー時は安全に空リストまたは例外ログを出力してフォールバックする")]
    public async Task<IReadOnlyList<SuggestedTag>> SuggestTagsAsync(
        string content,
        float minScore = SuggestedTag.DefaultCandidateThreshold,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(content) || cancellationToken.IsCancellationRequested)
        {
            return [];
        }

        try
        {
            float[] queryVector = (await _tagEmbeddingService.GenerateEmbeddingAsync(content)).ToArray();

            if (queryVector.Length == 0 || cancellationToken.IsCancellationRequested)
            {
                return [];
            }

            await using ApplicationDbContext dbContext = await _dbFactory.CreateDbContextAsync(cancellationToken);

            // 埋め込みベクトルが存在するタグを取得（ルートタグ、投票タグ、リアクションタグはサジェスト候補から除外）
            List<Data.Tag> candidates = await dbContext.Tags
                .AsNoTracking()
                .WhereVisibleToUser()
                .Where(t => t.Embedding != null && t.Name != Data.Tag.RootTagName)
                .ToListAsync(cancellationToken);

            List<SuggestedTag> suggestions = [];

            foreach (Data.Tag tag in candidates)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return [];
                }

                // 投票タグ・リアクションタグは自動サジェスト対象外とする
                if (Data.Tag.VoteTagNames.Contains(tag.Name) || Data.Tag.ReactionTagNames.Contains(tag.Name))
                {
                    continue;
                }

                if (tag.Embedding.Length != queryVector.Length)
                {
                    continue;
                }

                float score = TensorPrimitives.CosineSimilarity(tag.Embedding, queryVector);
                if (score >= minScore)
                {
                    suggestions.Add(new SuggestedTag(tag.Id, tag.Name, score));
                }
            }

            return suggestions
                .OrderByDescending(s => s.Score)
                .Take(20)
                .ToList();
        }
        catch (OperationCanceledException)
        {
            return [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "タグ推薦ベクトルの類似度計算に失敗しました: {Message}", ex.Message);
            return [];
        }
    }
}