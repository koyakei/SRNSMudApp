using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using SRNSMudApp.Models.Unions;

namespace SRNSMudApp.Data;

public static class ApplicationDbContextTagExtensions
{
    /// <summary>
    ///     タグのオーナーが RightAsset を自動発行して消費し、自身のタグを付与するシナリオ
    /// </summary>
    public static async Task CreateFreeTagRelationAsync(
        this ApplicationDbContext context,
        int itemId,
        int tagId,
        string currentUserId)
    {
        ArgumentNullException.ThrowIfNull(context);

        await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync();

        try
        {
            // 1. Tag のオーナー権限検証 (SystemClassificationTag は誰でも無償付与可能、UserCustomTag は本人のみ)
            Tag tag = await context.Tags.FindAsync(tagId) ?? throw new InvalidOperationException("指定されたタグが見つかりません。");
#pragma warning disable CA1508, IDE0072
            var isAuthorized = tag.GetKind() switch
            {
                UserCustomTag custom => custom.OwnerId == currentUserId,
                SystemClassificationTag or ReactionTag => true,
                VoteTag => false
            };
#pragma warning restore CA1508, IDE0072

            if (!isAuthorized)
            {
                throw new UnauthorizedAccessException("このタグを無償で付与する権限がありません（タグのオーナーではありません）。");
            }

            // 2. RightAsset の発行と即時消費 (Burn)
            var rightAsset = new RightAsset
            {
                OwnerId = currentUserId,
                TargetTagId = tagId,
                IsBurned = true,
                BurnStatusJson = JsonSerializer.Serialize<BurnStatus>(new Burned(DateTime.UtcNow))
            };
            _ = context.RightAssets.Add(rightAsset);
            _ = await context.SaveChangesAsync(); // IDを発行するためにSave

            // 3. TagRelation の作成
            var relation = new TagRelation
            {
                ItemId = itemId,
                TagId = tagId,
                OwnerId = currentUserId,
                Weight = 1 // 基本値
            };
            _ = context.TagRelations.Add(relation);

            _ = await context.SaveChangesAsync();

            // 4. Tag.CachedWeight の更新と以前の値の取得
            var previousWeight = tag.CachedWeight;
            tag.CachedWeight++;
            var newWeight = tag.CachedWeight;

            // 5. 元帳 (Ledger) への記帳
            var isSystemTag = tag.GetKind() is SystemClassificationTag;
            var ledger = new TagWeightLedger
            {
                TagId = tagId,
                TagNameSnapshot = tag.Name,
                ItemId = itemId,
                SourceType = "TagRelation",
                SourceId = relation.Id,
                ConsumedRightAssetId = rightAsset.Id, // 必ずセットされる
                Delta = 1,
                PreviousWeight = previousWeight,
                NewWeight = newWeight,
                IsOwnerAction = !isSystemTag || tag.OwnerId == currentUserId,
                Reason = isSystemTag ? "System Classification Tagging" : "Owner Self-Tagging",
                OwnerId = currentUserId
            };
            _ = context.TagWeightLedgers.Add(ledger);

            _ = context.TimelineEvents.Add(new TimelineEvent
            {
                OwnerId = currentUserId,
                TimelineTargetJson = JsonSerializer.Serialize<TimelineTarget>(new ItemTarget(itemId)),
                FollowedTagId = tagId,
                EventType = "Insert",
                NewWeight = 1
            });

            _ = await context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (Exception)
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}