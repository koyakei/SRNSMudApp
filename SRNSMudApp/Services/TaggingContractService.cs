using System.Diagnostics;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;

namespace SRNSMudApp.Services;

public class TaggingContractService(ApplicationDbContext dbContext)
{
    public async Task<Result<TaggingRequestEntity>> ProposeGratisContractAsync(
        string requesterUserId,
        string tagOwnerUserId,
        int targetItemId,
        int requestedTagId,
        TaggingRequestType requestType = TaggingRequestType.Add,
        int proposedWeight = 1,
        string? message = null)
    {
        var content = message ?? (requestType switch
        {
            TaggingRequestType.Add => "タグ追加リクエストを送信しました。",
            _ => "タグ削除リクエストを送信しました。"
        });

        var requestItem = new Item
        {
            OwnerId = requesterUserId,
            Content = content
        };

        var contract = new TaggingRequestEntity
        {
            ContractType = "Gratis",
            OwnerId = requesterUserId,
            RequesterUserId = requesterUserId,
            TagOwnerUserId = tagOwnerUserId,
            TargetItemId = targetItemId,
            RequestedTagId = requestedTagId,
            Status = TradeStatus.Proposed,
            RequestType = requestType,
            ProposedWeight = proposedWeight,
            RequesterMessage = message,
            RequestItem = requestItem
        };

        _ = dbContext.TaggingRequestEntities!.Add(contract);
        _ = await dbContext.SaveChangesAsync();
        return new Success<TaggingRequestEntity>(contract);
    }

    public async Task<Result<TaggingRequestEntity>> ProposeMutualContractAsync(
        string requesterUserId,
        string tagOwnerUserId,
        int targetItemId,
        int requestedTagId,
        int offeredTargetItemId,
        int offeredTagId,
        int consumedRightAssetId,
        TaggingRequestType requestType = TaggingRequestType.Add,
        int proposedWeight = 1)
    {
        var content = requestType switch
        {
            TaggingRequestType.Add => "タグ追加リクエスト(Mutual)を送信しました。",
            _ => "タグ削除リクエスト(Mutual)を送信しました。"
        };

        var requestItem = new Item
        {
            OwnerId = requesterUserId,
            Content = content
        };

        var contract = new TaggingRequestEntity
        {
            ContractType = "Mutual",
            OwnerId = requesterUserId,
            RequesterUserId = requesterUserId,
            TagOwnerUserId = tagOwnerUserId,
            TargetItemId = targetItemId,
            RequestedTagId = requestedTagId,
            OfferedTargetItemId = offeredTargetItemId,
            OfferedTagId = offeredTagId,
            ConsumedRightAssetId = consumedRightAssetId,
            Status = TradeStatus.Proposed,
            RequestType = requestType,
            ProposedWeight = proposedWeight,
            RequestItem = requestItem
        };
        _ = dbContext.TaggingRequestEntities!.Add(contract);
        _ = await dbContext.SaveChangesAsync();
        return new Success<TaggingRequestEntity>(contract);
    }

    public async Task<List<TaggingRequestEntity>> GetRequestsByItemIdAsync(int itemId)
    {
        return await dbContext.TaggingRequestEntities!
            .Include(r => r.TargetItem)
            .Include(r => r.Owner)
            .Include(r => r.RequestedTag)
            .Where(r => r.TargetItemId == itemId)
            .OrderByDescending(r => r.CreatedDate)
            .ToListAsync();
    }

    public async Task<Result<string>> AcceptContractAsync(int contractId, string currentUserId, int? fulfillerAssetId = null)
    {
        TaggingRequestEntity? entity = await dbContext.TaggingRequestEntities!
                                          .Include(c => c.RequestedTag)
                                          .Include(c => c.ConsumedRightAsset)
                                          .FirstOrDefaultAsync(c => c.Id == contractId);

        Result<TaggingRequestEntity> preCheckResult = entity switch
        {
            null => new Failure("契約が見つかりません。"),
            { Status: not TradeStatus.Proposed } => new Failure("実行・承認できない状態の契約です。"),
            { ContractType: "Trigger", RequesterUserId: var reqId } when reqId != currentUserId => new Failure("実行できない契約です。"),
            { ContractType: "Gratis" or "Mutual", TagOwnerUserId: var ownerId } when ownerId != currentUserId => new Failure("承認できない契約です。"),
            _ => new Success<TaggingRequestEntity>(entity)
        };

        return await (preCheckResult switch
        {
            Failure f => Task.FromResult<Result<string>>(f),
            Success<TaggingRequestEntity> s => ProcessAcceptContractAtomicAsync(s.Value, currentUserId, fulfillerAssetId)
        });
    }

