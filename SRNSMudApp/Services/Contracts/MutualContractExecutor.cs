using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Models.Unions;

#pragma warning disable CA1508
#pragma warning disable IDE0010, IDE0072

namespace SRNSMudApp.Services.Contracts;

/// <summary>
///     Mutual（相互タグ付け）コントラクトの承認・実行処理を担当する <see cref="IContractExecutor" /> 実装。
///     依頼者と承認者が互いに RightAsset を消費し、双方のアイテムにタグを付与または解除する。
/// </summary>
public class MutualContractExecutor(
    ApplicationDbContext dbContext,
    TimeProvider? timeProvider = null) : IContractExecutor
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public string ContractType => ContractTypes.Mutual;

    public async Task<Result<string>> ExecuteAsync(TaggingRequestEntity contract, string currentUserId, int? fulfillerAssetId = null)
    {
        Result<RightAsset> assetValidation = contract.ConsumedRightAsset switch
        {
            null => new Failure("相互タグ付けには対価のアセットが必要です。"),
            _ => new Success<RightAsset>(contract.ConsumedRightAsset)
        };

        return await (assetValidation switch
        {
            Failure f => Task.FromResult<Result<string>>(f),
            Success<RightAsset> s => ProcessMutualWithAssetAsync(contract, s.Value, currentUserId)
        });
    }

    private async Task<Result<string>> ProcessMutualWithAssetAsync(TaggingRequestEntity contract, RightAsset consumedAsset, string executorUserId)
    {
        var requesterAssetId = contract.ConsumedRightAssetId!.Value;
        consumedAsset.IsBurned = true;
        consumedAsset.Status = new Burned(_timeProvider.GetUtcNow().UtcDateTime);
        _ = dbContext.RightAssets!.Update(consumedAsset);

        var offeredTagAsset = new RightAsset
        {
            OwnerId = contract.RequesterUserId,
            TargetTagId = contract.Payload is MutualPayload m2 ? m2.OfferedTagId : 0,
            IsBurned = true,
            Status = new Burned(_timeProvider.GetUtcNow().UtcDateTime)
        };
        _ = dbContext.RightAssets!.Add(offeredTagAsset);

        Tag? requestedTag = contract.RequestedTag ?? await dbContext.Tags!.FindAsync(contract.RequestedTagId);
        Tag? offeredTag = await dbContext.Tags!.FindAsync(contract.Payload is MutualPayload m3 ? m3.OfferedTagId : 0);

        Result<(Tag Req, Tag Off)> fetchResult = (requestedTag, offeredTag) switch
        {
            (null, _) => new Failure("Requested Tag not found"),
            (_, null) => new Failure("Offered Tag not found"),
            (var r, var o) => new Success<(Tag, Tag)>((r, o))
        };

        return await (fetchResult switch
        {
            Failure f => Task.FromResult<Result<string>>(f),
            Success<(Tag req, Tag off)> s => ProcessMutualTagsAsync(contract, s.Value.req, s.Value.off, requesterAssetId, offeredTagAsset, executorUserId)
        });
    }

    private async Task<Result<string>> ProcessMutualTagsAsync(TaggingRequestEntity contract, Tag requestedTag, Tag offeredTag, int requesterAssetId, RightAsset offeredTagAsset, string executorUserId)
    {
        Result<string> processResult = await (contract.RequestType switch
        {
            TaggingRequestType.Add => ProcessMutualAddAsync(contract, requestedTag, requesterAssetId, executorUserId),
            TaggingRequestType.Remove => ProcessMutualRemoveAsync(contract, requestedTag, requesterAssetId, executorUserId),
            _ => Task.FromResult<Result<string>>(new Failure("無効なリクエストタイプです。"))
        });

        return await (processResult switch
        {
            Failure f => Task.FromResult<Result<string>>(f),
            Success<string> => ProcessMutualOfferedAsync(contract, offeredTag, offeredTagAsset, executorUserId)
        });
    }

    private async Task<Result<string>> ProcessMutualAddAsync(TaggingRequestEntity contract, Tag requestedTag, int requesterAssetId, string executorUserId)
    {
        var relation1 = new TagRelation
        {
            ItemId = contract.TargetItemId,
            TagId = contract.RequestedTagId,
            Weight = contract.ProposedWeight,
            OwnerId = contract.RequesterUserId
        };
        _ = dbContext.TagRelations.Add(relation1);
        _ = await dbContext.SaveChangesAsync();

        var prevReqWeight = requestedTag.CachedWeight;
        requestedTag.CachedWeight += contract.ProposedWeight;
        var newReqWeight = requestedTag.CachedWeight;

        var ledger1 = new TagWeightLedger
        {
            TagId = contract.RequestedTagId,
            TagNameSnapshot = requestedTag.Name,
            ItemId = contract.TargetItemId,
            SourceType = "TagRelation",
            SourceId = relation1.Id,
            ConsumedRightAssetId = requesterAssetId,
            Delta = contract.ProposedWeight,
            PreviousWeight = prevReqWeight,
            NewWeight = newReqWeight,
            IsOwnerAction = true,
            Reason = "Mutual Tagging Contract Accepted (Requested)",
            OwnerId = executorUserId
        };
        _ = dbContext.TagWeightLedgers.Add(ledger1);

        _ = dbContext.TimelineEvents.Add(new TimelineEvent
        {
            OwnerId = executorUserId,
            Target = new ItemTarget(contract.TargetItemId),
            FollowedTagId = contract.RequestedTagId,
            EventType = "Insert",
            NewWeight = contract.ProposedWeight
        });

        return new Success<string>("追加完了");
    }

    private async Task<Result<string>> ProcessMutualRemoveAsync(TaggingRequestEntity contract, Tag requestedTag, int requesterAssetId, string executorUserId)
    {
        TagRelation? relation1Remove = await dbContext.TagRelations
            .FirstOrDefaultAsync(tr => tr.ItemId == contract.TargetItemId && tr.TagId == contract.RequestedTagId);

        Result<TagRelation> removeResult = relation1Remove switch
        {
            null => new Failure("対象のタグ付けが見つかりません。"),
            var r => new Success<TagRelation>(r)
        };

        return removeResult switch
        {
            Failure f => f,
            Success<TagRelation> s => ProcessMutualRemoveRelation(contract, requestedTag, s.Value, requesterAssetId, executorUserId)
        };
    }

    private Result<string> ProcessMutualRemoveRelation(TaggingRequestEntity contract, Tag requestedTag, TagRelation relation, int requesterAssetId, string executorUserId)
    {
        var prevWeight = relation.Weight;
        _ = dbContext.TagRelations.Remove(relation);

        var prevReqWeightRem = requestedTag.CachedWeight;
        requestedTag.CachedWeight -= prevWeight;
        var newReqWeightRem = requestedTag.CachedWeight;

        var ledger1Rem = new TagWeightLedger
        {
            TagId = contract.RequestedTagId,
            TagNameSnapshot = requestedTag.Name,
            ItemId = contract.TargetItemId,
            SourceType = "TagRelation",
            SourceId = relation.Id,
            ConsumedRightAssetId = requesterAssetId,
            Delta = -prevWeight,
            PreviousWeight = prevReqWeightRem,
            NewWeight = newReqWeightRem,
            IsOwnerAction = true,
            Reason = "Mutual Tagging Contract Accepted (Requested Remove)",
            OwnerId = executorUserId
        };
        _ = dbContext.TagWeightLedgers.Add(ledger1Rem);

        _ = dbContext.TimelineEvents.Add(new TimelineEvent
        {
            OwnerId = executorUserId,
            Target = new ItemTarget(contract.TargetItemId),
            FollowedTagId = contract.RequestedTagId,
            EventType = "Delete",
            PreviousWeight = prevWeight
        });

        return new Success<string>("削除完了");
    }

    private async Task<Result<string>> ProcessMutualOfferedAsync(TaggingRequestEntity contract, Tag offeredTag, RightAsset offeredTagAsset, string executorUserId)
    {
        if (contract.Payload is not MutualPayload mutualPayload)
        {
            return new Failure("Mutual payload is missing");
        }

        var relation2 = new TagRelation
        {
            ItemId = mutualPayload.OfferedTargetItemId,
            TagId = mutualPayload.OfferedTagId,
            Weight = contract.ProposedWeight,
            OwnerId = contract.TagOwnerUserId
        };
        _ = dbContext.TagRelations.Add(relation2);
        _ = await dbContext.SaveChangesAsync();

        var prevOffWeight = offeredTag.CachedWeight;
        offeredTag.CachedWeight += contract.ProposedWeight;
        var newOffWeight = offeredTag.CachedWeight;

        var ledger2 = new TagWeightLedger
        {
            TagId = mutualPayload.OfferedTagId,
            TagNameSnapshot = offeredTag.Name,
            ItemId = mutualPayload.OfferedTargetItemId,
            SourceType = "TagRelation",
            SourceId = relation2.Id,
            ConsumedRightAssetId = offeredTagAsset.Id,
            Delta = contract.ProposedWeight,
            PreviousWeight = prevOffWeight,
            NewWeight = newOffWeight,
            IsOwnerAction = false,
            Reason = "Mutual Tagging Contract Accepted (Offered)",
            OwnerId = contract.RequesterUserId
        };
        _ = dbContext.TagWeightLedgers.Add(ledger2);

        _ = dbContext.TimelineEvents.Add(new TimelineEvent
        {
            OwnerId = executorUserId,
            Target = new ItemTarget(mutualPayload.OfferedTargetItemId),
            FollowedTagId = mutualPayload.OfferedTagId,
            EventType = "Insert",
            NewWeight = contract.ProposedWeight
        });

        return new Success<string>("相互タグ付けが完了しました。");
    }
}