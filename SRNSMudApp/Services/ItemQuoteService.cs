using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services.Resolvers;

namespace SRNSMudApp.Services;

/// <summary>
///     アイテムの引用（引用リツイート）および引用されたアイテム一覧の取得を担当するサービス実装クラス。
/// </summary>
/// <param name="dbFactory">DbContext ファクトリ。</param>
/// <param name="sourceResolvers">優先順位順に実行する元アイテム解決戦略。</param>
public class ItemQuoteService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    IEnumerable<IItemSourceResolver> sourceResolvers) : IItemQuoteService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
    private readonly IReadOnlyList<IItemSourceResolver> _sourceResolvers =
        sourceResolvers?.ToArray() ?? throw new ArgumentNullException(nameof(sourceResolvers));

    /// <inheritdoc />
    public async Task<Item?> CreateQuoteItemAsync(
        int quotedItemId,
        string content,
        string userId,
        IReadOnlyCollection<int>? initialTagIds = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException("コンテンツは必須です。", nameof(content));
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("ユーザーIDは必須です。", nameof(userId));
        }

        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync(cancellationToken);

        // 引用元のアイテムが存在することを確認する
        var targetExists = await context.Items.AnyAsync(i => i.Id == quotedItemId, cancellationToken);
        if (!targetExists)
        {
            return null;
        }

        var quoteItem = new Item
        {
            Content = content,
            OwnerId = userId,
            QuotedItemId = quotedItemId,
            CreatedDate = DateTime.UtcNow,
            UpdatedDate = DateTime.UtcNow,
            ItemKindJson = JsonSerializer.Serialize(new QuoteItem(quotedItemId))
        };

        _ = context.Items.Add(quoteItem);

        if (initialTagIds is { Count: > 0 })
        {
            var relations = initialTagIds
                .Select(tagId => new TagRelation
                {
                    TagId = tagId,
                    Weight = 1,
                    OwnerId = userId,
                    CreatedDate = DateTime.UtcNow,
                    UpdatedDate = DateTime.UtcNow
                })
                .ToList();
            quoteItem.TagRelations = relations;
        }

        _ = await context.SaveChangesAsync(cancellationToken);

        return await context.Items
            .Include(i => i.Owner)
            .Include(i => i.TagRelations)
                .ThenInclude(tr => tr.Tag)
            .Include(i => i.QuotedItem)
                .ThenInclude(q => q!.Owner)
            .FirstOrDefaultAsync(i => i.Id == quoteItem.Id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Item>> GetQuotedByItemsAsync(
        int quotedItemId,
        CancellationToken cancellationToken = default)
    {
        if (quotedItemId <= 0)
        {
            return [];
        }

        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync(cancellationToken);

        return await context.Items
            .AsNoTracking()
            .Include(i => i.Owner)
            .Include(i => i.TagRelations)
                .ThenInclude(tr => tr.Tag)
            .Include(i => i.QuotedItem)
                .ThenInclude(q => q!.Owner)
            .Where(i => i.QuotedItemId == quotedItemId)
            .OrderByDescending(i => i.CreatedDate)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> GetQuoteCountAsync(
        int quotedItemId,
        CancellationToken cancellationToken = default)
    {
        if (quotedItemId <= 0)
        {
            return 0;
        }

        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync(cancellationToken);
        return await context.Items.CountAsync(i => i.QuotedItemId == quotedItemId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Item?> GetQuotedItemAsync(
        int quotedItemId,
        CancellationToken cancellationToken = default)
    {
        if (quotedItemId <= 0)
        {
            return null;
        }

        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync(cancellationToken);

        return await context.Items
            .AsNoTracking()
            .Include(i => i.Owner)
            .Include(i => i.TagRelations)
                .ThenInclude(tr => tr.Tag)
            .FirstOrDefaultAsync(i => i.Id == quotedItemId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Item?> GetSourceItemAsync(
        int itemId,
        CancellationToken cancellationToken = default)
    {
        if (itemId <= 0)
        {
            return null;
        }

        foreach (IItemSourceResolver resolver in _sourceResolvers)
        {
            Item? sourceItem = await resolver.ResolveSourceAsync(itemId, cancellationToken);
            if (sourceItem is not null)
            {
                return sourceItem;
            }
        }

        return null;
    }
}