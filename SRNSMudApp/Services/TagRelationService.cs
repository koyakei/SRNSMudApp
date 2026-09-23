using System.Diagnostics.CodeAnalysis;

using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;

namespace SRNSMudApp.Services;

public class TagRelationService(IDbContextFactory<ApplicationDbContext> dbFactory)
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));

    /// <summary>
    ///     アイテムにタグを紐付けるメインロジック
    /// </summary>
    public async Task<Result<bool>> LinkTagToItemAsync(int itemId, int tagId, string currentUserId,
        int requiredWeight = 1)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();
        Item? item = await context.Items.FindAsync(itemId);
        Tag? tag = await context.Tags.FindAsync(tagId);

        return await ((item, tag) switch
        {
            (not null, not null) => ExecuteLinkTagTransactionAsync(context, item, tag, currentUserId, requiredWeight),
            _ => Task.FromResult<Result<bool>>(new Failure("Item or Tag not found"))
        });
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "トランザクション実行中の予期せぬ例外をFailure結果として捕捉するため")]
    private static async Task<Result<bool>> ExecuteLinkTagTransactionAsync(ApplicationDbContext context, Item item, Tag tag, string currentUserId, int requiredWeight)
    {
        try
        {
            return await context.Database.ExecuteWithStrategyAsync(async () =>
            {
                var rightAsset = new RightAsset
                {
                    OwnerId = currentUserId,
                    TargetTagId = tag.Id,
                    IsBurned = true,
                    Status = new Burned(DateTime.UtcNow)
                };
                _ = context.RightAssets.Add(rightAsset);
                _ = await context.SaveChangesAsync();

                var relation = new TagRelation
                {
                    ItemId = item.Id,
                    TagId = tag.Id,
                    OwnerId = currentUserId,
                    Weight = requiredWeight
                };
                _ = context.TagRelations.Add(relation);
                _ = await context.SaveChangesAsync();

                var previousWeight = tag.CachedWeight;
                tag.CachedWeight += requiredWeight;
                var newWeight = tag.CachedWeight;

                var ledger = new TagWeightLedger
                {
                    TagId = tag.Id,
                    TagNameSnapshot = tag.Name,
                    ItemId = item.Id,
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
                return new Success<bool>(true);
            });
        }
        catch (DbUpdateConcurrencyException)
        {
            return new Failure("データの状態が変更されました。ページを再読み込みしてから、もう一度やり直してください。");
        }
        catch (Exception ex)
        {
            return new Failure($"タグの紐付け中にエラーが発生しました: {ex.Message}");
        }
    }

    /// <summary>
    ///     既存のRightAssetを消費して、TagRelationにWeightを割り当てる
    /// </summary>
    public async Task<Result<bool>> AllocateWeightAsync(int rightAssetId, int itemId, int tagId, string currentUserId,
        int manipulationDelta)
    {
        return await (manipulationDelta switch
        {
            0 => Task.FromException<Result<bool>>(new ArgumentException("Manipulation delta cannot be 0")),
            _ => ProcessAllocateWeightAsync(rightAssetId, itemId, tagId, currentUserId, manipulationDelta)
        });
    }

    private async Task<Result<bool>> ProcessAllocateWeightAsync(int rightAssetId, int itemId, int tagId, string currentUserId, int manipulationDelta)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();
        var consumeAmount = Math.Abs(manipulationDelta);
        RightAsset? rightAsset = await context.RightAssets.FindAsync(rightAssetId);

        return await (rightAsset switch
        {
            null => Task.FromResult<Result<bool>>(new Failure("RightAsset not found or unauthorized")),
            _ when rightAsset.OwnerId != currentUserId || rightAsset.IsBurned => Task.FromResult<Result<bool>>(new Failure("RightAsset not found or unauthorized")),
            _ when rightAsset.Amount < consumeAmount => Task.FromException<Result<bool>>(new InvalidOperationException("RightAsset amount is insufficient.")),
            _ => FetchEntitiesAndExecuteAllocationAsync(context, rightAsset, itemId, tagId, currentUserId, manipulationDelta, consumeAmount)
        });
    }

    private static async Task<Result<bool>> FetchEntitiesAndExecuteAllocationAsync(ApplicationDbContext context, RightAsset rightAsset, int itemId, int tagId, string currentUserId, int manipulationDelta, int consumeAmount)
    {
        Item? item = await context.Items.FindAsync(itemId);
        Tag? tag = await context.Tags.FindAsync(tagId);

        return await ((item, tag) switch
        {
            (not null, not null) => ExecuteAllocationTransactionAsync(context, rightAsset, item, tag, currentUserId, manipulationDelta, consumeAmount),
            _ => Task.FromResult<Result<bool>>(new Failure("Item or Tag not found"))
        });
    }
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "トランザクション実行中の予期せぬ例外をFailure結果として捕捉するため")]
    private static async Task<Result<bool>> ExecuteAllocationTransactionAsync(ApplicationDbContext context, RightAsset rightAsset, Item item, Tag tag, string currentUserId, int manipulationDelta, int consumeAmount)
    {
        try
        {
            return await context.Database.ExecuteWithStrategyAsync(async () =>
            {
                rightAsset.Amount -= consumeAmount;
                rightAsset.IsBurned = rightAsset.Amount switch
                {
                    0 => true,
                    _ => rightAsset.IsBurned
                };

                rightAsset.Status = rightAsset.Amount switch
                {
                    0 => new Burned(DateTime.UtcNow),
                    _ => rightAsset.Status
                };

                _ = context.RightAssets.Update(rightAsset);

                TagRelation? relation = await context.TagRelations.FirstOrDefaultAsync(r => r.ItemId == item.Id && r.TagId == tag.Id);
                relation = relation switch
                {
                    null => new TagRelation { ItemId = item.Id, TagId = tag.Id, OwnerId = currentUserId, Weight = 0 },
                    _ => relation
                };

                _ = relation.Id switch
                {
                    0 => context.TagRelations.Add(relation),
                    _ => null
                };

                relation.Weight += manipulationDelta;
                _ = await context.SaveChangesAsync();

                var previousWeight = tag.CachedWeight;
                tag.CachedWeight += manipulationDelta;
                var newWeight = tag.CachedWeight;

                var ledger = new TagWeightLedger
                {
                    TagId = tag.Id,
                    TagNameSnapshot = tag.Name,
                    ItemId = item.Id,
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
                return new Success<bool>(true);
            });
        }
        catch (DbUpdateConcurrencyException)
        {
            return new Failure("データの状態が変更されました。ページを再読み込みしてから、もう一度やり直してください。");
        }
        catch (Exception ex)
        {
            return new Failure($"ウェイト割り当て中にエラーが発生しました: {ex.Message}");
        }
    }
}