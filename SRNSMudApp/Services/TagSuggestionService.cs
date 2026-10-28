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

            if (cancellationToken.IsCancellationRequested)
            {
                return [];
            }

            // 投票タグ・リアクションタグを除外し、ベクトル類似度を計算して上位20件をサジェスト
            return [.. candidates
                .Where(tag => !Data.Tag.VoteTagNames.Contains(tag.Name)
                              && !Data.Tag.ReactionTagNames.Contains(tag.Name)
                              && tag.Embedding?.Length == queryVector.Length)
                .Select(tag => new SuggestedTag(tag.Id, tag.Name, TensorPrimitives.CosineSimilarity(tag.Embedding!, queryVector)))
                .Where(s => s.Score >= minScore)
                .OrderByDescending(s => s.Score)
                .Take(20)];
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