using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Models;
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

    /// <summary>指定IDのアイテムを取得する（親アイテム等の参照用）。</summary>
    Task<Item?> GetItemByIdAsync(int itemId);

    /// <summary>管理者権限によりアイテムを強制削除する。</summary>
    Task<bool> DeleteItemByAdminAsync(int itemId, string adminUserId);

    /// <summary>管理者権限によりアイテムの強制非公開状態（および解除）を設定する。</summary>
    Task<bool> SetAdminHiddenAsync(int itemId, bool isHidden, string? reason, string adminUserId);
}

public class ItemCardDataProvider(IDbContextFactory<ApplicationDbContext> dbFactory) : IItemCardDataProvider
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));

    public async Task<Item?> GetItemByIdAsync(int itemId)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();
        return await context.Items
            .AsNoTracking()
            .Include(i => i.Owner)
            .Include(i => i.TargetUserGroup)
            .Include(i => i.TagRelations)
                .ThenInclude(tr => tr.Tag)
            .FirstOrDefaultAsync(i => i.Id == itemId);
    }

    public async Task DeleteItemAsync(int itemId)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();
        Item? item = await context.Items
            .Include(i => i.TagTarget)
            .FirstOrDefaultAsync(i => i.Id == itemId);
        if (item is not null)
        {
            await CleanupItemDependenciesAsync(context, item);
            _ = context.Items.Remove(item);
            _ = await context.SaveChangesAsync();
        }
    }

    public async Task<bool> DeleteItemByAdminAsync(int itemId, string adminUserId)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();
        Item? item = await context.Items
            .Include(i => i.TagTarget)
            .FirstOrDefaultAsync(i => i.Id == itemId);
        if (item is null)
        {
            return false;
        }

        await CleanupItemDependenciesAsync(context, item);
        _ = context.Items.Remove(item);
        _ = await context.SaveChangesAsync();
        return true;
    }

    private static async Task CleanupItemDependenciesAsync(ApplicationDbContext context, Item item)
    {
        // 外部キー制約（DeleteBehavior.Restrict）による削除失敗を防ぐため、
        // 当該アイテム宛ての未決コントラクト提案やリクエスト本体を事前にクリーンアップする
        if (item.TagTargetId > 0)
        {
            List<TaggingRequestEntity> relatedTaggingRequests = await context.TaggingRequestEntities
                .Where(t => t.TargetId == item.TagTargetId || t.RequestItemId == item.Id)
                .ToListAsync();
            if (relatedTaggingRequests.Count > 0)
            {
                context.TaggingRequestEntities.RemoveRange(relatedTaggingRequests);
            }
        }

        // 当該アイテムへのリプライ（コメント等）も併せて削除する
        List<Item> replyItems = await context.Items
            .Where(i => i.ParentItemId == item.Id || i.RootItemId == item.Id)
            .ToListAsync();
        if (replyItems.Count > 0)
        {
            context.Items.RemoveRange(replyItems);
        }

        // Issue 1-1: 被引用アイテムの外部キー (QuotedItemId) を NULL 化して削除可能にする
        await context.Items
            .Where(i => i.QuotedItemId == item.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.QuotedItemId, (int?)null));

        // Issue 1-2: 当該アイテムを対象とする分割リクエスト (OriginalItemId / CreatedItemId) を削除
        List<ItemSplitRequest> splitRequests = await context.ItemSplitRequests
            .Where(s => s.OriginalItemId == item.Id || s.CreatedItemId == item.Id)
            .ToListAsync();
        if (splitRequests.Count > 0)
        {
            context.ItemSplitRequests.RemoveRange(splitRequests);
        }
    }

    public async Task<bool> SetAdminHiddenAsync(int itemId, bool isHidden, string? reason, string adminUserId)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();
        Item? item = await context.Items.FindAsync(itemId);
        if (item is null)
        {
            return false;
        }

        item.IsAdminHidden = isHidden;
        item.AdminHiddenAt = isHidden ? DateTimeOffset.UtcNow : null;
        item.AdminHiddenReason = isHidden ? reason : null;
        item.UpdatedDate = DateTime.UtcNow;

        _ = await context.SaveChangesAsync();
        return true;
    }

    public async Task<Tag?> GetTagWithOwnerAsync(int tagId)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();
        var strategy = context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
            await context.Tags.AsNoTracking().Include(t => t.Owner).WhereVisibleToUser().FirstOrDefaultAsync(t => t.Id == tagId));
    }

    public async Task<TagRelation?> AddFreeTagRelationAsync(int itemId, int tagId, string userId)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();
        var strategy = context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
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
        });
    }

    public async Task CreateItemAsync(Item item, IReadOnlyCollection<int>? initialTagIds)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();
        _ = context.Items.Add(item);

        List<TagRelation> tagRelations = [];
        if (initialTagIds is { Count: > 0 })
        {
            tagRelations = initialTagIds
                .Select(tagId => new TagRelation
                {
                    TagId = tagId,
                    Weight = 1,
                    OwnerId = item.OwnerId
                })
                .ToList();

            item.TagRelations = tagRelations;
        }

        _ = await context.SaveChangesAsync();

        if (tagRelations.Count > 0)
        {
            var tagIds = tagRelations.Select(tr => tr.TagId).ToList();
            List<Tag> tags = await context.Tags.Where(t => tagIds.Contains(t.Id)).ToListAsync();
            foreach (TagRelation relation in tagRelations)
            {
                Tag? tag = tags.FirstOrDefault(t => t.Id == relation.TagId);
                var (prevWeight, newWeight) = tag != null
                    ? tag.ApplyWeightDelta(1)
                    : (0, 1);

                _ = context.TagWeightLedgers.Add(new TagWeightLedger
                {
                    TagId = relation.TagId,
                    TagNameSnapshot = tag?.Name ?? string.Empty,
                    ItemId = item.Id,
                    SourceType = "TagRelationInsert",
                    SourceId = relation.Id,
                    TagRelation = relation,
                    PreviousWeight = prevWeight,
                    NewWeight = newWeight,
                    Delta = 1,
                    IsOwnerAction = true,
                    Reason = "タグの新規追加",
                    OwnerId = item.OwnerId
                });

                _ = context.TimelineEvents.Add(new TimelineEvent
                {
                    OwnerId = item.OwnerId,
                    TimelineTargetJson = JsonSerializer.Serialize<TimelineTarget>(new ItemTarget(item.Id)),
                    FollowedTagId = relation.TagId,
                    ParsedEventType = TimelineEventType.Insert,
                    NewWeight = 1
                });
            }

            _ = await context.SaveChangesAsync();
        }
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
        var strategy = context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            Tag? tag = await context.Tags.FindAsync(tagId);
            return tag is not null && await context.CanUserAttachTagDirectlyAsync(tag, userId);
        });
    }
}