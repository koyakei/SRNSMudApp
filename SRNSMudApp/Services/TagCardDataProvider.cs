using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;

namespace SRNSMudApp.Services;

/// <summary>
///     TagCard の操作結果。
/// </summary>
public enum TagCardOperationResult
{
    Success,
    AlreadyExists,
    NotFound,
    NotOwner
}

/// <summary>
///     TagCard コンポーネント用のデータアクセスを分離するインターフェース。
///     コンポーネントから DbContext への直接依存を断ち、単体テストでモック可能にする。
/// </summary>
public interface ITagCardDataProvider
{
    Task<List<TagRelationToTag>> GetUserVoteRelationsAsync(int tagId, string userId);

    /// <summary>ユーザーの投票状態を切り替える（対象に投票、逆側の票は解除）。</summary>
    Task ToggleTagVoteAsync(int tagId, string userId, int targetSystemTagId, int oppositeSystemTagId);

    Task<TagCardOperationResult> AddTagToTagAsync(int targetTagId, int selectedTagId, string ownerId);

    Task<TagCardOperationResult> RemoveRelationAsync(int relationId, string ownerId);

    /// <summary>Weight を delta 分だけ増減する。エンティティが存在しない場合は <see cref="TagCardOperationResult.NotFound" />。</summary>
    Task<TagCardOperationResult> UpdateRelationWeightAsync(int relationId, int delta, string ownerId);

    /// <summary>Weight を指定値に変更する。エンティティが存在しない場合は <see cref="TagCardOperationResult.NotFound" />。</summary>
    Task<TagCardOperationResult> SetRelationWeightAsync(int relationId, int newWeight, string ownerId);

    Task<TagCardOperationResult> ChangeRelationTagAsync(int oldRelationId, int targetTagId, int newTagId, string ownerId);

    Task<TagCardOperationResult> SetParentTagAsync(int childTagId, int parentTagId, string ownerId);
}

