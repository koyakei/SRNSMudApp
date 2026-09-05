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
///     ステートレスな設計とし、呼び出し元からトランザクション境界となる <see cref="ApplicationDbContext" /> を受け取る。
/// </summary>
public class BountyContractExecutor(
    TimeProvider? timeProvider = null) : IContractExecutor
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public string ContractType => ContractTypes.Bounty;

    public async Task<Result<string>> ExecuteAsync(ApplicationDbContext dbContext, TaggingRequestEntity contract, string currentUserId, int? fulfillerAssetId = null)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(contract);

        Result<int> assetProvider = await ResolveBountyAssetProviderAsync(dbContext, contract, currentUserId, fulfillerAssetId);

        return await (assetProvider switch
        {
            Failure f => Task.FromResult<Result<string>>(f),
            Success<int> s => ProcessBountyRequestAsync(dbContext, contract, currentUserId, s.Value)
        });
    }

    private async Task<Result<int>> ResolveBountyAssetProviderAsync(ApplicationDbContext dbContext, TaggingRequestEntity contract, string fulfillerUserId, int? fulfillerAssetId)
    {
        Result<int> state = (fulfillerAssetId.HasValue, contract.RequestedTag!.OwnerId == fulfillerUserId) switch
        {
            (true, _) => await VerifyAndConsumeFulfillerAssetAsync(dbContext, fulfillerAssetId!.Value, fulfillerUserId, contract.RequestedTagId),
            (false, true) => await MintAndConsumeGoodwillAssetAsync(dbContext, fulfillerUserId, contract.RequestedTagId),
            (false, false) => new Failure("バウンティを承認するには対象タグの RightAsset が必要です。")
        };
        return state;
    }

    private async Task<Result<int>> VerifyAndConsumeFulfillerAssetAsync(ApplicationDbContext dbContext, int fulfillerAssetId, string fulfillerUserId, int requestedTagId)
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
            Success<RightAsset> s => ConsumeFulfillerAsset(dbContext, s.Value)
        };
    }

    private Result<int> ConsumeFulfillerAsset(ApplicationDbContext dbContext, RightAsset asset)
    {
        asset.IsBurned = true;
        asset.Status = new Burned(_timeProvider.GetUtcNow().UtcDateTime);
        _ = dbContext.RightAssets.Update(asset);
        return new Success<int>(asset.Id);
    }

    private async Task<Result<int>> MintAndConsumeGoodwillAssetAsync(ApplicationDbContext dbContext, string fulfillerUserId, int requestedTagId)
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

    private static async Task<Result<string>> ProcessBountyRequestAsync(ApplicationDbContext dbContext, TaggingRequestEntity contract, string fulfillerUserId, int consumedAssetId)
    {
        return await (contract.RequestType switch
        {
            TaggingRequestType.Add => ProcessBountyAddAsync(dbContext, contract, fulfillerUserId, consumedAssetId),
            TaggingRequestType.Remove => ProcessBountyRemoveAsync(dbContext, contract, fulfillerUserId, consumedAssetId),
            _ => Task.FromResult<Result<string>>(new Failure("無効なリクエストタイプです。"))
        });
    }

    private static async Task<Result<string>> ProcessBountyAddAsync(ApplicationDbContext dbContext, TaggingRequestEntity contract, string fulfillerUserId, int consumedAssetId)
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

        Result<bool> rewardResult = await TransferBountyRewardAsync(dbContext, contract, fulfillerUserId);

        return await (rewardResult switch
        {
            Failure f => Task.FromResult<Result<string>>(f),
            Success<bool> => ProcessBountyAddTagLedgerAsync(dbContext, contract, fulfillerUserId, consumedAssetId, newRelation)
        });
    }

    private static async Task<Result<string>> ProcessBountyAddTagLedgerAsync(ApplicationDbContext dbContext, TaggingRequestEntity contract, string fulfillerUserId, int consumedAssetId, TagRelation newRelation)
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
            Success<Tag> s => CompleteBountyAddTagLedger(dbContext, contract, fulfillerUserId, consumedAssetId, newRelation, s.Value)
        };
    }

    private static Result<string> CompleteBountyAddTagLedger(ApplicationDbContext dbContext, TaggingRequestEntity contract, string fulfillerUserId, int consumedAssetId, TagRelation newRelation, Tag tag)
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

    private static async Task<Result<bool>> TransferBountyRewardAsync(ApplicationDbContext dbContext, TaggingRequestEntity contract, string fulfillerUserId)
    {
        Result<bool> actionState = (contract.Payload is BountyPayload b2 && b2.OfferedRewardAssetId != 0) switch
        {
            false => new Success<bool>(true),
            true => await ProcessRewardTransferAsync(dbContext, contract, fulfillerUserId)
        };
        return actionState;
    }

    private static async Task<Result<bool>> ProcessRewardTransferAsync(ApplicationDbContext dbContext, TaggingRequestEntity contract, string fulfillerUserId)
    {
        var rewardAssetId = contract.Payload is BountyPayload b3 ? b3.OfferedRewardAssetId : 0;
        RightAsset? rewardAsset = await dbContext.RightAssets
            .FirstOrDefaultAsync(a => a.Id == rewardAssetId && a.OwnerId == contract.RequesterUserId);

        Result<bool> authResult = (rewardAsset?.IsBurned == false) switch
        {
            true => CompleteRewardTransfer(dbContext, rewardAsset, fulfillerUserId),
            false => new Failure("約束された報酬アセットが無効になっています。")
        };
        return authResult;
    }

    private static Result<bool> CompleteRewardTransfer(ApplicationDbContext dbContext, RightAsset rewardAsset, string fulfillerUserId)
    {
        rewardAsset.OwnerId = fulfillerUserId;
        _ = dbContext.RightAssets.Update(rewardAsset);
        return new Success<bool>(true);
    }

    private static async Task<Result<string>> ProcessBountyRemoveAsync(ApplicationDbContext dbContext, TaggingRequestEntity contract, string fulfillerUserId, int consumedAssetId)
    {
        TagRelation? relation = await dbContext.TagRelations
            .FirstOrDefaultAsync(tr => tr.ItemId == contract.TargetItemId && tr.TagId == contract.RequestedTagId);

        return await (relation switch
        {
            null => Task.FromResult<Result<string>>(new Success<string>("削除対象が存在しません。")),
            var r => ProcessBountyRemoveRelationAsync(dbContext, contract, fulfillerUserId, consumedAssetId, r)
        });
    }

    private static async Task<Result<string>> ProcessBountyRemoveRelationAsync(ApplicationDbContext dbContext, TaggingRequestEntity contract, string fulfillerUserId, int consumedAssetId, TagRelation relation)
    {
        var prevWeight = relation.Weight;
        _ = dbContext.TagRelations.Remove(relation);

        Result<bool> rewardResult = await TransferBountyRewardAsync(dbContext, contract, fulfillerUserId);

        return await (rewardResult switch
        {
            Failure f => Task.FromResult<Result<string>>(f),
            Success<bool> => ProcessBountyRemoveTagLedgerAsync(dbContext, contract, fulfillerUserId, consumedAssetId, relation, prevWeight)
        });
    }

    private static async Task<Result<string>> ProcessBountyRemoveTagLedgerAsync(ApplicationDbContext dbContext, TaggingRequestEntity contract, string fulfillerUserId, int consumedAssetId, TagRelation relation, int prevWeight)
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
            Success<Tag> s => CompleteBountyRemoveTagLedger(dbContext, contract, fulfillerUserId, consumedAssetId, relation, prevWeight, s.Value)
        };
    }

    private static Result<string> CompleteBountyRemoveTagLedger(ApplicationDbContext dbContext, TaggingRequestEntity contract, string fulfillerUserId, int consumedAssetId, TagRelation relation, int prevWeight, Tag tag)
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