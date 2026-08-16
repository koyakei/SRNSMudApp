using System.Diagnostics;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using SRNSMudApp.Data;

namespace SRNSMudApp.Services;

public class TaggingContractService(ApplicationDbContext dbContext)
{

    public async Task<GratisTaggingContract> ProposeGratisContractAsync(
        string requesterUserId,
        string tagOwnerUserId,
        int targetItemId,
        int requestedTagId,
        TaggingRequestType requestType = TaggingRequestType.Add,
        string? message = null)
    {
        var requestItem = new Item
        {
            OwnerId = requesterUserId,
            Content = message ?? (requestType == TaggingRequestType.Add ? "タグ追加リクエストを送信しました。" : "タグ削除リクエストを送信しました。")
        };

        var contract = new GratisTaggingContract
        {
            OwnerId = requesterUserId,
            RequesterUserId = requesterUserId,
            TagOwnerUserId = tagOwnerUserId,
            TargetItemId = targetItemId,
            RequestedTagId = requestedTagId,
            Status = TradeStatus.Proposed,
            RequestType = requestType,
            RequesterMessage = message,
            RequestItem = requestItem
        };

        _ = dbContext.TaggingRequestEntities!.Add(contract);
        _ = await dbContext.SaveChangesAsync();
        return contract;
    }