    private async Task<Result<string>> ProcessAcceptContractAtomicAsync(TaggingRequestEntity entity, string currentUserId, int? fulfillerAssetId)
    {
        await using IDbContextTransaction transaction = await dbContext.Database.BeginTransactionAsync();

        try
        {
            var executeResult = await (entity.ContractType switch
            {
                "Gratis" => ExecuteGratisAsync(new GratisContractData(entity), currentUserId),
                "Mutual" => ExecuteMutualAsync(new MutualContractData(entity), currentUserId),
                "Trigger" => ExecuteTriggerAsync(new TriggerContractData(entity), currentUserId),
                "Bounty" => ExecuteBountyAsync(new BountyContractData(entity), currentUserId, fulfillerAssetId),
                _ => Task.FromResult<Result<string>>(new Failure("DBに未知の契約型が存在します。"))
            });

            return await (executeResult switch
            {
                Failure f => RollbackAndReturnAsync(transaction, f),
                Success<string> s => CommitAndReturnAsync(transaction, entity, s)
            });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return new Failure($"契約の承認中に予期せぬエラーが発生しました: {ex.Message}");
        }
    }

    private async Task<Result<string>> RollbackAndReturnAsync(IDbContextTransaction transaction, Failure f)
    {
        await transaction.RollbackAsync();
        return f;
    }

    private async Task<Result<string>> CommitAndReturnAsync(IDbContextTransaction transaction, TaggingRequestEntity entity, Success<string> s)
    {
        entity.Status = TradeStatus.Executed;
        _ = await dbContext.SaveChangesAsync();
        await transaction.CommitAsync();
        return s;
    }

    public async Task<Result<string>> CancelContractAsync(int contractId, string currentUserId)
    {
        TaggingRequestEntity? entity = await dbContext.TaggingRequestEntities!
                                          .FirstOrDefaultAsync(c => c.Id == contractId);

        Result<TaggingRequestEntity> fetchResult = entity switch
        {
            null => new Failure("契約が見つかりません。"),
            { Status: not TradeStatus.Proposed } => new Failure("この状態の契約はキャンセルできません。"),
            var e when e.RequesterUserId != currentUserId && e.TagOwnerUserId != currentUserId => new Failure("この契約をキャンセル・拒否する権限がありません。"),
            _ => new Success<TaggingRequestEntity>(entity)
        };

        return await (fetchResult switch
        {
            Failure f => Task.FromResult<Result<string>>(f),
            Success<TaggingRequestEntity> s => ProcessCancelAsync(s.Value)
        });
    }

    private async Task<Result<string>> ProcessCancelAsync(TaggingRequestEntity entity)
    {
        entity.Status = TradeStatus.Canceled;
        _ = await dbContext.SaveChangesAsync();
        return new Success<string>("契約をキャンセルしました。");
    }

    private async Task<Result<string>> ExecuteGratisAsync(GratisContractData contractData, string executorUserId)
    {
        var contract = contractData.Entity;
        
        var assetProcessResult = await (contract.ConsumedRightAsset switch
        {
            not null => UpdateConsumedAsset(contract.ConsumedRightAsset, contract.ConsumedRightAssetId!.Value),
            null => CreateNewRightAsset(contract.TagOwnerUserId, contract.RequestedTagId)
        });

        var consumedAssetId = assetProcessResult switch
        {
            Success<int> s => s.Value,
            Failure f => throw new UnreachableException("RightAsset processing cannot fail here")
        };

        Tag? requestedTag = contract.RequestedTag ?? await dbContext.Tags!.FindAsync(contract.RequestedTagId);
        
        return await (requestedTag switch
        {
            null => Task.FromResult<Result<string>>(new Failure("Tag not found")),
            var tag => ProcessGratisTagActionAsync(contract, tag, consumedAssetId, executorUserId)
        });
    }

