using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;

#pragma warning disable CA1508
#pragma warning disable IDE0010, IDE0072

namespace SRNSMudApp.Services;

public class TagEdgeService(IDbContextFactory<ApplicationDbContext> dbFactory) : ITagEdgeService
{
    private const string LedgerSourceTypeInsert = "TagEdgeTagAttachmentInsert";
    private const string LedgerSourceTypeDelete = "TagEdgeTagAttachmentDelete";

    public async Task<Result<TagEdge>> CreateEdgeAsync(int sourceTagId, int targetTagId, string ownerId)
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();

        Tag? sourceTag = await context.Tags.FindAsync(sourceTagId);
        Tag? targetTag = await context.Tags.FindAsync(targetTagId);
        if (sourceTag is null || targetTag is null)
        {
            return new Failure("SourceTag または TargetTag が見つかりません。");
        }

        bool alreadyExists = await context.TagEdges
            .AnyAsync(e => e.OwnerId == ownerId && e.SourceTagId == sourceTagId && e.TargetTagId == targetTagId);
        if (alreadyExists)
        {
            return new Failure("同じ Source/Target の組み合わせの Edge が既に存在します。");
        }

        var edge = new TagEdge
        {
            SourceTagId = sourceTagId,
            TargetTagId = targetTagId,
            OwnerId = ownerId
        };
        _ = context.TagEdges.Add(edge);
        _ = await context.SaveChangesAsync();

        return new Success<TagEdge>(edge);
    }

    public async Task<Result<bool>> DeleteEdgeAsync(int edgeId, string ownerId)
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();

        TagEdge? edge = await context.TagEdges.FindAsync(edgeId);
        if (edge is null)
        {
            return new Failure("Edge が見つかりません。");
        }

        if (edge.OwnerId != ownerId)
        {
            return new Failure("Edge の作成者ではないため、削除する権限がありません。");
        }

        _ = context.TagEdges.Remove(edge);
        _ = await context.SaveChangesAsync();
        return new Success<bool>(true);
    }

    public async Task<Result<TagEdgeTagAttachment>> AttachTagToEdgeAsync(
        int edgeId, int tagId, int rightAssetId, string currentUserId, int weight = 1)
    {
        if (weight <= 0)
        {
            return new Failure("weight は 1 以上を指定してください。");
        }

        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();

        TagEdge? edge = await context.TagEdges.FindAsync(edgeId);
        if (edge is null)
        {
            return new Failure("Edge が見つかりません。");
        }

        Tag? tag = await context.Tags.FindAsync(tagId);
        if (tag is null)
        {
            return new Failure("紐付け対象のタグが見つかりません。");
        }

        bool alreadyAttached = await context.TagEdgeTagAttachments
            .AnyAsync(a => a.TagEdgeId == edgeId && a.TagId == tagId);
        if (alreadyAttached)
        {
            return new Failure("このタグは既に Edge に紐付けられています。");
        }

        RightAsset? rightAsset = await context.RightAssets.FindAsync(rightAssetId);
        Result<RightAsset> assetCheck = rightAsset switch
        {
            null => new Failure("指定された RightAsset が見つかりません。"),
            { OwnerId: var o } r when o != currentUserId => new Failure("指定された RightAsset を所有していません。"),
            { IsBurned: true } => new Failure("指定された RightAsset は既に消費済みです。"),
            { TargetTagId: var t } r2 when t != tagId => new Failure("指定された RightAsset は対象タグの権利ではありません。"),
            { Amount: <= 0 } => new Failure("指定された RightAsset の残量が不足しています。"),
            _ => new Success<RightAsset>(rightAsset)
        };

        return await (assetCheck switch
        {
            Failure f => Task.FromResult<Result<TagEdgeTagAttachment>>(f),
            Success<RightAsset> s => ExecuteAttachAsync(context, edge, tag, s.Value, currentUserId, weight)
        });
    }

    private static async Task<Result<TagEdgeTagAttachment>> ExecuteAttachAsync(
        ApplicationDbContext context, TagEdge edge, Tag tag, RightAsset rightAsset, string currentUserId, int weight)
    {
        await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync();
        try
        {
            rightAsset.Amount -= 1;
            if (rightAsset.Amount <= 0)
            {
                rightAsset.IsBurned = true;
                rightAsset.Status = new Burned(DateTime.UtcNow);
            }
            _ = context.RightAssets.Update(rightAsset);

            var attachment = new TagEdgeTagAttachment
            {
                TagEdgeId = edge.Id,
                TagId = tag.Id,
                Weight = weight,
                ConsumedRightAssetId = rightAsset.Id,
                OwnerId = currentUserId
            };
            _ = context.TagEdgeTagAttachments.Add(attachment);
            _ = await context.SaveChangesAsync();

            var previousWeight = tag.CachedWeight;
            tag.CachedWeight += weight;

            _ = context.TagWeightLedgers.Add(new TagWeightLedger
            {
                TagId = tag.Id,
                TagNameSnapshot = tag.Name,
                SourceType = LedgerSourceTypeInsert,
                SourceId = null,
                ConsumedRightAssetId = rightAsset.Id,
                Delta = weight,
                PreviousWeight = previousWeight,
                NewWeight = tag.CachedWeight,
                IsOwnerAction = tag.OwnerId == currentUserId,
                Reason = "Edgeへのタグ紐付け（RightAsset消費）",
                OwnerId = currentUserId
            });

            _ = await context.SaveChangesAsync();
            await transaction.CommitAsync();

            return new Success<TagEdgeTagAttachment>(attachment);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<Result<bool>> DetachTagFromEdgeAsync(int attachmentId, string currentUserId)
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();

        TagEdgeTagAttachment? attachment = await context.TagEdgeTagAttachments
            .Include(a => a.Tag)
            .FirstOrDefaultAsync(a => a.Id == attachmentId);
        if (attachment is null)
        {
            return new Failure("紐付けが見つかりません。");
        }

        if (attachment.OwnerId != currentUserId)
        {
            return new Failure("紐付けた本人ではないため、解除する権限がありません。");
        }

        Tag? tag = attachment.Tag ?? await context.Tags.FindAsync(attachment.TagId);
        if (tag is not null)
        {
            var previousWeight = tag.CachedWeight;
            tag.CachedWeight -= attachment.Weight;

            _ = context.TagWeightLedgers.Add(new TagWeightLedger
            {
                TagId = tag.Id,
                TagNameSnapshot = tag.Name,
                SourceType = LedgerSourceTypeDelete,
                SourceId = null,
                ConsumedRightAssetId = attachment.ConsumedRightAssetId,
                Delta = -attachment.Weight,
                PreviousWeight = previousWeight,
                NewWeight = tag.CachedWeight,
                IsOwnerAction = tag.OwnerId == currentUserId,
                Reason = "Edgeタグ紐付けの解除",
                OwnerId = currentUserId
            });
        }

        _ = context.TagEdgeTagAttachments.Remove(attachment);
        _ = await context.SaveChangesAsync();

        return new Success<bool>(true);
    }

    public async Task<IReadOnlyList<TagEdge>> GetEdgesForTagAsync(int tagId)
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();
        return await context.TagEdges
            .Include(e => e.SourceTag)
            .Include(e => e.TargetTag)
            .Where(e => e.SourceTagId == tagId || e.TargetTagId == tagId)
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<IReadOnlyList<TagEdgeTagAttachment>> GetAttachmentsForEdgeAsync(int edgeId)
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();
        return await context.TagEdgeTagAttachments
            .Include(a => a.Tag)
            .Where(a => a.TagEdgeId == edgeId)
            .AsNoTracking()
            .ToListAsync();
    }
}