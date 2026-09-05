using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Models.Unions;

#pragma warning disable CA1508
#pragma warning disable IDE0010, IDE0072

namespace SRNSMudApp.Services.Contracts;

/// <summary>
///     Bounty コントラクトの承認・実行処理を担当する <see cref="IContractExecutor" /> 実装。
///     タグオーナーが報酬（バウンティ）を設定し、実行者が RightAsset を提供してタグを付与または解除する。
/// </summary>
public class BountyContractExecutor(
    ApplicationDbContext dbContext,
    TimeProvider? timeProvider = null) : IContractExecutor
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public string ContractType => ContractTypes.Bounty;

    public async Task<Result<string>> ExecuteAsync(TaggingRequestEntity contract, string currentUserId, int? fulfillerAssetId = null)
    {
        Result<int> assetProvider = await ResolveBountyAssetProviderAsync(contract, currentUserId, fulfillerAssetId);

        return await (assetProvider switch
        {
            Failure f => Task.FromResult<Result<string>>(f),
            Success<int> s => ProcessBountyRequestAsync(contract, currentUserId, s.Value)
        });
    }

    private async Task<Result<int>> ResolveBountyAssetProviderAsync(TaggingRequestEntity contract, string fulfillerUserId, int? fulfillerAssetId)
    {
        Result<int> state = (fulfillerAssetId.HasValue, contract.RequestedTag!.OwnerId == fulfillerUserId) switch
        {
            (true, _) => await VerifyAndConsumeFulfillerAssetAsync(fulfillerAssetId!.Value, fulfillerUserId, contract.RequestedTagId),
            (false, true) => await MintAndConsumeGoodwillAssetAsync(fulfillerUserId, contract.RequestedTagId),
            (false, false) => new Failure("バウンティを承認するには対象タグの RightAsset が必要です。")
        };
        return state;
    }

    private async Task<Result<int>> VerifyAndConsumeFulfillerAssetAsync(int fulfillerAssetId, string fulfillerUserId, int requestedTagId)
    {
        RightAsset? fulfillerAsset = await dbContext.RightAssets
            .FirstOrDefaultAsync(a => a.Id == fulfillerAssetId && a.OwnerId == fulfillerUserId && !a.IsBurned);

        Result<RightAsset> assetValidation = fulfillerAsset switch
        {
            null => new Failure("提供されたアセットが無効または所有していません。"),
            var a when a.TargetTagId != requestedTagId => new Failure("提供されたアセットは対象タグの権利ではありません。"),
            var a => new Success<RightAsset>(a)
        };

        return assetValidation switch
        {
            Failure f => f,
            Success<RightAsset> s => ConsumeFulfillerAsset(s.Value)
        };
    }

    private Result<int> ConsumeFulfillerAsset(RightAsset asset)
    {
        asset.IsBurned = true;
        asset.Status = new Burned(_timeProvider.GetUtcNow().UtcDateTime);
        _ = dbContext.RightAssets.Update(asset);
        return new Success<int>(asset.Id);
    }

    private async Task<Result<int>> MintAndConsumeGoodwillAssetAsync(string fulfillerUserId, int requestedTagId)
    {
        var rightAsset = new RightAsset
        {
            OwnerId = fulfillerUserId,
            TargetTagId = requestedTagId,
            IsBurned = true,
            Status = new Burned(_timeProvider.GetUtcNow().UtcDateTime)
        };
        _ = dbContext.RightAssets.Add(rightAsset);
        _ = await dbContext.SaveChangesAsync();
        return new Success<int>(rightAsset.Id);
    }

    private async Task<Result<string>> ProcessBountyRequestAsync(TaggingRequestEntity contract, string fulfillerUserId, int consumedAssetId)
    {
        return await (contract.RequestType switch
        {
            TaggingRequestType.Add => ProcessBountyAddAsync(contract, fulfillerUserId, consumedAssetId),
            TaggingRequestType.Remove => ProcessBountyRemoveAsync(contract, fulfillerUserId, consumedAssetId),
            _ => Task.FromResult<Result<string>>(new Failure("無効なリクエストタイプです。"))
        });
    }

    private async Task<Result<string>> ProcessBountyAddAsync(TaggingRequestEntity contract, string fulfillerUserId, int consumedAssetId)
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

        Result<bool> rewardResult = await TransferBountyRewardAsync(contract, fulfillerUserId);

        return await (rewardResult switch
        {
            Failure f => Task.FromResult<Result<string>>(f),
            Success<bool> => ProcessBountyAddTagLedgerAsync(contract, fulfillerUserId, consumedAssetId, newRelation)
        });
    }

    private async Task<Result<string>> ProcessBountyAddTagLedgerAsync(TaggingRequestEntity contract, string fulfillerUserId, int consumedAssetId, TagRelation newRelation)
    {
        Tag? tag = contract.RequestedTag ?? await dbContext.Tags.FindAsync(contract.RequestedTagId);

        Result<Tag> tagResult = tag switch
        {
            null => new Failure("Tag not found"),
            _ => new Success<Tag>(tag)
        };

        return tagResult switch
        {
            Failure f => f,
            Success<Tag> s => CompleteBountyAddTagLedger(contract, fulfillerUserId, consumedAssetId, newRelation, s.Value)
        };
    }

    private Result<string> CompleteBountyAddTagLedger(TaggingRequestEntity contract, string fulfillerUserId, int consumedAssetId, TagRelation newRelation, Tag tag)
    {
        var previousWeight = tag.CachedWeight;
        tag.CachedWeight += contract.ProposedWeight;
        var newWeight = tag.CachedWeight;

        var ledger = new TagWeightLedger
        {
            TagId = contract.RequestedTagId,
            TagNameSnapshot = tag.Name,
            ItemId = contract.TargetItemId,
            SourceType = "TagRelation",
            SourceId = newRelation.Id,
            ConsumedRightAssetId = consumedAssetId,
            Delta = contract.ProposedWeight,
            PreviousWeight = previousWeight,
            NewWeight = newWeight,
            IsOwnerAction = tag.OwnerId == fulfillerUserId,
            Reason = (contract.Payload is BountyPayload b && b.OfferedRewardAssetId != 0) ? "Reward Bounty Fulfilled" : "Goodwill Bounty Fulfilled",
            OwnerId = fulfillerUserId
        };
        _ = dbContext.TagWeightLedgers.Add(ledger);

        _ = dbContext.TimelineEvents.Add(new TimelineEvent
        {
            OwnerId = fulfillerUserId,
            Target = new ItemTarget(contract.TargetItemId),
            FollowedTagId = contract.RequestedTagId,
            EventType = "Insert",
            NewWeight = contract.ProposedWeight
        });

        return new Success<string>("バウンティリクエストを承認しました。");
    }

    private async Task<Result<bool>> TransferBountyRewardAsync(TaggingRequestEntity contract, string fulfillerUserId)
    {
        Result<bool> actionState = (contract.Payload is BountyPayload b2 && b2.OfferedRewardAssetId != 0) switch
        {
            false => new Success<bool>(true),
            true => await ProcessRewardTransferAsync(contract, fulfillerUserId)
        };
        return actionState;
    }

    private async Task<Result<bool>> ProcessRewardTransferAsync(TaggingRequestEntity contract, string fulfillerUserId)
    {
        var rewardAssetId = contract.Payload is BountyPayload b3 ? b3.OfferedRewardAssetId : 0;
        RightAsset? rewardAsset = await dbContext.RightAssets
            .FirstOrDefaultAsync(a => a.Id == rewardAssetId && a.OwnerId == contract.RequesterUserId);

        Result<bool> authResult = (rewardAsset?.IsBurned == false) switch
        {
            true => CompleteRewardTransfer(rewardAsset, fulfillerUserId),
            false => new Failure("約束された報酬アセットが無効になっています。")
        };
        return authResult;
    }

    private Result<bool> CompleteRewardTransfer(RightAsset rewardAsset, string fulfillerUserId)
    {
        rewardAsset.OwnerId = fulfillerUserId;
        _ = dbContext.RightAssets.Update(rewardAsset);
        return new Success<bool>(true);
    }

    private async Task<Result<string>> ProcessBountyRemoveAsync(TaggingRequestEntity contract, string fulfillerUserId, int consumedAssetId)
    {
        TagRelation? relation = await dbContext.TagRelations
            .FirstOrDefaultAsync(tr => tr.ItemId == contract.TargetItemId && tr.TagId == contract.RequestedTagId);

        return await (relation switch
        {
            null => Task.FromResult<Result<string>>(new Success<string>("削除対象が存在しません。")),
            var r => ProcessBountyRemoveRelationAsync(contract, fulfillerUserId, consumedAssetId, r)
        });
    }

    private async Task<Result<string>> ProcessBountyRemoveRelationAsync(TaggingRequestEntity contract, string fulfillerUserId, int consumedAssetId, TagRelation relation)
    {
        var prevWeight = relation.Weight;
        _ = dbContext.TagRelations.Remove(relation);

        Result<bool> rewardResult = await TransferBountyRewardAsync(contract, fulfillerUserId);

        return await (rewardResult switch
        {
            Failure f => Task.FromResult<Result<string>>(f),
            Success<bool> => ProcessBountyRemoveTagLedgerAsync(contract, fulfillerUserId, consumedAssetId, relation, prevWeight)
        });
    }

    private async Task<Result<string>> ProcessBountyRemoveTagLedgerAsync(TaggingRequestEntity contract, string fulfillerUserId, int consumedAssetId, TagRelation relation, int prevWeight)
    {
        Tag? tag = contract.RequestedTag ?? await dbContext.Tags.FindAsync(contract.RequestedTagId);

        Result<Tag> tagResult = tag switch
        {
            null => new Failure("Tag not found"),
            _ => new Success<Tag>(tag)
        };

        return tagResult switch
        {
            Failure f => f,
            Success<Tag> s => CompleteBountyRemoveTagLedger(contract, fulfillerUserId, consumedAssetId, relation, prevWeight, s.Value)
        };
    }

    private Result<string> CompleteBountyRemoveTagLedger(TaggingRequestEntity contract, string fulfillerUserId, int consumedAssetId, TagRelation relation, int prevWeight, Tag tag)
    {
        var previousWeight = tag.CachedWeight;
        tag.CachedWeight -= prevWeight;
        var newWeight = tag.CachedWeight;

        var ledger = new TagWeightLedger
        {
            TagId = contract.RequestedTagId,
            TagNameSnapshot = tag.Name,
            ItemId = contract.TargetItemId,
            SourceType = "TagRelation",
            SourceId = relation.Id,
            ConsumedRightAssetId = consumedAssetId,
            Delta = -prevWeight,
            PreviousWeight = previousWeight,
            NewWeight = newWeight,
            IsOwnerAction = tag.OwnerId == fulfillerUserId,
            Reason = (contract.Payload is BountyPayload b4 && b4.OfferedRewardAssetId != 0) ? "Reward Bounty Fulfilled (Remove)" : "Goodwill Bounty Fulfilled (Remove)",
            OwnerId = fulfillerUserId
        };
        _ = dbContext.TagWeightLedgers.Add(ledger);

        _ = dbContext.TimelineEvents.Add(new TimelineEvent
        {
            OwnerId = fulfillerUserId,
            Target = new ItemTarget(contract.TargetItemId),
            FollowedTagId = contract.RequestedTagId,
            EventType = "Delete",
            PreviousWeight = prevWeight
        });

        return new Success<string>("バウンティリクエスト(削除)を承認しました。");
    }
}