    private Task<Result<int>> UpdateConsumedAsset(RightAsset asset, int assetId)
    {
        asset.IsBurned = true;
        asset.BurnedAt = DateTime.UtcNow;
        _ = dbContext.RightAssets!.Update(asset);
        return Task.FromResult<Result<int>>(new Success<int>(assetId));
    }

    private async Task<Result<int>> CreateNewRightAsset(string ownerId, int targetTagId)
    {
        var rightAsset = new RightAsset
        {
            OwnerId = ownerId,
            TargetTagId = targetTagId,
            IsBurned = true,
            BurnedAt = DateTime.UtcNow
        };
        _ = dbContext.RightAssets!.Add(rightAsset);
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
        _ = dbContext.TagRelations!.Add(newRelation);
        _ = await dbContext.SaveChangesAsync();

        var previousWeightAdd = tag.CachedWeight;
        tag.CachedWeight += contract.ProposedWeight;
        var newWeightAdd = tag.CachedWeight;

        var ledgerAdd = new TagWeightLedger
        {
            TagId = contract.RequestedTagId,
            TagNameSnapshot = tag.Name,
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
        _ = dbContext.TagWeightLedgers!.Add(ledgerAdd);

        _ = dbContext.TimelineEvents!.Add(new TimelineEvent
        {
            OwnerId = executorUserId,
            TargetType = "Item",
            TargetItemId = contract.TargetItemId,
            FollowedTagId = contract.RequestedTagId,
            EventType = "Insert",
            NewWeight = contract.ProposedWeight
        });
        
        return new Success<string>("タグを追加しました。");
    }

    private async Task<Result<string>> ProcessGratisRemoveOrDecreaseAsync(TaggingRequestEntity contract, Tag tag, int consumedAssetId, string executorUserId, bool isRemove)
    {
        TagRelation? relation = await dbContext.TagRelations!
            .FirstOrDefaultAsync(tr => tr.ItemId == contract.TargetItemId && tr.TagId == contract.RequestedTagId);

        return await (relation switch
        {
            null => Task.FromResult<Result<string>>(new Failure("対象のタグ付けが見つかりません。")),
            var r => ProcessRelationDecreaseAsync(contract, tag, r, consumedAssetId, executorUserId, isRemove)
        });
    }

    private Task<Result<string>> ProcessRelationDecreaseAsync(TaggingRequestEntity contract, Tag tag, TagRelation relation, int consumedAssetId, string executorUserId, bool isRemove)
    {
        int prevWeight = relation.Weight;
        int delta = isRemove ? -prevWeight : -contract.ProposedWeight;

        _ = (isRemove || prevWeight + delta <= 0) switch
        {
            true => (object)dbContext.TagRelations!.Remove(relation),
            false => (object)(relation.Weight += delta)
        };

        int previousTagWeight = tag.CachedWeight;
        tag.CachedWeight += delta;

        _ = dbContext.TagWeightLedgers!.Add(new TagWeightLedger
        {
            TagId = contract.RequestedTagId,
            TagNameSnapshot = tag.Name,
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

        _ = dbContext.TimelineEvents!.Add(new TimelineEvent
        {
            OwnerId = executorUserId,
            TargetType = "Item",
            TargetItemId = contract.TargetItemId,
            FollowedTagId = contract.RequestedTagId,
            EventType = "Update",
            NewWeight = contract.ProposedWeight
        });
        
        return Task.FromResult<Result<string>>(new Success<string>("タグを削除または削減しました。"));
    }

    private async Task<Result<string>> ExecuteMutualAsync(MutualContractData contractData, string executorUserId)
    {
        var contract = contractData.Entity;

        Result<RightAsset> assetValidation = contract.ConsumedRightAsset switch
        {
            null => new Failure("相互タグ付けには対価のアセットが必要です。"),
            _ => new Success<RightAsset>(contract.ConsumedRightAsset)
        };

        return await (assetValidation switch
        {
            Failure f => Task.FromResult<Result<string>>(f),
            Success<RightAsset> s => ProcessMutualWithAssetAsync(contract, s.Value, executorUserId)
        });
    }

    private async Task<Result<string>> ProcessMutualWithAssetAsync(TaggingRequestEntity contract, RightAsset consumedAsset, string executorUserId)
    {
        var requesterAssetId = contract.ConsumedRightAssetId!.Value;
        consumedAsset.IsBurned = true;
        consumedAsset.BurnedAt = DateTime.UtcNow;
        _ = dbContext.RightAssets!.Update(consumedAsset);

        var offeredTagAsset = new RightAsset
        {
            OwnerId = contract.RequesterUserId,
            TargetTagId = contract.OfferedTagId!.Value,
            IsBurned = true,
            BurnedAt = DateTime.UtcNow
        };
        _ = dbContext.RightAssets!.Add(offeredTagAsset);

        Tag? requestedTag = contract.RequestedTag ?? await dbContext.Tags!.FindAsync(contract.RequestedTagId);
        Tag? offeredTag = await dbContext.Tags!.FindAsync(contract.OfferedTagId);

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
        var processResult = await (contract.RequestType switch
        {
            TaggingRequestType.Add => ProcessMutualAddAsync(contract, requestedTag, requesterAssetId, executorUserId),
            TaggingRequestType.Remove => ProcessMutualRemoveAsync(contract, requestedTag, requesterAssetId, executorUserId),
            _ => Task.FromResult<Result<string>>(new Failure("無効なリクエストタイプです。"))
        });

        return await (processResult switch
        {
            Failure f => Task.FromResult<Result<string>>(f),
            Success<string> _ => ProcessMutualOfferedAsync(contract, offeredTag, offeredTagAsset, executorUserId)
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
        _ = dbContext.TagRelations!.Add(relation1);
        _ = await dbContext.SaveChangesAsync();

        var prevReqWeight = requestedTag.CachedWeight;
        requestedTag.CachedWeight += contract.ProposedWeight;
        var newReqWeight = requestedTag.CachedWeight;

        var ledger1 = new TagWeightLedger
        {
            TagId = contract.RequestedTagId,
            TagNameSnapshot = requestedTag.Name,
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
        _ = dbContext.TagWeightLedgers!.Add(ledger1);

        _ = dbContext.TimelineEvents!.Add(new TimelineEvent
        {
            OwnerId = executorUserId,
            TargetType = "Item",
            TargetItemId = contract.TargetItemId,
            FollowedTagId = contract.RequestedTagId,
            EventType = "Insert",
            NewWeight = contract.ProposedWeight
        });
        
        return new Success<string>("追加完了");
    }

    private async Task<Result<string>> ProcessMutualRemoveAsync(TaggingRequestEntity contract, Tag requestedTag, int requesterAssetId, string executorUserId)
    {
        TagRelation? relation1Remove = await dbContext.TagRelations!
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
        _ = dbContext.TagRelations!.Remove(relation);

        var prevReqWeightRem = requestedTag.CachedWeight;
        requestedTag.CachedWeight -= prevWeight;
        var newReqWeightRem = requestedTag.CachedWeight;

        var ledger1Rem = new TagWeightLedger
        {
            TagId = contract.RequestedTagId,
            TagNameSnapshot = requestedTag.Name,
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
        _ = dbContext.TagWeightLedgers!.Add(ledger1Rem);

        _ = dbContext.TimelineEvents!.Add(new TimelineEvent
        {
            OwnerId = executorUserId,
            TargetType = "Item",
            TargetItemId = contract.TargetItemId,
            FollowedTagId = contract.RequestedTagId,
            EventType = "Delete",
            PreviousWeight = prevWeight
        });
        
        return new Success<string>("削除完了");
    }

    private async Task<Result<string>> ProcessMutualOfferedAsync(TaggingRequestEntity contract, Tag offeredTag, RightAsset offeredTagAsset, string executorUserId)
    {
        var relation2 = new TagRelation
        {
            ItemId = contract.OfferedTargetItemId!.Value,
            TagId = contract.OfferedTagId!.Value,
            Weight = contract.ProposedWeight,
            OwnerId = contract.TagOwnerUserId
        };
        _ = dbContext.TagRelations!.Add(relation2);
        _ = await dbContext.SaveChangesAsync();

        var prevOffWeight = offeredTag.CachedWeight;
        offeredTag.CachedWeight += contract.ProposedWeight;
        var newOffWeight = offeredTag.CachedWeight;

        var ledger2 = new TagWeightLedger
        {
            TagId = contract.OfferedTagId!.Value,
            TagNameSnapshot = offeredTag.Name,
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
        _ = dbContext.TagWeightLedgers!.Add(ledger2);

        _ = dbContext.TimelineEvents!.Add(new TimelineEvent
        {
            OwnerId = executorUserId,
            TargetType = "Item",
            TargetItemId = contract.OfferedTargetItemId!.Value,
            FollowedTagId = contract.OfferedTagId!.Value,
            EventType = "Insert",
            NewWeight = contract.ProposedWeight
        });
        
        return new Success<string>("相互タグ付けが完了しました。");
    }

    private async Task<Result<string>> ExecuteTriggerAsync(TriggerContractData contractData, string executorUserId)
    {
        var contract = contractData.Entity;
        
        PublicTradeOffer? offer = await dbContext.PublicTradeOffers!
                                     .FirstOrDefaultAsync(o => o.Id == contract.TargetPublicTradeOfferId);

        Result<PublicTradeOffer> offerResult = offer switch
        {
            null => new Failure("公開オファーが見つかりません。"),
            { IsActive: false } => new Failure("この公開オファーは現在有効ではありません。"),
            var o => new Success<PublicTradeOffer>(o)
        };

        return await (offerResult switch
        {
            Failure f => Task.FromResult<Result<string>>(f),
            Success<PublicTradeOffer> s => ProcessTriggerWithOfferAsync(contract, s.Value, executorUserId)
        });
    }
    
    private async Task<Result<string>> ProcessTriggerWithOfferAsync(TaggingRequestEntity contract, PublicTradeOffer offer, string executorUserId)
    {
        var assetValidation = (offer.RequiredAssetAmount > 0) switch
        {
            true => ValidateTriggerAsset(contract, offer),
            false => new Success<RightAsset?>(null)
        };
        
        return await (assetValidation switch
        {
            Failure f => Task.FromResult<Result<string>>(f),
            Success<RightAsset?> s => ProcessTriggerAssetActionAsync(contract, offer, s.Value, executorUserId)
        });
    }

    private Result<RightAsset?> ValidateTriggerAsset(TaggingRequestEntity contract, PublicTradeOffer offer)
    {
        return (contract.ConsumedRightAsset) switch
        {
            null => new Failure("提供された RightAsset の量が不足しています。"),
            var a when a.Amount < offer.RequiredAssetAmount => new Failure("提供された RightAsset の量が不足しています。"),
            var a when a.TargetTagId != offer.OfferedTagId => new Failure("提供された RightAsset は対象のタグの権利ではありません。"),
            var a => new Success<RightAsset?>(a)
        };
    }
    
    private async Task<Result<string>> ProcessTriggerAssetActionAsync(TaggingRequestEntity contract, PublicTradeOffer offer, RightAsset? validatedAsset, string executorUserId)
    {
        var consumedAssetId = await (validatedAsset switch
        {
            not null => UpdateTriggerConsumedAssetAsync(validatedAsset, contract.ConsumedRightAssetId!.Value),
            null => CreateTriggerOwnerAssetAsync(offer)
        });

        return await (contract.RequestType switch
        {
            TaggingRequestType.Add => ProcessTriggerAddAsync(contract, offer, consumedAssetId),
            TaggingRequestType.Remove => ProcessTriggerRemoveAsync(contract, offer, consumedAssetId),
            _ => Task.FromResult<Result<string>>(new Failure("無効なリクエストタイプです。"))
        });
    }

    private Task<int> UpdateTriggerConsumedAssetAsync(RightAsset asset, int assetId)
    {
        asset.IsBurned = true;
        asset.BurnedAt = DateTime.UtcNow;
        _ = dbContext.RightAssets!.Update(asset);
        return Task.FromResult(assetId);
    }
    
    private async Task<int> CreateTriggerOwnerAssetAsync(PublicTradeOffer offer)
    {
        var ownerAsset = new RightAsset
        {
            OwnerId = offer.OwnerId,
            TargetTagId = offer.OfferedTagId,
            IsBurned = true,
            BurnedAt = DateTime.UtcNow
        };
        _ = dbContext.RightAssets!.Add(ownerAsset);
        _ = await dbContext.SaveChangesAsync();
        return ownerAsset.Id;
    }

    private async Task<Result<string>> ProcessTriggerAddAsync(TaggingRequestEntity contract, PublicTradeOffer offer, int consumedAssetId)
    {
        var newRelation = new TagRelation
        {
            ItemId = contract.TargetItemId,
            TagId = offer.OfferedTagId,
            Weight = contract.ProposedWeight,
            OwnerId = contract.RequesterUserId
        };
        _ = dbContext.TagRelations!.Add(newRelation);
        _ = await dbContext.SaveChangesAsync();

        Tag? tag = await dbContext.Tags!.FindAsync(offer.OfferedTagId);
        Result<Tag> tagResult = tag switch
        {
            null => new Failure("Tag not found"),
            _ => new Success<Tag>(tag)
        };
        
        return tagResult switch
        {
            Failure f => f,
            Success<Tag> s => CompleteTriggerAdd(contract, offer, consumedAssetId, s.Value, newRelation)
        };
    }
    
    private Result<string> CompleteTriggerAdd(TaggingRequestEntity contract, PublicTradeOffer offer, int consumedAssetId, Tag tag, TagRelation newRelation)
    {
        var prevWeight = tag.CachedWeight;
        tag.CachedWeight += contract.ProposedWeight;
        var newWeight = tag.CachedWeight;

        var ledger = new TagWeightLedger
        {
            TagId = offer.OfferedTagId,
            TagNameSnapshot = tag.Name,
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
        _ = dbContext.TagWeightLedgers!.Add(ledger);

        _ = dbContext.TimelineEvents!.Add(new TimelineEvent
        {
            OwnerId = contract.RequesterUserId,
            TargetType = "Item",
            TargetItemId = contract.TargetItemId,
            FollowedTagId = offer.OfferedTagId,
            EventType = "Insert",
            NewWeight = contract.ProposedWeight
        });
        
        return new Success<string>("公開オファーを実行しました。");
    }
    
    private async Task<Result<string>> ProcessTriggerRemoveAsync(TaggingRequestEntity contract, PublicTradeOffer offer, int consumedAssetId)
    {
        TagRelation? relation = await dbContext.TagRelations!
            .FirstOrDefaultAsync(tr => tr.ItemId == contract.TargetItemId && tr.TagId == offer.OfferedTagId);

        return await (relation switch
        {
            null => Task.FromResult<Result<string>>(new Success<string>("削除対象が存在しません。")),
            var r => ProcessTriggerRemoveRelationAsync(contract, offer, consumedAssetId, r)
        });
    }

    private async Task<Result<string>> ProcessTriggerRemoveRelationAsync(TaggingRequestEntity contract, PublicTradeOffer offer, int consumedAssetId, TagRelation relation)
    {
        var prevWeight = relation.Weight;
        _ = dbContext.TagRelations!.Remove(relation);

        Tag? tag = await dbContext.Tags!.FindAsync(offer.OfferedTagId);
        Result<Tag> tagResult = tag switch
        {
            null => new Failure("Tag not found"),
            _ => new Success<Tag>(tag)
        };
        
        return tagResult switch
        {
            Failure f => f,
            Success<Tag> s => CompleteTriggerRemove(contract, offer, consumedAssetId, s.Value, relation, prevWeight)
        };
    }
    
    private Result<string> CompleteTriggerRemove(TaggingRequestEntity contract, PublicTradeOffer offer, int consumedAssetId, Tag tag, TagRelation relation, int prevWeight)
    {
        var previousWeight = tag.CachedWeight;
        tag.CachedWeight -= prevWeight;
        var newWeight = tag.CachedWeight;

        var ledger = new TagWeightLedger
        {
            TagId = offer.OfferedTagId,
            TagNameSnapshot = tag.Name,
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
        _ = dbContext.TagWeightLedgers!.Add(ledger);

        _ = dbContext.TimelineEvents!.Add(new TimelineEvent
        {
            OwnerId = contract.RequesterUserId,
            TargetType = "Item",
            TargetItemId = contract.TargetItemId,
            FollowedTagId = offer.OfferedTagId,
            EventType = "Delete",
            PreviousWeight = prevWeight
        });
        
        return new Success<string>("公開オファーをキャンセル(Remove)しました。");
    }

    private async Task<Result<string>> ExecuteBountyAsync(BountyContractData contractData, string fulfillerUserId, int? fulfillerAssetId)
    {
        var contract = contractData.Entity;
        var assetProvider = await ResolveBountyAssetProviderAsync(contract, fulfillerUserId, fulfillerAssetId);

        return await (assetProvider switch
        {
            Failure f => Task.FromResult<Result<string>>(f),
            Success<int> s => ProcessBountyRequestAsync(contract, fulfillerUserId, s.Value)
        });
    }
    
    private async Task<Result<int>> ResolveBountyAssetProviderAsync(TaggingRequestEntity contract, string fulfillerUserId, int? fulfillerAssetId)
    {
        var state = (fulfillerAssetId.HasValue, contract.RequestedTag!.OwnerId == fulfillerUserId) switch
        {
            (true, _) => await VerifyAndConsumeFulfillerAssetAsync(fulfillerAssetId!.Value, fulfillerUserId, contract.RequestedTagId),
            (false, true) => await MintAndConsumeGoodwillAssetAsync(fulfillerUserId, contract.RequestedTagId),
            (false, false) => new Failure("バウンティを承認するには対象タグの RightAsset が必要です。")
        };
        return state;
    }

    private async Task<Result<int>> VerifyAndConsumeFulfillerAssetAsync(int fulfillerAssetId, string fulfillerUserId, int requestedTagId)
    {
        RightAsset? fulfillerAsset = await dbContext.RightAssets!
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
        asset.BurnedAt = DateTime.UtcNow;
        _ = dbContext.RightAssets!.Update(asset);
        return new Success<int>(asset.Id);
    }
    
    private async Task<Result<int>> MintAndConsumeGoodwillAssetAsync(string fulfillerUserId, int requestedTagId)
    {
        var rightAsset = new RightAsset
        {
            OwnerId = fulfillerUserId,
            TargetTagId = requestedTagId,
            IsBurned = true,
            BurnedAt = DateTime.UtcNow
        };
        _ = dbContext.RightAssets!.Add(rightAsset);
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
        _ = dbContext.TagRelations!.Add(newRelation);
        _ = await dbContext.SaveChangesAsync();

        var rewardResult = await TransferBountyRewardAsync(contract, fulfillerUserId);
        
        return await (rewardResult switch
        {
            Failure f => Task.FromResult<Result<string>>(f),
            Success<bool> _ => ProcessBountyAddTagLedgerAsync(contract, fulfillerUserId, consumedAssetId, newRelation)
        });
    }

    private async Task<Result<string>> ProcessBountyAddTagLedgerAsync(TaggingRequestEntity contract, string fulfillerUserId, int consumedAssetId, TagRelation newRelation)
    {
        Tag? tag = contract.RequestedTag ?? await dbContext.Tags!.FindAsync(contract.RequestedTagId);
        
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
            SourceType = "TagRelation",
            SourceId = newRelation.Id,
            ConsumedRightAssetId = consumedAssetId,
            Delta = contract.ProposedWeight,
            PreviousWeight = previousWeight,
            NewWeight = newWeight,
            IsOwnerAction = contract.RequestedTag!.OwnerId == fulfillerUserId,
            Reason = contract.OfferedRewardAssetId.HasValue ? "Reward Bounty Fulfilled" : "Goodwill Bounty Fulfilled",
            OwnerId = fulfillerUserId
        };
        _ = dbContext.TagWeightLedgers!.Add(ledger);

        _ = dbContext.TimelineEvents!.Add(new TimelineEvent
        {
            OwnerId = fulfillerUserId,
            TargetType = "Item",
            TargetItemId = contract.TargetItemId,
            FollowedTagId = contract.RequestedTagId,
            EventType = "Insert",
            NewWeight = contract.ProposedWeight
        });
        
        return new Success<string>("バウンティリクエストを承認しました。");
    }

    private async Task<Result<bool>> TransferBountyRewardAsync(TaggingRequestEntity contract, string fulfillerUserId)
    {
        var actionState = contract.OfferedRewardAssetId.HasValue switch
        {
            false => new Success<bool>(true),
            true => await ProcessRewardTransferAsync(contract, fulfillerUserId)
        };
        return actionState;
    }
    
    private async Task<Result<bool>> ProcessRewardTransferAsync(TaggingRequestEntity contract, string fulfillerUserId)
    {
        RightAsset? rewardAsset = await dbContext.RightAssets!
            .FirstOrDefaultAsync(a => a.Id == contract.OfferedRewardAssetId!.Value && a.OwnerId == contract.RequesterUserId);

        Result<bool> authResult = (rewardAsset?.IsBurned == false) switch
        {
            true => CompleteRewardTransfer(rewardAsset!, fulfillerUserId),
            false => new Failure("約束された報酬アセットが無効になっています。")
        };
        return authResult;
    }
    
    private Result<bool> CompleteRewardTransfer(RightAsset rewardAsset, string fulfillerUserId)
    {
        rewardAsset.OwnerId = fulfillerUserId;
        _ = dbContext.RightAssets!.Update(rewardAsset);
        return new Success<bool>(true);
    }
    
    private async Task<Result<string>> ProcessBountyRemoveAsync(TaggingRequestEntity contract, string fulfillerUserId, int consumedAssetId)
    {
        TagRelation? relation = await dbContext.TagRelations!
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
        _ = dbContext.TagRelations!.Remove(relation);

        var rewardResult = await TransferBountyRewardAsync(contract, fulfillerUserId);
        
        return await (rewardResult switch
        {
            Failure f => Task.FromResult<Result<string>>(f),
            Success<bool> _ => ProcessBountyRemoveTagLedgerAsync(contract, fulfillerUserId, consumedAssetId, relation, prevWeight)
        });
    }

    private async Task<Result<string>> ProcessBountyRemoveTagLedgerAsync(TaggingRequestEntity contract, string fulfillerUserId, int consumedAssetId, TagRelation relation, int prevWeight)
    {
        Tag? tag = contract.RequestedTag ?? await dbContext.Tags!.FindAsync(contract.RequestedTagId);
        
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
            SourceType = "TagRelation",
            SourceId = relation.Id,
            ConsumedRightAssetId = consumedAssetId,
            Delta = -prevWeight,
            PreviousWeight = previousWeight,
            NewWeight = newWeight,
            IsOwnerAction = contract.RequestedTag!.OwnerId == fulfillerUserId,
            Reason = contract.OfferedRewardAssetId.HasValue ? "Reward Bounty Fulfilled (Remove)" : "Goodwill Bounty Fulfilled (Remove)",
            OwnerId = fulfillerUserId
        };
        _ = dbContext.TagWeightLedgers!.Add(ledger);

        _ = dbContext.TimelineEvents!.Add(new TimelineEvent
        {
            OwnerId = fulfillerUserId,
            TargetType = "Item",
            TargetItemId = contract.TargetItemId,
            FollowedTagId = contract.RequestedTagId,
            EventType = "Delete",
            PreviousWeight = prevWeight
        });
        
        return new Success<string>("バウンティリクエスト(削除)を承認しました。");
    }
}