#pragma warning disable CA1848

using System.Diagnostics.CodeAnalysis;
using System.Numerics.Tensors;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using SRNSMudApp.Data;

using Tag = SRNSMudApp.Data.Tag;

namespace SRNSMudApp.Services;

/// <summary>
///     タグの読み取り処理を担当する Query サービス。
///     書き込み処理と依存関係を分離し、検索系コンポーネントをテストしやすくする。
/// </summary>
public class TagSearchQueryService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    ITagEmbeddingService tagEmbeddingService,
    ILogger<TagSearchQueryService>? logger = null) : ITagSearchQueryService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
    private readonly ITagEmbeddingService _tagEmbeddingService =
        tagEmbeddingService ?? throw new ArgumentNullException(nameof(tagEmbeddingService));
    private readonly ILogger<TagSearchQueryService> _logger =
        logger ?? NullLogger<TagSearchQueryService>.Instance;

    public async Task<List<Tag>> GetAllTagsAsync()
    {
        await using ApplicationDbContext dbContext = await _dbFactory.CreateDbContextAsync();
        return await dbContext.Tags.AsNoTracking().ToListAsync();
    }

    public async Task<List<Tag>> SearchTagsAsync(string searchText)
    {
        float[] queryVector = (await _tagEmbeddingService.GenerateEmbeddingAsync(searchText)).ToArray();

        await using ApplicationDbContext dbContext = await _dbFactory.CreateDbContextAsync();

        List<Tag> textMatches = await dbContext.Tags
            .Where(x => x.Name.Contains(searchText) || x.Content.Contains(searchText))
            .AsNoTracking()
            .ToListAsync();

        List<Tag> vectorTags = await dbContext.Tags.Where(x => x.Embedding != null).AsNoTracking().ToListAsync();

        List<Tag> vectorMatches = vectorTags
            .Where(x => x.Embedding.Length == queryVector.Length)
            .OrderByDescending(x => TensorPrimitives.CosineSimilarity(x.Embedding, queryVector))
            .Take(50)
            .ToList();

        return
        [
            .. textMatches.Concat(vectorMatches)
                .DistinctBy(x => x.Id)
                .Take(50)
        ];
    }

    public async Task<Tag?> FindTagByNameAsync(string tagName)
    {
        await using ApplicationDbContext dbContext = await _dbFactory.CreateDbContextAsync();
        return await dbContext.Tags.FirstOrDefaultAsync(t => t.Name == tagName);
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "検索失敗時にテキスト検索へフォールバックするため広く捕捉する")]
    public async Task<List<Tag>> SearchTagsWithFallbackAsync(string? value, CancellationToken token = default)
    {
        if (token.IsCancellationRequested)
        {
            return [];
        }

        await using ApplicationDbContext dbContext = await _dbFactory.CreateDbContextAsync(token);
        IQueryable<Tag> query = dbContext.Tags.AsQueryable();

        if (string.IsNullOrEmpty(value))
        {
            return await query.OrderBy(t => t.Name).AsNoTracking().Take(50).ToListAsync(token);
        }

        try
        {
            float[] queryVector = (await _tagEmbeddingService.GenerateEmbeddingAsync(value)).ToArray();

            List<Tag> textMatches = await query
                .Where(x => x.Name.Contains(value) || x.Content.Contains(value))
                .OrderBy(x => x.Name)
                .AsNoTracking()
                .ToListAsync(token);

            List<Tag> vectorTags = await query.Where(x => x.Embedding != null).AsNoTracking().ToListAsync(token);

            List<Tag> vectorMatches = vectorTags
                .Where(x => x.Embedding.Length == queryVector.Length)
                .OrderByDescending(x => TensorPrimitives.CosineSimilarity(x.Embedding, queryVector))
                .Take(50)
                .ToList();

            return
            [
                .. textMatches.Concat(vectorMatches)
                    .DistinctBy(x => x.Id)
                    .Take(50)
            ];
        }
        catch (OperationCanceledException)
        {
            return [];
        }
        catch (Exception ex)
        {
            if (token.IsCancellationRequested)
            {
                return [];
            }

            _logger.LogWarning(ex, "Vector search failed: {Message}", ex.Message);
            query = query.Where(x => x.Name.Contains(value) || x.Content.Contains(value));
            return await query.OrderBy(x => x.Name).AsNoTracking().Take(50).ToListAsync(token);
        }
    }

    public async Task<List<Tag>> GetTagsWithDetailsAsync()
    {
        await using ApplicationDbContext dbContext = await _dbFactory.CreateDbContextAsync();
        return await dbContext.Tags
            .Include(t => t.Owner)
            .Include(t => t.TargetTagRelations)
            .ThenInclude(tr => tr.Tag)
            .ThenInclude(t => t.Owner)
            .AsNoTracking()
            .ToListAsync();
    }
}