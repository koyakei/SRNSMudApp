using System.Diagnostics;

using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Models.Unions;

#pragma warning disable CA1508
#pragma warning disable IDE0010, IDE0072

namespace SRNSMudApp.Services.Contracts;

/// <summary>
///     Gratis（無償タグ付け）コントラクトの承認・実行処理を担当する <see cref="IContractExecutor" /> 実装。
///     タグオーナーが RightAsset を発行・消費し、対象アイテムへタグを付与または解除する。
/// </summary>
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
            null => Task.FromResult<Result<string>>(new Failure(ContractMessages.TagNotFound)),
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
        TaggableTarget? target = contract.Target ?? await dbContext.TaggableTargets
            .Include(t => t.Item)
            .Include(t => t.TagEdge)
            .FirstOrDefaultAsync(t => t.Id == contract.TargetId);

        if (target?.TargetType == "TagEdge" || target?.TagEdge != null)
        {
            int edgeId = target.TagEdge?.Id ?? await dbContext.TagEdges.Where(e => e.TagTargetId == target.Id).Select(e => e.Id).FirstOrDefaultAsync();
            return await (contract.RequestType switch
            {
                TaggingRequestType.Add => ProcessGratisAddEdgeAsync(contract, tag, edgeId, consumedAssetId, executorUserId),
                TaggingRequestType.Remove => ProcessGratisRemoveOrDecreaseEdgeAsync(contract, tag, edgeId, consumedAssetId, executorUserId, true),
                TaggingRequestType.DecreaseWeight => ProcessGratisRemoveOrDecreaseEdgeAsync(contract, tag, edgeId, consumedAssetId, executorUserId, false),
                _ => Task.FromResult<Result<string>>(new Failure(ContractMessages.InvalidRequestType))
            });
        }

        return await (contract.RequestType switch
        {
            TaggingRequestType.Add => ProcessGratisAddAsync(contract, tag, consumedAssetId, executorUserId),
            TaggingRequestType.Remove => ProcessGratisRemoveOrDecreaseAsync(contract, tag, consumedAssetId, executorUserId, true),
            TaggingRequestType.DecreaseWeight => ProcessGratisRemoveOrDecreaseAsync(contract, tag, consumedAssetId, executorUserId, false),
            _ => Task.FromResult<Result<string>>(new Failure(ContractMessages.InvalidRequestType))
        });
    }

    private async Task<Result<string>> ProcessGratisAddEdgeAsync(TaggingRequestEntity contract, Tag tag, int edgeId, int consumedAssetId, string executorUserId)
    {
        var newAttachment = new TagEdgeTagAttachment
        {
            TagEdgeId = edgeId,
            TagId = contract.RequestedTagId,
            Weight = contract.ProposedWeight,
            ConsumedRightAssetId = consumedAssetId,
            OwnerId = contract.RequesterUserId
        };
        dbContext.TagEdgeTagAttachments.Add(newAttachment);
        await dbContext.SaveChangesAsync();

        var previousWeight = tag.CachedWeight;
        tag.CachedWeight += contract.ProposedWeight;
        var newWeight = tag.CachedWeight;

        var ledger = new TagWeightLedger
        {
            TagId = contract.RequestedTagId,
            TagNameSnapshot = tag.Name,
            SourceType = "TagEdgeTagAttachmentInsert",
            SourceId = null,
            ConsumedRightAssetId = consumedAssetId,
            Delta = contract.ProposedWeight,
            PreviousWeight = previousWeight,
            NewWeight = newWeight,
            IsOwnerAction = true,
            Reason = "Gratis TagEdge Contract Accepted",
            OwnerId = executorUserId
        };
        dbContext.TagWeightLedgers.Add(ledger);
        await dbContext.SaveChangesAsync();

        return new Success<string>(ContractMessages.TagEdgeTagAttached);
    }

    private async Task<Result<string>> ProcessGratisRemoveOrDecreaseEdgeAsync(TaggingRequestEntity contract, Tag tag, int edgeId, int consumedAssetId, string executorUserId, bool isRemove)
    {
        var attachment = await dbContext.TagEdgeTagAttachments
            .FirstOrDefaultAsync(a => a.TagEdgeId == edgeId && a.TagId == contract.RequestedTagId);

        if (attachment == null)
        {
            return new Failure(ContractMessages.TagEdgeAttachmentNotFound);
        }

        var prevWeight = attachment.Weight;
        var delta = isRemove ? -prevWeight : -contract.ProposedWeight;

        if (isRemove || prevWeight + delta <= 0)
        {
            dbContext.TagEdgeTagAttachments.Remove(attachment);
        }
        else
        {
            attachment.Weight += delta;
        }

        var previousWeight = tag.CachedWeight;
        tag.CachedWeight += delta;
        var newWeight = tag.CachedWeight;

        var ledger = new TagWeightLedger
        {
            TagId = contract.RequestedTagId,
            TagNameSnapshot = tag.Name,
            SourceType = isRemove || prevWeight + delta <= 0 ? "TagEdgeTagAttachmentDelete" : "TagEdgeTagAttachmentUpdate",
            SourceId = null,
            ConsumedRightAssetId = null,
            Delta = delta,
            PreviousWeight = previousWeight,
            NewWeight = newWeight,
            IsOwnerAction = true,
            Reason = $"Gratis TagEdge Contract ({(isRemove ? "Remove" : "Decrease Weight")})",
            OwnerId = executorUserId
        };
        dbContext.TagWeightLedgers.Add(ledger);
        await dbContext.SaveChangesAsync();

        return new Success<string>(ContractMessages.TagEdgeTagDetachedOrDecreased);
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
            null => Task.FromResult<Result<string>>(new Failure(ContractMessages.TagRelationNotFound)),
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