    public async Task<MutualTaggingContract> ProposeMutualContractAsync(
        string requesterUserId,
        string tagOwnerUserId,
        int targetItemId,
        int requestedTagId,
        int offeredTargetItemId,
        int offeredTagId,
        int consumedRightAssetId,
        TaggingRequestType requestType = TaggingRequestType.Add)
    {
        var requestItem = new Item
        {
            OwnerId = requesterUserId,
            Content = requestType == TaggingRequestType.Add ? "タグ追加リクエスト(Mutual)を送信しました。" : "タグ削除リクエスト(Mutual)を送信しました。"
        };

        var contract = new MutualTaggingContract
        {
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
            RequestItem = requestItem
        };
        _ = dbContext.TaggingRequestEntities!.Add(contract);
        _ = await dbContext.SaveChangesAsync();
        return contract;
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

    public async Task AcceptContractAsync(int contractId, string currentUserId, int? fulfillerAssetId = null)
    {
        // 1. EF Core からはベースエンティティとして取得
        TaggingRequestEntity entity = await dbContext.TaggingRequestEntities!
            .Include(c => c.RequestedTag)
            .Include(c => c.ConsumedRightAsset)
            .FirstOrDefaultAsync(c => c.Id == contractId)
            ?? throw new InvalidOperationException("契約が見つかりません。");

        if (entity.Status != TradeStatus.Proposed)
        {
            throw new InvalidOperationException("実行・承認できない状態の契約です。");
        }

        if (entity is PublicOfferTriggerContract)
        {
            if (entity.RequesterUserId != currentUserId)
            {
                throw new InvalidOperationException("実行できない契約です。");
            }
        }
        else if (entity is BountyTaggingContract)
        {
            // Anyone can fulfill a Bounty (except maybe we could restrict it if we wanted, but by design anyone can)
            // But they MUST have a valid asset to burn, which is checked in ExecuteBountyAsync
        }
        else
        {
            if (entity.TagOwnerUserId != currentUserId)
            {
                throw new InvalidOperationException("承認できない契約です。");
            }
        }

        await using IDbContextTransaction transaction = await dbContext.Database.BeginTransactionAsync();

        try
        {
            // 3. 【重要】パターンマッチングによる分岐
            switch (entity)
            {
                case GratisTaggingContract gratis:
                    await ExecuteGratisAsync(gratis, currentUserId);
                    break;

                case MutualTaggingContract mutual:
                    await ExecuteMutualAsync(mutual, currentUserId);
                    break;

                case PublicOfferTriggerContract trigger:
                    await ExecuteTriggerAsync(trigger);
                    break;

                case BountyTaggingContract bounty:
                    await ExecuteBountyAsync(bounty, currentUserId, fulfillerAssetId);
                    break;

                default:
                    throw new UnreachableException("DBに未知の契約型が存在します。");
            }

            // 4. トランザクション完了
            entity.Status = TradeStatus.Executed;
            _ = await dbContext.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task CancelContractAsync(int contractId, string currentUserId)
    {
        TaggingRequestEntity entity = await dbContext.TaggingRequestEntities!
            .FirstOrDefaultAsync(c => c.Id == contractId)
            ?? throw new InvalidOperationException("契約が見つかりません。");

        if (entity.Status != TradeStatus.Proposed)
        {
            throw new InvalidOperationException("この状態の契約はキャンセルできません。");
        }

        // Requester or TagOwner can cancel/reject
        if (entity.RequesterUserId != currentUserId && entity.TagOwnerUserId != currentUserId)
        {
            throw new InvalidOperationException("この契約をキャンセル・拒否する権限がありません。");
        }

        entity.Status = TradeStatus.Canceled;
        _ = await dbContext.SaveChangesAsync();
    }

    private async Task ExecuteGratisAsync(GratisTaggingContract contract, string executorUserId)
    {
        int consumedAssetId;

        // 1. Burn Requester's RightAsset (If one is provided)
        if (contract.ConsumedRightAsset != null)
        {
            consumedAssetId = contract.ConsumedRightAssetId!.Value;
            contract.ConsumedRightAsset.IsBurned = true;
            contract.ConsumedRightAsset.BurnedAt = DateTime.UtcNow;
            _ = dbContext.RightAssets!.Update(contract.ConsumedRightAsset);
        }
        else
        {
            // TagOwner (executor) mints and burns a RightAsset on behalf of the Requester
            var rightAsset = new RightAsset
            {
                OwnerId = contract.TagOwnerUserId,
                TargetTagId = contract.RequestedTagId,
                IsBurned = true,
                BurnedAt = DateTime.UtcNow
            };
            _ = dbContext.RightAssets!.Add(rightAsset);
            _ = await dbContext.SaveChangesAsync(); // get ID
            consumedAssetId = rightAsset.Id;
        }

        Tag tag = (contract.RequestedTag ?? await dbContext.Tags!.FindAsync(contract.RequestedTagId)) ?? throw new InvalidOperationException("Tag not found");

        if (contract.RequestType == TaggingRequestType.Add)
        {
            var newRelation = new TagRelation
            {
                ItemId = contract.TargetItemId,
                TagId = contract.RequestedTagId,
                Weight = 1,
                OwnerId = contract.RequesterUserId,
            };
            _ = dbContext.TagRelations!.Add(newRelation);
            _ = await dbContext.SaveChangesAsync();

            var previousWeight = tag.CachedWeight;
            tag.CachedWeight += 1;
            var newWeight = tag.CachedWeight;

            var ledger = new TagWeightLedger
            {
                TagId = contract.RequestedTagId,
                TagNameSnapshot = tag.Name,
                SourceType = "TagRelation",
                SourceId = newRelation.Id,
                ConsumedRightAssetId = consumedAssetId,
                Delta = 1,
                PreviousWeight = previousWeight,
                NewWeight = newWeight,
                IsOwnerAction = true, 
                Reason = "Gratis Tagging Contract Accepted",
                OwnerId = executorUserId
            };
            _ = dbContext.TagWeightLedgers!.Add(ledger);

            _ = dbContext.TimelineEvents!.Add(new TimelineEvent
            {
                OwnerId = executorUserId,
                TargetType = "Item",
                TargetItemId = contract.TargetItemId,
                FollowedTagId = contract.RequestedTagId,
                EventType = "Insert",
                NewWeight = 1
            });
        }
        else if (contract.RequestType == TaggingRequestType.Remove || contract.RequestType == TaggingRequestType.DecreaseWeight)
        {
            var relation = await dbContext.TagRelations!
                .FirstOrDefaultAsync(tr => tr.ItemId == contract.TargetItemId && tr.TagId == contract.RequestedTagId);

            if (relation != null)
            {
                var prevWeight = relation.Weight;
                var delta = contract.RequestType == TaggingRequestType.Remove ? -prevWeight : -1;
                
                if (contract.RequestType == TaggingRequestType.Remove || prevWeight + delta <= 0)
                {
                    dbContext.TagRelations.Remove(relation);
                }
                else
                {
                    relation.Weight += delta;
                }
                
                var previousWeight = tag.CachedWeight;
                tag.CachedWeight += delta;
                var newWeight = tag.CachedWeight;

                var ledger = new TagWeightLedger
                {
                    TagId = contract.RequestedTagId,
                    TagNameSnapshot = tag.Name,
                    SourceType = "TagRelation",
                    SourceId = relation.Id,
                    ConsumedRightAssetId = consumedAssetId,
                    Delta = delta,
                    PreviousWeight = previousWeight,
                    NewWeight = newWeight,
                    IsOwnerAction = true, 
                    Reason = $"Gratis Tagging Contract Accepted ({(contract.RequestType == TaggingRequestType.Remove ? "Remove" : "Decrease Weight")})",
                    OwnerId = executorUserId
                };
                _ = dbContext.TagWeightLedgers!.Add(ledger);

                _ = dbContext.TimelineEvents!.Add(new TimelineEvent
                {
                    OwnerId = executorUserId,
                    TargetType = "Item",
                    TargetItemId = contract.TargetItemId,
                    FollowedTagId = contract.RequestedTagId,
                    EventType = contract.RequestType == TaggingRequestType.Remove || prevWeight + delta <= 0 ? "Delete" : "Update",
                    PreviousWeight = prevWeight,
                    NewWeight = contract.RequestType == TaggingRequestType.Remove || prevWeight + delta <= 0 ? 0 : prevWeight + delta
                });
            }
        }
    }

    private async Task ExecuteMutualAsync(MutualTaggingContract contract, string executorUserId)
    {
        if (contract.ConsumedRightAsset == null)
        {
            throw new InvalidOperationException("相互タグ付けには対価のアセットが必要です。");
        }

        // 1. Burn Requester's RightAsset for RequestedTag
        var requesterAssetId = contract.ConsumedRightAssetId!.Value;
        contract.ConsumedRightAsset.IsBurned = true;
        contract.ConsumedRightAsset.BurnedAt = DateTime.UtcNow;
        _ = dbContext.RightAssets!.Update(contract.ConsumedRightAsset);

        // 2. Mint and burn TagOwner's RightAsset for OfferedTag (since Requester is offering their own tag)
        var offeredTagAsset = new RightAsset
        {
            OwnerId = contract.RequesterUserId,
            TargetTagId = contract.OfferedTagId,
            IsBurned = true,
            BurnedAt = DateTime.UtcNow
        };
        _ = dbContext.RightAssets!.Add(offeredTagAsset);

        Tag? requestedTag = contract.RequestedTag ?? await dbContext.Tags!.FindAsync(contract.RequestedTagId);
        Tag? offeredTag = await dbContext.Tags!.FindAsync(contract.OfferedTagId);

        if (requestedTag == null || offeredTag == null)
        {
            throw new InvalidOperationException("Tag not found");
        }

        if (contract.RequestType == TaggingRequestType.Add)
        {
            var relation1 = new TagRelation
            {
                ItemId = contract.TargetItemId,
                TagId = contract.RequestedTagId,
                Weight = 1,
                OwnerId = contract.RequesterUserId,
            };
            _ = dbContext.TagRelations!.Add(relation1);
            _ = await dbContext.SaveChangesAsync();

            var prevReqWeight = requestedTag.CachedWeight;
            requestedTag.CachedWeight += 1;
            var newReqWeight = requestedTag.CachedWeight;

            var ledger1 = new TagWeightLedger
            {
                TagId = contract.RequestedTagId,
                TagNameSnapshot = requestedTag.Name,
                SourceType = "TagRelation",
                SourceId = relation1.Id,
                ConsumedRightAssetId = requesterAssetId,
                Delta = 1,
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
                NewWeight = 1
            });
        }
        else if (contract.RequestType == TaggingRequestType.Remove)
        {
            var relation1 = await dbContext.TagRelations!
                .FirstOrDefaultAsync(tr => tr.ItemId == contract.TargetItemId && tr.TagId == contract.RequestedTagId);
            
            if (relation1 != null)
            {
                var prevWeight = relation1.Weight;
                dbContext.TagRelations.Remove(relation1);

                var prevReqWeight = requestedTag.CachedWeight;
                requestedTag.CachedWeight -= prevWeight;
                var newReqWeight = requestedTag.CachedWeight;

                var ledger1 = new TagWeightLedger
                {
                    TagId = contract.RequestedTagId,
                    TagNameSnapshot = requestedTag.Name,
                    SourceType = "TagRelation",
                    SourceId = relation1.Id,
                    ConsumedRightAssetId = requesterAssetId,
                    Delta = -prevWeight,
                    PreviousWeight = prevReqWeight,
                    NewWeight = newReqWeight,
                    IsOwnerAction = true, 
                    Reason = "Mutual Tagging Contract Accepted (Requested Remove)",
                    OwnerId = executorUserId
                };
                _ = dbContext.TagWeightLedgers!.Add(ledger1);

                _ = dbContext.TimelineEvents!.Add(new TimelineEvent
                {
                    OwnerId = executorUserId,
                    TargetType = "Item",
                    TargetItemId = contract.TargetItemId,
                    FollowedTagId = contract.RequestedTagId,
                    EventType = "Delete",
                    PreviousWeight = prevWeight
                });
            }
        }

        var relation2 = new TagRelation
        {
            ItemId = contract.OfferedTargetItemId,
            TagId = contract.OfferedTagId,
            Weight = 1,
            OwnerId = contract.TagOwnerUserId,
        };
        _ = dbContext.TagRelations!.Add(relation2);
        _ = await dbContext.SaveChangesAsync();

        var prevOffWeight = offeredTag.CachedWeight;
        offeredTag.CachedWeight += 1;
        var newOffWeight = offeredTag.CachedWeight;

        var ledger2 = new TagWeightLedger
        {
            TagId = contract.OfferedTagId,
            TagNameSnapshot = offeredTag.Name,
            SourceType = "TagRelation",
            SourceId = relation2.Id,
            ConsumedRightAssetId = offeredTagAsset.Id,
            Delta = 1,
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
            TargetItemId = contract.OfferedTargetItemId,
            FollowedTagId = contract.OfferedTagId,
            EventType = "Insert",
            NewWeight = 1
        });
    }

    private async Task ExecuteTriggerAsync(PublicOfferTriggerContract contract)
    {
        PublicTradeOffer offer = await dbContext.PublicTradeOffers!
            .FirstOrDefaultAsync(o => o.Id == contract.TargetPublicTradeOfferId)
            ?? throw new InvalidOperationException("公開オファーが見つかりません。");

        if (!offer.IsActive)
        {
            throw new InvalidOperationException("この公開オファーは現在有効ではありません。");
        }

        int consumedAssetId;
        if (offer.RequiredAssetAmount > 0)
        {
            if (contract.ConsumedRightAsset == null || contract.ConsumedRightAsset.Amount < offer.RequiredAssetAmount)
            {
                throw new InvalidOperationException("提供された RightAsset の量が不足しています。");
            }

            if (contract.ConsumedRightAsset.TargetTagId != offer.OfferedTagId)
            {
                throw new InvalidOperationException("提供された RightAsset は対象のタグの権利ではありません。");
            }

            consumedAssetId = contract.ConsumedRightAssetId!.Value;
            contract.ConsumedRightAsset.IsBurned = true;
            contract.ConsumedRightAsset.BurnedAt = DateTime.UtcNow;
            _ = dbContext.RightAssets!.Update(contract.ConsumedRightAsset);
        }
        else
        {
            // Owner gives it for free, mint & burn
            var ownerAsset = new RightAsset
            {
                OwnerId = offer.OwnerId,
                TargetTagId = offer.OfferedTagId,
                IsBurned = true,
                BurnedAt = DateTime.UtcNow
            };
            _ = dbContext.RightAssets!.Add(ownerAsset);
            _ = await dbContext.SaveChangesAsync();
            consumedAssetId = ownerAsset.Id;
        }

        if (contract.RequestType == TaggingRequestType.Add)
        {
            var newRelation = new TagRelation
            {
                ItemId = contract.TargetItemId,
                TagId = offer.OfferedTagId,
                Weight = 1,
                OwnerId = contract.RequesterUserId,
            };
            _ = dbContext.TagRelations!.Add(newRelation);
            _ = await dbContext.SaveChangesAsync();

            Tag tag = await dbContext.Tags!.FindAsync(offer.OfferedTagId) ?? throw new InvalidOperationException("Tag not found");
            var prevWeight = tag.CachedWeight;
            tag.CachedWeight += 1;
            var newWeight = tag.CachedWeight;

            var ledger = new TagWeightLedger
            {
                TagId = offer.OfferedTagId,
                TagNameSnapshot = tag.Name,
                SourceType = "TagRelation",
                SourceId = newRelation.Id,
                ConsumedRightAssetId = consumedAssetId,
                Delta = 1,
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
                NewWeight = 1
            });
        }
        else if (contract.RequestType == TaggingRequestType.Remove)
        {
            var relation = await dbContext.TagRelations!
                .FirstOrDefaultAsync(tr => tr.ItemId == contract.TargetItemId && tr.TagId == offer.OfferedTagId);
            
            if (relation != null)
            {
                var prevWeight = relation.Weight;
                dbContext.TagRelations.Remove(relation);
                
                Tag tag = await dbContext.Tags!.FindAsync(offer.OfferedTagId) ?? throw new InvalidOperationException("Tag not found");
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
            }
        }
    }

