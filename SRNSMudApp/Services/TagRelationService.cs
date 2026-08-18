using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using SRNSMudApp.Data;

namespace SRNSMudApp.Services;

// 処理の結果をUIに伝えるための列挙型
public enum TaggingResult
{
    Success,
    ErrorNotFound
}

public class TagRelationService(ApplicationDbContext context)
{
    /// <summary>
    ///     アイテムにタグを紐付けるメインロジック
    /// </summary>
    /// <param name="itemId">紐付け先のアイテムID</param>
    /// <param name="tagId">紐付けるタグID</param>
    /// <param name="currentUserId">操作しているユーザーのID</param>
    /// <param name="requiredWeight">紐付けに必要な重み（デフォルト1など）</param>
    public async Task<TaggingResult> LinkTagToItemAsync(int itemId, int tagId, string currentUserId,
        int requiredWeight = 1)
    {
        // 1. 必要なデータの取得
        Item? item = await context.Items.FindAsync(itemId);
        Tag? tag = await context.Tags.FindAsync(tagId);

        if (item == null || tag == null)
        {
            return TaggingResult.ErrorNotFound;
        }

        await using IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync();

        try
        {
            // 2. RightAsset の発行と即時消費 (Burn)
            // このメソッドはテストやレガシーAPI向けのため、現在のユーザー宛てにAssetを発行して使用する
            var rightAsset = new RightAsset
            {
                OwnerId = currentUserId,
                TargetTagId = tagId,
                IsBurned = true,
                BurnedAt = DateTime.UtcNow
            };
            _ = context.RightAssets.Add(rightAsset);
            _ = await context.SaveChangesAsync();

            // 3. TagRelation を作成
            var relation = new TagRelation
            {
                ItemId = itemId,
                TagId = tagId,
                OwnerId = currentUserId,
                Weight = requiredWeight
            };
            _ = context.TagRelations.Add(relation);
            _ = await context.SaveChangesAsync();

            // 4. Cache Update
            var previousWeight = tag.CachedWeight;
            tag.CachedWeight += requiredWeight;
            var newWeight = tag.CachedWeight;

            // 5. Ledger 作成
            var ledger = new TagWeightLedger
            {
                TagId = tagId,
                TagNameSnapshot = tag.Name,
                SourceType = "TagRelation",
                SourceId = relation.Id,
                ConsumedRightAssetId = rightAsset.Id,
                Delta = requiredWeight,
                PreviousWeight = previousWeight,
                NewWeight = newWeight,
                IsOwnerAction = tag.OwnerId == currentUserId,
                Reason = "TagRelationService.LinkTagToItemAsync (Legacy/Direct)",
                OwnerId = currentUserId
            };
            _ = context.TagWeightLedgers.Add(ledger);

            _ = await context.SaveChangesAsync();
            await transaction.CommitAsync();
            return TaggingResult.Success;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    ///     既存のRightAssetを消費して、TagRelationにWeightを割り当てる
    /// </summary>
    public async Task<TaggingResult> AllocateWeightAsync(int rightAssetId, int itemId, int tagId, string currentUserId,
        int manipulationDelta)
    {
        if (manipulationDelta == 0)
        {
            throw new ArgumentException("Manipulation delta cannot be 0");
        }

        var consumeAmount = Math.Abs(manipulationDelta);

        RightAsset? rightAsset = await context.RightAssets.FindAsync(rightAssetId);
        if (rightAsset == null || rightAsset.OwnerId != currentUserId || rightAsset.IsBurned)
        {
            return TaggingResult.ErrorNotFound;
        }

        if (rightAsset.Amount < consumeAmount)
        {
            throw new InvalidOperationException("RightAsset amount is insufficient.");
        }

        Item? item = await context.Items.FindAsync(itemId);
        Tag? tag = await context.Tags.FindAsync(tagId);

        if (item == null || tag == null)
        {
            return TaggingResult.ErrorNotFound;
        }

        await using IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync();

        try
        {
            rightAsset.Amount -= consumeAmount;
            if (rightAsset.Amount == 0)
            {
                rightAsset.IsBurned = true;
                rightAsset.BurnedAt = DateTime.UtcNow;
            }

            _ = context.RightAssets.Update(rightAsset);

            TagRelation? relation =
                await context.TagRelations.FirstOrDefaultAsync(r => r.ItemId == itemId && r.TagId == tagId);
            if (relation == null)
            {
                relation = new TagRelation { ItemId = itemId, TagId = tagId, OwnerId = currentUserId, Weight = 0 };
                _ = context.TagRelations.Add(relation);
            }

            relation.Weight += manipulationDelta;
            _ = await context.SaveChangesAsync();

            var previousWeight = tag.CachedWeight;
            tag.CachedWeight += manipulationDelta;
            var newWeight = tag.CachedWeight;

            var ledger = new TagWeightLedger
            {
                TagId = tagId,
                TagNameSnapshot = tag.Name,
                SourceType = "TagRelation",
                SourceId = relation.Id,
                ConsumedRightAssetId = rightAsset.Id,
                Delta = manipulationDelta,
                PreviousWeight = previousWeight,
                NewWeight = newWeight,
                IsOwnerAction = tag.OwnerId == currentUserId,
                Reason = "TagRelationService.AllocateWeightAsync",
                OwnerId = currentUserId
            };
            _ = context.TagWeightLedgers.Add(ledger);

            _ = await context.SaveChangesAsync();
            await transaction.CommitAsync();

            return TaggingResult.Success;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}