using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;

#pragma warning disable CA1508
#pragma warning disable IDE0010, IDE0072

namespace SRNSMudApp.Services;

/// <summary>
/// タグ間エッジと、エッジに付与されたタグの永続化を担当します。
/// </summary>
/// <param name="dbFactory">データベースコンテキストを生成するファクトリ。</param>
/// <param name="timeProvider">消費日時を取得する時刻プロバイダー。</param>
public class TagEdgeService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    TimeProvider? timeProvider = null) : ITagEdgeService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    private const string LedgerSourceTypeInsert = "TagEdgeTagAttachmentInsert";
    private const string LedgerSourceTypeDelete = "TagEdgeTagAttachmentDelete";

    /// <summary>タグ間に新しいエッジを作成します。</summary>
    /// <param name="sourceTagId">始点タグの ID。</param>
    /// <param name="targetTagId">終点タグの ID。</param>
    /// <param name="ownerId">エッジの所有者 ID。</param>
    /// <returns>作成結果。</returns>
    public async Task<Result<TagEdge>> CreateEdgeAsync(int sourceTagId, int targetTagId, string ownerId)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();

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

    /// <summary>所有者が作成したエッジを削除します。</summary>
    /// <param name="edgeId">削除対象エッジの ID。</param>
    /// <param name="ownerId">削除を要求する所有者 ID。</param>
    /// <returns>削除結果。</returns>
    public async Task<Result<bool>> DeleteEdgeAsync(int edgeId, string ownerId)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();

        TagEdge? edge = await context.TagEdges.FindAsync(edgeId);
        if (edge is null)
        {
            return new Failure("Edge が見つかりません。");
        }

        if (edge.OwnerId != ownerId)
        {
            return new Failure("Edge の作成者ではないため、削除する権限がありません。");
        }

        try
        {
            _ = context.TagEdges.Remove(edge);
            _ = await context.SaveChangesAsync();
            return new Success<bool>(true);
        }
        catch (DbUpdateConcurrencyException)
        {
            context.ChangeTracker.Clear();
            return new Failure("データの状態が変更されました。ページを再読み込みしてから、もう一度やり直してください。");
        }
    }

    /// <summary>RightAsset を消費してタグをエッジに付与します。</summary>
    /// <param name="edgeId">対象エッジの ID。</param>
    /// <param name="tagId">付与するタグの ID。</param>
    /// <param name="rightAssetId">消費する RightAsset の ID。</param>
    /// <param name="currentUserId">操作を行うユーザーの ID。</param>
    /// <param name="weight">付与する重み。</param>
    /// <returns>付与結果。</returns>
    public async Task<Result<TagEdgeTagAttachment>> AttachTagToEdgeAsync(
        int edgeId, int tagId, int rightAssetId, string currentUserId, int weight = 1)
    {
        if (weight <= 0)
        {
            return new Failure("weight は 1 以上を指定してください。");
        }

        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();

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
            Success<RightAsset> s => ExecuteAttachAsync(context, edge, tag, s.Value, currentUserId, weight, _timeProvider)
        });
    }

    private static async Task<Result<TagEdgeTagAttachment>> ExecuteAttachAsync(
        ApplicationDbContext context, TagEdge edge, Tag tag, RightAsset rightAsset, string currentUserId, int weight, TimeProvider timeProvider)
    {
        try
        {
            return await context.Database.ExecuteWithStrategyAsync(async () =>
            {
                rightAsset.Amount -= 1;
                if (rightAsset.Amount <= 0)
                {
                    rightAsset.IsBurned = true;
                    rightAsset.Status = new Burned(timeProvider.GetUtcNow().UtcDateTime);
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

                return new Success<TagEdgeTagAttachment>(attachment);
            });
        }
        catch (DbUpdateConcurrencyException)
        {
            context.ChangeTracker.Clear();
            return new Failure("データの状態が変更されました。ページを再読み込みしてから、もう一度やり直してください。");
        }
    }

    /// <summary>エッジに付与されたタグを解除します。</summary>
    /// <param name="attachmentId">解除対象付与情報の ID。</param>
    /// <param name="currentUserId">操作を行うユーザーの ID。</param>
    /// <returns>解除結果。</returns>
    public async Task<Result<bool>> DetachTagFromEdgeAsync(int attachmentId, string currentUserId)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();

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

        try
        {
            _ = context.TagEdgeTagAttachments.Remove(attachment);
            _ = await context.SaveChangesAsync();

            return new Success<bool>(true);
        }
        catch (DbUpdateConcurrencyException)
        {
            context.ChangeTracker.Clear();
            return new Failure("データの状態が変更されました。ページを再読み込みしてから、もう一度やり直してください。");
        }
    }

    /// <summary>指定タグに接続されたエッジを取得します。</summary>
    /// <param name="tagId">検索対象タグの ID。</param>
    /// <returns>接続されたエッジの一覧。</returns>
    public async Task<IReadOnlyList<TagEdge>> GetEdgesForTagAsync(int tagId)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();
        return await context.TagEdges
            .Include(e => e.SourceTag)
            .Include(e => e.TargetTag)
            .Where(e => e.SourceTagId == tagId || e.TargetTagId == tagId)
            .AsNoTracking()
            .ToListAsync();
    }

    /// <summary>指定エッジに付与されたタグを取得します。</summary>
    /// <param name="edgeId">検索対象エッジの ID。</param>
    /// <returns>タグ付与情報の一覧。</returns>
    public async Task<IReadOnlyList<TagEdgeTagAttachment>> GetAttachmentsForEdgeAsync(int edgeId)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();
        return await context.TagEdgeTagAttachments
            .Include(a => a.Tag)
            .Where(a => a.TagEdgeId == edgeId)
            .AsNoTracking()
            .ToListAsync();
    }

    /// <summary>すべてのエッジと関連タグを取得します。</summary>
    /// <returns>エッジの一覧。</returns>
    public async Task<IReadOnlyList<TagEdge>> GetAllEdgesAsync()
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();
        return await context.TagEdges
            .Include(e => e.SourceTag)
            .Include(e => e.TargetTag)
            .Include(e => e.TagAttachments)
                .ThenInclude(a => a.Tag)
            .AsNoTracking()
            .ToListAsync();
    }

    /// <summary>ユーザーが利用可能な RightAsset を取得します。</summary>
    /// <param name="userId">所有者のユーザー ID。</param>
    /// <param name="targetTagId">権利対象タグの ID。</param>
    /// <returns>利用可能な RightAsset の一覧。</returns>
    public async Task<List<RightAsset>> GetAvailableRightAssetsAsync(string userId, int targetTagId)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();
        List<RightAsset> assets = await context.RightAssets
            .Where(r => r.OwnerId == userId && r.TargetTagId == targetTagId && !r.IsBurned && r.Amount > 0)
            .OrderByDescending(r => r.Amount)
            .AsNoTracking()
            .ToListAsync();

        if (assets.Count == 0 && !string.IsNullOrWhiteSpace(userId))
        {
            Tag? tag = await context.Tags.FindAsync(targetTagId);
            if (tag != null && tag.OwnerId == userId)
            {
                // タグオーナー自身で未消費 RightAsset が存在しない場合、自動的に新規 RightAsset を発行して付与
                RightAsset newAsset = new()
                {
                    OwnerId = userId,
                    TargetTagId = targetTagId,
                    Amount = 10,
                    IsBurned = false,
                    Status = new NotBurned()
                };
                _ = context.RightAssets.Add(newAsset);
                _ = await context.SaveChangesAsync();
                assets.Add(newAsset);
            }
        }

        return assets;
    }
}