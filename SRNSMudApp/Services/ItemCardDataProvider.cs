using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;

namespace SRNSMudApp.Services;

/// <summary>
///     ItemCard コンポーネント用のデータアクセスを分離するインターフェース。
///     コンポーネントから DbContext への直接依存を断ち、単体テストでモック可能にする。
/// </summary>
public interface IItemCardDataProvider
{
    Task DeleteItemAsync(int itemId);

    /// <summary>オーナー込みでタグを取得する。存在しない場合は null。</summary>
    Task<Tag?> GetTagWithOwnerAsync(int tagId);

    /// <summary>
    ///     RightAsset を消費して無償でタグ付けし、作成されたリレーション (Tag 込み) を返す。
    /// </summary>
    Task<TagRelation?> AddFreeTagRelationAsync(int itemId, int tagId, string userId);

    /// <summary>アイテムを初期タグ付きで作成する。</summary>
    Task CreateItemAsync(Item item, IReadOnlyCollection<int>? initialTagIds);

    /// <summary>アイテム本文を更新する。対象が存在しない場合は false。</summary>
    Task<bool> UpdateItemContentAsync(int itemId, string content);

    /// <summary>
    ///     指定ユーザーがタグを直接（コントラクト提案を経ずに）付与可能かどうかを判定する。
    ///     - システムタグ・リアクションタグ
    ///     - タグのオーナー本人
    ///     - タグの自動承認（全体またはユーザーが所属するグループ）が有効な場合
    /// </summary>
    Task<bool> CanUserAttachTagDirectlyAsync(int tagId, string userId);
}

public class ItemCardDataProvider(IDbContextFactory<ApplicationDbContext> dbFactory) : IItemCardDataProvider
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));


    public async Task DeleteItemAsync(int itemId)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();
        Item? item = await context.Items.FindAsync(itemId);
        if (item is not null)
        {
            _ = context.Items.Remove(item);
            _ = await context.SaveChangesAsync();
        }
    }

    public async Task<Tag?> GetTagWithOwnerAsync(int tagId)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();
        return await context.Tags.Include(t => t.Owner).FirstOrDefaultAsync(t => t.Id == tagId);
    }

    public async Task<TagRelation?> AddFreeTagRelationAsync(int itemId, int tagId, string userId)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();

        // DbContext の CreateFreeTagRelationAsync を使用:
        // - RightAsset の自動発行・消費
        // - TagRelation の作成
        // - TagWeightLedger への記帳 (ConsumedRightAssetId を含む)
        // - TimelineEvent の追加
        // をトランザクション内で一括処理する
        await context.CreateFreeTagRelationAsync(itemId, tagId, userId);

        return await context.Set<TagRelation>()
            .Include(tr => tr.Tag)
            .OrderByDescending(tr => tr.Id)
            .FirstOrDefaultAsync(tr => tr.ItemId == itemId && tr.TagId == tagId && tr.OwnerId == userId);
    }

    public async Task CreateItemAsync(Item item, IReadOnlyCollection<int>? initialTagIds)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();
        _ = context.Items.Add(item);

        switch (initialTagIds is { Count: > 0 })
        {
            case true:
                {
                    var tagRelations = initialTagIds
                        .Select(tagId => new TagRelation
                        {
                            TagId = tagId,
                            Weight = 1,
                            OwnerId = item.OwnerId
                        })
                        .ToList();

                    item.TagRelations = tagRelations;
                    break;
                }
            default:
                break;
        }

        _ = await context.SaveChangesAsync();
    }
    public async Task<bool> UpdateItemContentAsync(int itemId, string content)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();
        Item? itemToUpdate = await context.Items.FindAsync(itemId);
        if (itemToUpdate is null)
        {
            return false;
        }

        itemToUpdate.Content = content;
        _ = await context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> CanUserAttachTagDirectlyAsync(int tagId, string userId)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();
        Tag? tag = await context.Tags.FindAsync(tagId);
        if (tag is null)
        {
            return false;
        }

        return await context.CanUserAttachTagDirectlyAsync(tag, userId);
    }
}