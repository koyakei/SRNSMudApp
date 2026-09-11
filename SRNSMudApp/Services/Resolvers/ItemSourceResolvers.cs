using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;

namespace SRNSMudApp.Services.Resolvers;

/// <summary>
///     元アイテム解決戦略の共通データアクセス処理を提供する基底クラス。
/// </summary>
/// <param name="dbFactory">DbContext ファクトリ。</param>
public abstract class ItemSourceResolverBase(IDbContextFactory<ApplicationDbContext> dbFactory)
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));

    /// <summary>
    ///     ID から表示用の元アイテムを関連エンティティ込みで取得する。
    /// </summary>
    /// <param name="context">利用中の DbContext。</param>
    /// <param name="itemId">取得するアイテム ID。</param>
    /// <param name="cancellationToken">キャンセレーショントークン。</param>
    /// <returns>取得されたアイテム。存在しない場合は <see langword="null" />。</returns>
    protected static Task<Item?> LoadSourceItemAsync(
        ApplicationDbContext context,
        int itemId,
        CancellationToken cancellationToken) =>
        context.Items
            .AsNoTracking()
            .Include(i => i.Owner)
            .Include(i => i.TagRelations)
                .ThenInclude(tr => tr.Tag)
            .FirstOrDefaultAsync(i => i.Id == itemId, cancellationToken);

    /// <summary>
    ///     resolver 用の DbContext を生成する。
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン。</param>
    /// <returns>生成された DbContext。</returns>
    protected Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken) =>
        _dbFactory.CreateDbContextAsync(cancellationToken);
}

/// <summary>
///     QuotedItemId を使用して元アイテムを解決する最優先戦略。
/// </summary>
public sealed class QuotedItemIdSourceResolver(IDbContextFactory<ApplicationDbContext> dbFactory)
    : ItemSourceResolverBase(dbFactory), IItemSourceResolver
{
    /// <inheritdoc />
    public async Task<Item?> ResolveSourceAsync(int itemId, CancellationToken cancellationToken = default)
    {
        await using ApplicationDbContext context = await CreateDbContextAsync(cancellationToken);

        int? sourceItemId = await context.Items
            .AsNoTracking()
            .Where(i => i.Id == itemId)
            .Select(i => i.QuotedItemId)
            .FirstOrDefaultAsync(cancellationToken);

        return sourceItemId is { } id && id > 0
            ? await LoadSourceItemAsync(context, id, cancellationToken)
            : null;
    }
}

/// <summary>
///     実行済み ItemSplitRequest を使用して元アイテムを解決する戦略。
/// </summary>
public sealed class ItemSplitRequestSourceResolver(IDbContextFactory<ApplicationDbContext> dbFactory)
    : ItemSourceResolverBase(dbFactory), IItemSourceResolver
{
    /// <inheritdoc />
    public async Task<Item?> ResolveSourceAsync(int itemId, CancellationToken cancellationToken = default)
    {
        await using ApplicationDbContext context = await CreateDbContextAsync(cancellationToken);

        int? sourceItemId = await context.ItemSplitRequests
            .AsNoTracking()
            .Where(r => r.CreatedItemId == itemId)
            .Select(r => (int?)r.OriginalItemId)
            .FirstOrDefaultAsync(cancellationToken);

        return sourceItemId is { } id && id > 0
            ? await LoadSourceItemAsync(context, id, cancellationToken)
            : null;
    }
}

/// <summary>
///     元アイテム本文内の ItemDetail リンクを使用して元アイテムを解決する最終フォールバック戦略。
/// </summary>
public sealed class ItemLinkSourceResolver(IDbContextFactory<ApplicationDbContext> dbFactory)
    : ItemSourceResolverBase(dbFactory), IItemSourceResolver
{
    /// <inheritdoc />
    public async Task<Item?> ResolveSourceAsync(int itemId, CancellationToken cancellationToken = default)
    {
        await using ApplicationDbContext context = await CreateDbContextAsync(cancellationToken);

        string linkUrl = $"/ItemDetail/{itemId}";
        int? sourceItemId = await context.Items
            .AsNoTracking()
            .Where(i => i.Id != itemId && i.Content.Contains(linkUrl))
            .Select(i => (int?)i.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return sourceItemId is { } id && id > 0
            ? await LoadSourceItemAsync(context, id, cancellationToken)
            : null;
    }
}