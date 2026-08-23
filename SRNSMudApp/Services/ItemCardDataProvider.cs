using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;

namespace SRNSMudApp.Services;

/// <summary>投票操作の結果。</summary>
public enum ItemVoteAction
{
    /// <summary>新しい投票を追加した。</summary>
    Added,

    /// <summary>既存の投票の Weight を変更した。</summary>
    Updated,

    /// <summary>既存の投票を取り消した。</summary>
    Removed
}

/// <summary>投票操作の結果とローカル状態更新に必要な情報。</summary>
public sealed record ItemVoteResult(ItemVoteAction Action, int RelationId, int Weight);

/// <summary>
///     ItemCard コンポーネント用のデータアクセスを分離するインターフェース。
///     コンポーネントから DbContext への直接依存を断ち、単体テストでモック可能にする。
/// </summary>
public interface IItemCardDataProvider
{
    /// <summary>good タグへの投票を追加 / 変更 / 取り消しする。</summary>
    Task<ItemVoteResult> ToggleItemVoteAsync(int itemId, string userId, int goodTagId, int targetWeight);

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
}

public class ItemCardDataProvider(IDbContextFactory<ApplicationDbContext> dbFactory) : IItemCardDataProvider
{
    public async Task<ItemVoteResult> ToggleItemVoteAsync(
        int itemId,
        string userId,
        int goodTagId,
        int targetWeight)
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();
        List<TagRelation> relations = await context.TagRelations
            .Where(tr => tr.ItemId == itemId && tr.OwnerId == userId)
            .ToListAsync();

        TagRelation? existingRelation = relations.FirstOrDefault(tr => tr.TagId == goodTagId);
        Tag? tag = await context.Tags.FindAsync(goodTagId);

        switch (existingRelation)
        {
            case null:
            {
                var newRelation = new TagRelation
                {
                    ItemId = itemId,
                    TagId = goodTagId,
                    OwnerId = userId,
                    Weight = targetWeight,
                    CreatedDate = DateTime.UtcNow,
                    UpdatedDate = DateTime.UtcNow
                };
                context.TagRelations.Add(newRelation);
                await context.SaveChangesAsync();
                return new ItemVoteResult(ItemVoteAction.Added, newRelation.Id, targetWeight);
            }
            default:
                switch (existingRelation.Weight == targetWeight)
                {
                    // 同じ Weight なら投票取り消し
                    case true:
                    {
                        var deltaCancel = -existingRelation.Weight;
                        switch (tag)
                        {
                            case not null:
                                var prevWeightCancel = tag.CachedWeight;
                                tag.CachedWeight += deltaCancel;
                                context.TagWeightLedgers!.Add(new TagWeightLedger
                                {
                                    TagId = tag.Id,
                                    TagNameSnapshot = tag.Name,
                                    SourceType = "TagRelationDelete",
                                    SourceId = existingRelation.Id,
                                    PreviousWeight = prevWeightCancel,
                                    NewWeight = tag.CachedWeight,
                                    Delta = deltaCancel,
                                    IsOwnerAction = true,
                                    Reason = "Vote取り消し",
                                    OwnerId = userId
                                });
                                break;
                        }

                        context.TimelineEvents!.Add(new TimelineEvent
                        {
                            OwnerId = userId,
                            Target = new ItemTarget(itemId),
                            FollowedTagId = goodTagId,
                            EventType = "Delete",
                            PreviousWeight = existingRelation.Weight
                        });

                        context.TagRelations.Remove(existingRelation);
                        await context.SaveChangesAsync();
                        return new ItemVoteResult(ItemVoteAction.Removed, existingRelation.Id, existingRelation.Weight);
                    }
                    default:
                    {
                        var deltaUpdate = targetWeight - existingRelation.Weight;
                        existingRelation.Weight = targetWeight;
                        existingRelation.UpdatedDate = DateTime.UtcNow;

                        switch (tag)
                        {
                            case not null:
                                var prevWeightUpdate = tag.CachedWeight;
                                tag.CachedWeight += deltaUpdate;
                                context.TagWeightLedgers!.Add(new TagWeightLedger
                                {
                                    TagId = tag.Id,
                                    TagNameSnapshot = tag.Name,
                                    SourceType = "TagRelationUpdate",
                                    SourceId = existingRelation.Id,
                                    PreviousWeight = prevWeightUpdate,
                                    NewWeight = tag.CachedWeight,
                                    Delta = deltaUpdate,
                                    IsOwnerAction = true,
                                    Reason = "Vote変更",
                                    OwnerId = userId
                                });
                                break;
                        }

                        context.TimelineEvents!.Add(new TimelineEvent
                        {
                            OwnerId = userId,
                            Target = new ItemTarget(itemId),
                            FollowedTagId = goodTagId,
                            EventType = "Update",
                            PreviousWeight = existingRelation.Weight - deltaUpdate,
                            NewWeight = existingRelation.Weight
                        });

                        await context.SaveChangesAsync();
                        return new ItemVoteResult(ItemVoteAction.Updated, existingRelation.Id, targetWeight);
                    }
                }
        }
    }

    public async Task DeleteItemAsync(int itemId)
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();
        Item? item = await context.Items.FindAsync(itemId);
        switch (item)
        {
            case not null:
                context.Items.Remove(item);
                await context.SaveChangesAsync();
                break;
        }
    }

    public async Task<Tag?> GetTagWithOwnerAsync(int tagId)
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();
        return await context.Tags.Include(t => t.Owner).FirstOrDefaultAsync(t => t.Id == tagId);
    }

    public async Task<TagRelation?> AddFreeTagRelationAsync(int itemId, int tagId, string userId)
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();

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
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();
        context.Items.Add(item);

        switch (initialTagIds is { Count: > 0 })
        {
            case true:
            {
                List<TagRelation> tagRelations = initialTagIds
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
        }

        await context.SaveChangesAsync();
    }
    public async Task<bool> UpdateItemContentAsync(int itemId, string content)
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();
        Item? itemToUpdate = await context.Items.FindAsync(itemId);
        switch (itemToUpdate)
        {
            case null:
                return false;
        }

        itemToUpdate.Content = content;
        await context.SaveChangesAsync();
        return true;
    }
}
