using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Models.Unions;

#pragma warning disable CA1508
#pragma warning disable IDE0010, IDE0072

namespace SRNSMudApp.Services.Contracts;

/// <summary>
///     Trigger/PublicOffer コントラクトの承認・実行処理を担当する <see cref="IContractExecutor" /> 実装。
///     依頼者自身が RightAsset を消費してコントラクトを実行し、対象アイテムへタグを付与または解除する。
///     ステートレスな設計とし、呼び出し元からトランザクション境界となる <see cref="ApplicationDbContext" /> を受け取る。
/// </summary>
public class TriggerContractExecutor(
    TimeProvider? timeProvider = null) : IContractExecutor
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public string ContractType => ContractTypes.Trigger;

    public async Task<Result<string>> ExecuteAsync(ApplicationDbContext dbContext, TaggingRequestEntity contract, string currentUserId, int? fulfillerAssetId = null)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(contract);

        var targetOfferId = contract.Payload is PublicOfferPayload p ? p.TargetPublicTradeOfferId : 0;
        PublicTradeOffer? offer = await dbContext.PublicTradeOffers
                                     .FirstOrDefaultAsync(o => o.Id == targetOfferId);

        Result<PublicTradeOffer> offerResult = offer switch
        {
            null => new Failure("公開オファーが見つかりません。"),
            { IsActive: false } => new Failure("この公開オファーは現在有効ではありません。"),
            var o => new Success<PublicTradeOffer>(o)
        };

        return await (offerResult switch
        {
            Failure f => Task.FromResult<Result<string>>(f),
            Success<PublicTradeOffer> s => ProcessTriggerWithOfferAsync(dbContext, contract, s.Value)
        });
    }

    private async Task<Result<string>> ProcessTriggerWithOfferAsync(ApplicationDbContext dbContext, TaggingRequestEntity contract, PublicTradeOffer offer)
    {
        Result<RightAsset?> assetValidation = (offer.RequiredAssetAmount > 0) switch
        {
            true => ValidateTriggerAsset(contract, offer),
            false => new Success<RightAsset?>(null)
        };

        return await (assetValidation switch
        {
            Failure f => Task.FromResult<Result<string>>(f),
            Success<RightAsset?> s => ProcessTriggerAssetActionAsync(dbContext, contract, offer, s.Value)
        });
    }

    private static Result<RightAsset?> ValidateTriggerAsset(TaggingRequestEntity contract, PublicTradeOffer offer)
    {
        return contract.ConsumedRightAsset switch
        {
            null => new Failure("提供された RightAsset の量が不足しています。"),
            var a when a.Amount < offer.RequiredAssetAmount => new Failure("提供された RightAsset の量が不足しています。"),
            var a when a.TargetTagId != offer.OfferedTagId => new Failure("提供された RightAsset は対象のタグの権利ではありません。"),
            var a => new Success<RightAsset?>(a)
        };
    }

    private async Task<Result<string>> ProcessTriggerAssetActionAsync(ApplicationDbContext dbContext, TaggingRequestEntity contract, PublicTradeOffer offer, RightAsset? validatedAsset)
    {
        var consumedAssetId = await (validatedAsset switch
        {
            not null => UpdateTriggerConsumedAssetAsync(dbContext, validatedAsset, contract.ConsumedRightAssetId!.Value),
            null => CreateTriggerOwnerAssetAsync(dbContext, offer)
        });

        return await (contract.RequestType switch
        {
            TaggingRequestType.Add => ProcessTriggerAddAsync(dbContext, contract, offer, consumedAssetId),
            TaggingRequestType.Remove => ProcessTriggerRemoveAsync(dbContext, contract, offer, consumedAssetId),
            _ => Task.FromResult<Result<string>>(new Failure("無効なリクエストタイプです。"))
        });
    }

    private Task<int> UpdateTriggerConsumedAssetAsync(ApplicationDbContext dbContext, RightAsset asset, int assetId)
    {
        asset.IsBurned = true;
        asset.Status = new Burned(_timeProvider.GetUtcNow().UtcDateTime);
        _ = dbContext.RightAssets.Update(asset);
        return Task.FromResult(assetId);
    }

    private async Task<int> CreateTriggerOwnerAssetAsync(ApplicationDbContext dbContext, PublicTradeOffer offer)
    {
        var ownerAsset = new RightAsset
        {
            OwnerId = offer.OwnerId,
            TargetTagId = offer.OfferedTagId,
            IsBurned = true,
            Status = new Burned(_timeProvider.GetUtcNow().UtcDateTime)
        };
        _ = dbContext.RightAssets.Add(ownerAsset);
        _ = await dbContext.SaveChangesAsync();
        return ownerAsset.Id;
    }

    private static async Task<Result<string>> ProcessTriggerAddAsync(ApplicationDbContext dbContext, TaggingRequestEntity contract, PublicTradeOffer offer, int consumedAssetId)
    {
        var newRelation = new TagRelation
        {
            ItemId = contract.TargetItemId,
            TagId = offer.OfferedTagId,
            Weight = contract.ProposedWeight,
            OwnerId = contract.RequesterUserId
        };
        _ = dbContext.TagRelations.Add(newRelation);
        _ = await dbContext.SaveChangesAsync();

        Tag? tag = await dbContext.Tags.FindAsync(offer.OfferedTagId);
        Result<Tag> tagResult = tag switch
        {
            null => new Failure("Tag not found"),
            _ => new Success<Tag>(tag)
        };

        return tagResult switch
        {
            Failure f => f,
            Success<Tag> s => CompleteTriggerAdd(dbContext, contract, offer, consumedAssetId, s.Value, newRelation)
        };
    }

    private static Result<string> CompleteTriggerAdd(ApplicationDbContext dbContext, TaggingRequestEntity contract, PublicTradeOffer offer, int consumedAssetId, Tag tag, TagRelation newRelation)
    {
        var prevWeight = tag.CachedWeight;
        tag.CachedWeight += contract.ProposedWeight;
        var newWeight = tag.CachedWeight;

        var ledger = new TagWeightLedger
        {
            TagId = offer.OfferedTagId,
            TagNameSnapshot = tag.Name,
            ItemId = contract.TargetItemId,
            SourceType = "TagRelation",
            SourceId = newRelation.Id,
            ConsumedRightAssetId = consumedAssetId,
            Delta = contract.ProposedWeight,
            PreviousWeight = prevWeight,
            NewWeight = newWeight,
            IsOwnerAction = false,
            Reason = "Public Offer Triggered",
            OwnerId = contract.RequesterUserId
        };
        _ = dbContext.TagWeightLedgers.Add(ledger);

        _ = dbContext.TimelineEvents.Add(new TimelineEvent
        {
            OwnerId = contract.RequesterUserId,
            Target = new ItemTarget(contract.TargetItemId),
            FollowedTagId = offer.OfferedTagId,
            EventType = "Insert",
            NewWeight = contract.ProposedWeight
        });

        return new Success<string>("公開オファーを実行しました。");
    }

    private static async Task<Result<string>> ProcessTriggerRemoveAsync(ApplicationDbContext dbContext, TaggingRequestEntity contract, PublicTradeOffer offer, int consumedAssetId)
    {
        TagRelation? relation = await dbContext.TagRelations
            .FirstOrDefaultAsync(tr => tr.ItemId == contract.TargetItemId && tr.TagId == offer.OfferedTagId);

        return await (relation switch
        {
            null => Task.FromResult<Result<string>>(new Success<string>("削除対象が存在しません。")),
            var r => ProcessTriggerRemoveRelationAsync(dbContext, contract, offer, consumedAssetId, r)
        });
    }

    private static async Task<Result<string>> ProcessTriggerRemoveRelationAsync(ApplicationDbContext dbContext, TaggingRequestEntity contract, PublicTradeOffer offer, int consumedAssetId, TagRelation relation)
    {
        var prevWeight = relation.Weight;
        _ = dbContext.TagRelations.Remove(relation);

        Tag? tag = await dbContext.Tags.FindAsync(offer.OfferedTagId);
        Result<Tag> tagResult = tag switch
        {
            null => new Failure("Tag not found"),
            _ => new Success<Tag>(tag)
        };

        return tagResult switch
        {
            Failure f => f,
            Success<Tag> s => CompleteTriggerRemove(dbContext, contract, offer, consumedAssetId, s.Value, relation, prevWeight)
        };
    }

    private static Result<string> CompleteTriggerRemove(ApplicationDbContext dbContext, TaggingRequestEntity contract, PublicTradeOffer offer, int consumedAssetId, Tag tag, TagRelation relation, int prevWeight)
    {
        var previousWeight = tag.CachedWeight;
        tag.CachedWeight -= prevWeight;
        var newWeight = tag.CachedWeight;

        var ledger = new TagWeightLedger
        {
            TagId = offer.OfferedTagId,
            TagNameSnapshot = tag.Name,
            ItemId = contract.TargetItemId,
            SourceType = "TagRelation",
            SourceId = relation.Id,
            ConsumedRightAssetId = consumedAssetId,
            Delta = -prevWeight,
            PreviousWeight = previousWeight,
            NewWeight = newWeight,
            IsOwnerAction = false,
            Reason = "Public Offer Triggered (Remove)",
            OwnerId = contract.RequesterUserId
        };
        _ = dbContext.TagWeightLedgers.Add(ledger);

        _ = dbContext.TimelineEvents.Add(new TimelineEvent
        {
            OwnerId = contract.RequesterUserId,
            Target = new ItemTarget(contract.TargetItemId),
            FollowedTagId = offer.OfferedTagId,
            EventType = "Delete",
            PreviousWeight = prevWeight
        });

        return new Success<string>("公開オファーをキャンセル(Remove)しました。");
    }
}