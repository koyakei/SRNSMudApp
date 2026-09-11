using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;

namespace SRNSMudApp.Services;

/// <summary>
///     アイテムの引用（引用リツイート）および引用されたアイテム一覧の取得を担当するサービス実装クラス。
/// </summary>
/// <param name="dbFactory">DbContext ファクトリ。</param>
public class ItemQuoteService(IDbContextFactory<ApplicationDbContext> dbFactory) : IItemQuoteService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));

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

        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync(cancellationToken);

        // 1. まず対象アイテムの QuotedItemId を確認
        int? quotedItemId = await context.Items
            .AsNoTracking()
            .Where(i => i.Id == itemId)
            .Select(i => i.QuotedItemId)
            .FirstOrDefaultAsync(cancellationToken);

        if (quotedItemId is { } parentId && parentId > 0)
        {
            return await GetQuotedItemAsync(parentId, cancellationToken);
        }

        // 2. フォールバック: ItemSplitRequest で CreatedItemId が一致する元の OriginalItemId を探す
        int? originalItemId = await context.ItemSplitRequests
            .AsNoTracking()
            .Where(r => r.CreatedItemId == itemId)
            .Select(r => (int?)r.OriginalItemId)
            .FirstOrDefaultAsync(cancellationToken);

        if (originalItemId is { } splitOriginId && splitOriginId > 0)
        {
            return await GetQuotedItemAsync(splitOriginId, cancellationToken);
        }

        // 3. フォールバック: 本文に /ItemDetail/{itemId} リンクを含んでいる元アイテムを探す
        var linkUrl = $"/ItemDetail/{itemId}";
        int? linkingItemId = await context.Items
            .AsNoTracking()
            .Where(i => i.Id != itemId && i.Content != null && i.Content.Contains(linkUrl))
            .Select(i => (int?)i.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (linkingItemId is { } linkOriginId && linkOriginId > 0)
        {
            return await GetQuotedItemAsync(linkOriginId, cancellationToken);
        }

        return null;
    }
}