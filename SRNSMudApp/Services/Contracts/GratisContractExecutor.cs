using System.Diagnostics;

using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Models.Unions;

#pragma warning disable CA1508
#pragma warning disable IDE0010, IDE0072

namespace SRNSMudApp.Services.Contracts;

public class GratisContractExecutor(ApplicationDbContext dbContext) : IContractExecutor
{
    public string ContractType => ContractTypes.Gratis;

    public async Task<Result<string>> ExecuteAsync(TaggingRequestEntity contract, string currentUserId, int? fulfillerAssetId = null)
    {
        Result<int> assetProcessResult = await (contract.ConsumedRightAsset switch
        {
            not null => UpdateConsumedAsset(contract.ConsumedRightAsset, contract.ConsumedRightAssetId!.Value),
            null => CreateNewRightAsset(contract.TagOwnerUserId, contract.RequestedTagId)
        });

        var consumedAssetId = assetProcessResult switch
        {
            Success<int> s => s.Value,
            Failure => throw new UnreachableException("RightAsset processing cannot fail here")
        };

        Tag? requestedTag = contract.RequestedTag ?? await dbContext.Tags!.FindAsync(contract.RequestedTagId);

        return await (requestedTag switch
        {
            null => Task.FromResult<Result<string>>(new Failure("Tag not found")),
            var tag => ProcessGratisTagActionAsync(contract, tag, consumedAssetId, currentUserId)
        });
    }

    private Task<Result<int>> UpdateConsumedAsset(RightAsset asset, int assetId)
    {
        asset.IsBurned = true;
        asset.Status = new Burned(DateTime.UtcNow);
        _ = dbContext.RightAssets.Update(asset);
        return Task.FromResult<Result<int>>(new Success<int>(assetId));
    }

    private async Task<Result<int>> CreateNewRightAsset(string ownerId, int targetTagId)
    {
        var rightAsset = new RightAsset
        {
            OwnerId = ownerId,
            TargetTagId = targetTagId,
            IsBurned = true,
            Status = new Burned(DateTime.UtcNow)
        };
        _ = dbContext.RightAssets.Add(rightAsset);
        _ = await dbContext.SaveChangesAsync();
        return new Success<int>(rightAsset.Id);
    }

    private async Task<Result<string>> ProcessGratisTagActionAsync(TaggingRequestEntity contract, Tag tag, int consumedAssetId, string executorUserId)
    {
        return await (contract.RequestType switch
        {
            TaggingRequestType.Add => ProcessGratisAddAsync(contract, tag, consumedAssetId, executorUserId),
            TaggingRequestType.Remove => ProcessGratisRemoveOrDecreaseAsync(contract, tag, consumedAssetId, executorUserId, true),
            TaggingRequestType.DecreaseWeight => ProcessGratisRemoveOrDecreaseAsync(contract, tag, consumedAssetId, executorUserId, false),
            _ => Task.FromResult<Result<string>>(new Failure("無効なリクエストタイプです。"))
        });
    }

    private async Task<Result<string>> ProcessGratisAddAsync(TaggingRequestEntity contract, Tag tag, int consumedAssetId, string executorUserId)
    {
        var newRelation = new TagRelation
        {
            ItemId = contract.TargetItemId,
            TagId = contract.RequestedTagId,
            Weight = contract.ProposedWeight,
            OwnerId = contract.RequesterUserId
        };
        _ = dbContext.TagRelations.Add(newRelation);
        _ = await dbContext.SaveChangesAsync();

        var previousWeightAdd = tag.CachedWeight;
        tag.CachedWeight += contract.ProposedWeight;
        var newWeightAdd = tag.CachedWeight;

        var ledgerAdd = new TagWeightLedger
        {
            TagId = contract.RequestedTagId,
            TagNameSnapshot = tag.Name,
            ItemId = contract.TargetItemId,
            SourceType = "TagRelation",
            SourceId = newRelation.Id,
            ConsumedRightAssetId = consumedAssetId,
            Delta = contract.ProposedWeight,
            PreviousWeight = previousWeightAdd,
            NewWeight = newWeightAdd,
            IsOwnerAction = true,
            Reason = "Gratis Tagging Contract Accepted",
            OwnerId = executorUserId
        };
        _ = dbContext.TagWeightLedgers.Add(ledgerAdd);

        _ = dbContext.TimelineEvents.Add(new TimelineEvent
        {
            OwnerId = executorUserId,
            Target = new ItemTarget(contract.TargetItemId),
            FollowedTagId = contract.RequestedTagId,
            EventType = "Insert",
            NewWeight = contract.ProposedWeight
        });

        return new Success<string>("タグを追加しました。");
    }

    private async Task<Result<string>> ProcessGratisRemoveOrDecreaseAsync(TaggingRequestEntity contract, Tag tag, int consumedAssetId, string executorUserId, bool isRemove)
    {
        TagRelation? relation = await dbContext.TagRelations
            .FirstOrDefaultAsync(tr => tr.ItemId == contract.TargetItemId && tr.TagId == contract.RequestedTagId);

        return await (relation switch
        {
            null => Task.FromResult<Result<string>>(new Failure("対象のタグ付けが見つかりません。")),
            var r => ProcessRelationDecreaseAsync(contract, tag, r, consumedAssetId, executorUserId, isRemove)
        });
    }

    private Task<Result<string>> ProcessRelationDecreaseAsync(TaggingRequestEntity contract, Tag tag, TagRelation relation, int consumedAssetId, string executorUserId, bool isRemove)
    {
        var prevWeight = relation.Weight;
        var delta = isRemove ? -prevWeight : -contract.ProposedWeight;

        _ = (isRemove || prevWeight + delta <= 0) switch
        {
            true => dbContext.TagRelations.Remove(relation),
            false => (object)(relation.Weight += delta)
        };

        var previousTagWeight = tag.CachedWeight;
        tag.CachedWeight += delta;

        _ = dbContext.TagWeightLedgers.Add(new TagWeightLedger
        {
            TagId = contract.RequestedTagId,
            TagNameSnapshot = tag.Name,
            ItemId = contract.TargetItemId,
            SourceType = "TagRelation",
            SourceId = relation.Id,
            ConsumedRightAssetId = consumedAssetId,
            Delta = delta,
            PreviousWeight = previousTagWeight,
            NewWeight = tag.CachedWeight,
            IsOwnerAction = true,
            Reason = $"Gratis Tagging Contract Accepted ({(isRemove ? "Remove" : "Decrease Weight")})",
            OwnerId = executorUserId
        });

        _ = dbContext.TimelineEvents.Add(new TimelineEvent
        {
            OwnerId = executorUserId,
            Target = new ItemTarget(contract.TargetItemId),
            FollowedTagId = contract.RequestedTagId,
            EventType = "Update",
            NewWeight = contract.ProposedWeight
        });

        return Task.FromResult<Result<string>>(new Success<string>("タグを削除または削減しました。"));
    }
}