    private async Task ExecuteBountyAsync(BountyTaggingContract contract, string fulfillerUserId, int? fulfillerAssetId)
    {
        int consumedAssetId;

        // Fulfiller must provide an asset for the requested tag, OR Fulfiller is the tag owner
        if (fulfillerAssetId.HasValue)
        {
            RightAsset fulfillerAsset = await dbContext.RightAssets
                .FirstOrDefaultAsync(a => a.Id == fulfillerAssetId.Value && a.OwnerId == fulfillerUserId && !a.IsBurned) ?? throw new InvalidOperationException("提供されたアセットが無効または所有していません。");
            if (fulfillerAsset.TargetTagId != contract.RequestedTagId)
            {
                throw new InvalidOperationException("提供されたアセットは対象タグの権利ではありません。");
            }

            consumedAssetId = fulfillerAsset.Id;
            fulfillerAsset.IsBurned = true;
            fulfillerAsset.BurnedAt = DateTime.UtcNow;
            _ = dbContext.RightAssets.Update(fulfillerAsset);
        }
        else if (contract.RequestedTag.OwnerId == fulfillerUserId)
        {
            // Tag owner is fulfilling it out of goodwill, mint and burn
            var rightAsset = new RightAsset
            {
                OwnerId = fulfillerUserId,
                TargetTagId = contract.RequestedTagId,
                IsBurned = true,
                BurnedAt = DateTime.UtcNow
            };
            _ = dbContext.RightAssets.Add(rightAsset);
            _ = await dbContext.SaveChangesAsync(); // get ID
            consumedAssetId = rightAsset.Id;
        }
        else
        {
            throw new InvalidOperationException("バウンティを承認するには対象タグの RightAsset が必要です。");
        }

        if (contract.RequestType == TaggingRequestType.Add)
        {
            var newRelation = new TagRelation
            {
                ItemId = contract.TargetItemId,
                TagId = contract.RequestedTagId,
                Weight = 1,
                OwnerId = contract.RequesterUserId,
            };
            _ = dbContext.TagRelations.Add(newRelation);
            _ = await dbContext.SaveChangesAsync();

            // Handle Reward transfer
            if (contract.OfferedRewardAssetId.HasValue)
            {
                RightAsset? rewardAsset = await dbContext.RightAssets
                    .FirstOrDefaultAsync(a => a.Id == contract.OfferedRewardAssetId.Value && a.OwnerId == contract.RequesterUserId);

                if (rewardAsset?.IsBurned == false)
                {
                    // Transfer ownership
                    rewardAsset.OwnerId = fulfillerUserId;
                    _ = dbContext.RightAssets.Update(rewardAsset);
                }
                else
                {
                    // Edge case: Reward asset was burned or moved before contract was accepted.
                    throw new InvalidOperationException("約束された報酬アセットが無効になっています。");
                }
            }

            // Cache & Ledger Update
            Tag? tag = contract.RequestedTag ?? await dbContext.Tags.FindAsync(contract.RequestedTagId);
            var previousWeight = tag.CachedWeight;
            tag.CachedWeight += 1;
            var newWeight = tag.CachedWeight;

            var ledger = new TagWeightLedger
            {
                TagId = contract.RequestedTagId,
                TagNameSnapshot = tag.Name,
                SourceType = "TagRelation",
                SourceId = newRelation.Id,
                ConsumedRightAssetId = consumedAssetId,
                Delta = 1,
                PreviousWeight = previousWeight,
                NewWeight = newWeight,
                IsOwnerAction = contract.RequestedTag.OwnerId == fulfillerUserId, // if fulfiller is owner
                Reason = contract.OfferedRewardAssetId.HasValue ? "Reward Bounty Fulfilled" : "Goodwill Bounty Fulfilled",
                OwnerId = fulfillerUserId
            };
            _ = dbContext.TagWeightLedgers.Add(ledger);

            _ = dbContext.TimelineEvents.Add(new TimelineEvent
            {
                OwnerId = fulfillerUserId,
                TargetType = "Item",
                TargetItemId = contract.TargetItemId,
                FollowedTagId = contract.RequestedTagId,
                EventType = "Insert",
                NewWeight = 1
            });
        }
        else if (contract.RequestType == TaggingRequestType.Remove)
        {
            var relation = await dbContext.TagRelations
                .FirstOrDefaultAsync(tr => tr.ItemId == contract.TargetItemId && tr.TagId == contract.RequestedTagId);
            
            if (relation != null)
            {
                var prevWeight = relation.Weight;
                dbContext.TagRelations.Remove(relation);
                
                // Handle Reward transfer
                if (contract.OfferedRewardAssetId.HasValue)
                {
                    RightAsset? rewardAsset = await dbContext.RightAssets
                        .FirstOrDefaultAsync(a => a.Id == contract.OfferedRewardAssetId.Value && a.OwnerId == contract.RequesterUserId);

                    if (rewardAsset?.IsBurned == false)
                    {
                        // Transfer ownership
                        rewardAsset.OwnerId = fulfillerUserId;
                        _ = dbContext.RightAssets.Update(rewardAsset);
                    }
                    else
                    {
                        throw new InvalidOperationException("約束された報酬アセットが無効になっています。");
                    }
                }

                // Cache & Ledger Update
                Tag? tag = contract.RequestedTag ?? await dbContext.Tags.FindAsync(contract.RequestedTagId);
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
                    IsOwnerAction = contract.RequestedTag.OwnerId == fulfillerUserId,
                    Reason = contract.OfferedRewardAssetId.HasValue ? "Reward Bounty Fulfilled (Remove)" : "Goodwill Bounty Fulfilled (Remove)",
                    OwnerId = fulfillerUserId
                };
                _ = dbContext.TagWeightLedgers.Add(ledger);

                _ = dbContext.TimelineEvents.Add(new TimelineEvent
                {
                    OwnerId = fulfillerUserId,
                    TargetType = "Item",
                    TargetItemId = contract.TargetItemId,
                    FollowedTagId = contract.RequestedTagId,
                    EventType = "Delete",
                    PreviousWeight = prevWeight
                });
            }
        }
    }
}