public class TagCardDataProvider(IDbContextFactory<ApplicationDbContext> dbFactory) : ITagCardDataProvider
{
    public async Task<List<TagRelationToTag>> GetUserVoteRelationsAsync(int tagId, string userId)
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();
        return await context.TagRelationToTags
            .Where(tr => tr.TargetTagId == tagId && tr.OwnerId == userId)
            .ToListAsync();
    }

    public async Task ToggleTagVoteAsync(
        int tagId,
        string userId,
        int targetSystemTagId,
        int oppositeSystemTagId)
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();
        List<TagRelationToTag> relations = await context.TagRelationToTags
            .Where(tr => tr.TargetTagId == tagId && tr.OwnerId == userId)
            .ToListAsync();

        TagRelationToTag? targetRelation = relations.FirstOrDefault(tr => tr.TagId == targetSystemTagId);
        TagRelationToTag? oppositeRelation = relations.FirstOrDefault(tr => tr.TagId == oppositeSystemTagId);

        if (targetRelation is not null)
        {
            _ = context.TagRelationToTags.Remove(targetRelation);
        }
        else
        {
            _ = context.TagRelationToTags.Add(new TagRelationToTag
            {
                TargetTagId = tagId,
                TagId = targetSystemTagId,
                OwnerId = userId,
                Weight = 1
            });

            if (oppositeRelation is not null)
            {
                _ = context.TagRelationToTags.Remove(oppositeRelation);
            }
        }

        _ = await context.SaveChangesAsync();
    }

    public async Task<TagCardOperationResult> AddTagToTagAsync(
        int targetTagId,
        int selectedTagId,
        string ownerId)
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();
        var alreadyExists = await context.TagRelationToTags
            .AnyAsync(tr => tr.TargetTagId == targetTagId && tr.TagId == selectedTagId);

        if (alreadyExists)
        {
            return TagCardOperationResult.AlreadyExists;
        }

        var newRelation = new TagRelationToTag
        {
            TargetTagId = targetTagId,
            TagId = selectedTagId,
            Weight = 1,
            OwnerId = ownerId
        };

        _ = context.Set<TagRelationToTag>().Add(newRelation);
        _ = context.TimelineEvents!.Add(new TimelineEvent
        {
            OwnerId = ownerId,
            Target = new TagTarget(targetTagId),
            FollowedTagId = selectedTagId,
            EventType = "Insert",
            NewWeight = 1
        });
        _ = await context.SaveChangesAsync();

        Tag? tagFromDb = await context.Tags.FindAsync(selectedTagId);
        if (tagFromDb is not null)
        {
            var prevWeight = tagFromDb.CachedWeight;
            tagFromDb.CachedWeight += 1;
            _ = context.TagWeightLedgers!.Add(new TagWeightLedger
            {
                TagId = tagFromDb.Id,
                TagNameSnapshot = tagFromDb.Name,
                SourceType = "TagRelationToTagInsert",
                SourceId = null,
                PreviousWeight = prevWeight,
                NewWeight = tagFromDb.CachedWeight,
                Delta = 1,
                IsOwnerAction = true,
                Reason = "タグの新規追加",
                OwnerId = ownerId
            });
            _ = await context.SaveChangesAsync();
        }

        return TagCardOperationResult.Success;
    }

    public async Task<TagCardOperationResult> RemoveRelationAsync(int relationId, string ownerId)
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();
        TagRelationToTag? entity = await context.TagRelationToTags.Include(tr => tr.Tag)
            .FirstOrDefaultAsync(tr => tr.Id == relationId);
        switch (entity)
        {
            case not null:
                Tag? tag = entity.Tag;
                if (tag is not null)
                {
                    var prevWeight = tag.CachedWeight;
                    tag.CachedWeight -= entity.Weight;
                    _ = context.TagWeightLedgers!.Add(new TagWeightLedger
                    {
                        TagId = tag.Id,
                        TagNameSnapshot = tag.Name,
                        SourceType = "TagRelationToTagDelete",
                        SourceId = entity.Id,
                        PreviousWeight = prevWeight,
                        NewWeight = tag.CachedWeight,
                        Delta = -entity.Weight,
                        IsOwnerAction = true,
                        Reason = "タグの関連付け解除",
                        OwnerId = ownerId
                    });
                }

                _ = context.Remove(entity);
                _ = context.TimelineEvents!.Add(new TimelineEvent
                {
                    OwnerId = ownerId,
                    Target = new TagTarget(entity.TargetTagId),
                    FollowedTagId = entity.TagId,
                    EventType = "Delete",
                    PreviousWeight = entity.Weight
                });
                _ = await context.SaveChangesAsync();
                return TagCardOperationResult.Success;
            default:
                return TagCardOperationResult.NotFound;
        }
    }

    public async Task<TagCardOperationResult> UpdateRelationWeightAsync(
        int relationId,
        int delta,
        string ownerId)
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();
        TagRelationToTag? entity = await context.TagRelationToTags.FindAsync(relationId);
        if (entity is null)
        {
            return TagCardOperationResult.NotFound;
        }

        entity.Weight += delta;
        entity.UpdatedDate = DateTime.UtcNow;

        await AddWeightLedgerAndTimelineAsync(context, entity, delta, ownerId, "ユーザーによる直接ウェイト変更");
        _ = await context.SaveChangesAsync();
        return TagCardOperationResult.Success;
    }

    public async Task<TagCardOperationResult> SetRelationWeightAsync(
        int relationId,
        int newWeight,
        string ownerId)
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();
        TagRelationToTag? entity = await context.TagRelationToTags.FindAsync(relationId);
        if (entity is null)
        {
            return TagCardOperationResult.NotFound;
        }

        var delta = newWeight - entity.Weight;
        entity.Weight = newWeight;
        entity.UpdatedDate = DateTime.UtcNow;

        await AddWeightLedgerAndTimelineAsync(context, entity, delta, ownerId, "ユーザーによるWeightの一括変更");
        _ = await context.SaveChangesAsync();
        return TagCardOperationResult.Success;
    }

    public async Task<TagCardOperationResult> ChangeRelationTagAsync(
        int oldRelationId,
        int targetTagId,
        int newTagId,
        string ownerId)
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();
        var alreadyExists = await context.TagRelationToTags
            .AnyAsync(tr => tr.TargetTagId == targetTagId && tr.TagId == newTagId);

        if (alreadyExists)
        {
            return TagCardOperationResult.AlreadyExists;
        }

        TagRelationToTag? entity = await context.TagRelationToTags.FindAsync(oldRelationId);
        switch (entity)
        {
            case not null:
                entity.TagId = newTagId;
                entity.UpdatedDate = DateTime.UtcNow;
                _ = await context.SaveChangesAsync();
                return TagCardOperationResult.Success;
            default:
                return TagCardOperationResult.NotFound;
        }
    }

    public async Task<TagCardOperationResult> SetParentTagAsync(
        int childTagId,
        int parentTagId,
        string ownerId)
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();
        Tag? entity = await context.Tags.FindAsync(childTagId);
        switch (entity)
        {
            case not null:
                if (entity.OwnerId != ownerId)
                {
                    return TagCardOperationResult.NotOwner;
                }

                entity.ParentTagId = parentTagId;
                entity.UpdatedDate = DateTime.UtcNow;
                _ = await context.SaveChangesAsync();
                return TagCardOperationResult.Success;
            default:
                return TagCardOperationResult.NotFound;
        }
    }

    private static async Task AddWeightLedgerAndTimelineAsync(
        ApplicationDbContext context,
        TagRelationToTag entity,
        int delta,
        string ownerId,
        string reason)
    {
        Tag? tag = await context.Tags.FindAsync(entity.TagId);
        if (tag is not null)
        {
            var prevWeight = tag.CachedWeight;
            tag.CachedWeight += delta;
            _ = context.TagWeightLedgers.Add(new TagWeightLedger
            {
                TagId = tag.Id,
                TagNameSnapshot = tag.Name,
                SourceType = "TagRelationToTagUpdate",
                SourceId = entity.Id,
                PreviousWeight = prevWeight,
                NewWeight = tag.CachedWeight,
                Delta = delta,
                IsOwnerAction = true,
                Reason = reason,
                OwnerId = ownerId
            });

            _ = context.TimelineEvents.Add(new TimelineEvent
            {
                OwnerId = ownerId,
                Target = new TagTarget(entity.TargetTagId),
                FollowedTagId = entity.TagId,
                EventType = "Update",
                PreviousWeight = entity.Weight - delta,
                NewWeight = entity.Weight
            });
        }
    }
}