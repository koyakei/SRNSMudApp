#region

using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;

#endregion

namespace SRNSMudApp.Services;

/// <summary>
/// IItemTagService の EF Core 実装。
/// ItemCard.razor の @code から DbFactory を直接使っていたロジックをここに集約する。
/// </summary>
public class ItemTagService(IDbContextFactory<ApplicationDbContext> dbFactory) : IItemTagService
{
    public async Task<string?> AddTagToItemAsync(int itemId, int tagId, string currentUserId)
    {
        await using var context = await dbFactory.CreateDbContextAsync();

        var tagFromDb = await context.Tags.FirstOrDefaultAsync(t => t.Id == tagId);
        if (tagFromDb == null) return "タグが見つかりません。";

        if (tagFromDb.OwnerId != currentUserId)
            return "タグの作成者ではないため、追加する権限がありません。";

        var alreadyExists = await context.TagRelations.AnyAsync(tr => tr.ItemId == itemId && tr.TagId == tagId);
        if (alreadyExists) return "このタグは既に追加されています。";

        context.TagRelations.Add(new TagRelation
        {
            ItemId = itemId,
            TagId = tagId,
            Weight = 1,
            OwnerId = currentUserId
        });
        context.TimelineEvents!.Add(new TimelineEvent
        {
            OwnerId = currentUserId,
            TargetType = "Item",
            TargetItemId = itemId,
            FollowedTagId = tagId,
            EventType = "Insert",
            NewWeight = 1
        });

        await context.SaveChangesAsync();
        return null;
    }

    public async Task<string?> RemoveTagRelationAsync(int relationId, string currentUserId)
    {
        await using var context = await dbFactory.CreateDbContextAsync();

        var relation = await context.TagRelations.FindAsync(relationId);
        if (relation == null) return "タグの関連付けが見つかりません。";

        if (relation.OwnerId != currentUserId)
            return "関連付けた本人ではないため、解除する権限がありません。";

        context.TimelineEvents!.Add(new TimelineEvent
        {
            OwnerId = currentUserId,
            TargetType = "Item",
            TargetItemId = relation.ItemId,
            FollowedTagId = relation.TagId,
            EventType = "Delete",
            PreviousWeight = relation.Weight
        });
        context.Remove(relation);

        await context.SaveChangesAsync();
        return null;
    }

    public async Task<UpdateWeightResult> UpdateTagWeightAsync(int relationId, int delta, string currentUserId)
    {
        await using var context = await dbFactory.CreateDbContextAsync();

        var entity = await context.TagRelations.FindAsync(relationId);
        if (entity == null) return UpdateWeightResult.NotFound;

        if (entity.OwnerId != currentUserId) return UpdateWeightResult.NoPermission;

        entity.Weight += delta;
        entity.UpdatedDate = DateTime.UtcNow;

        var tag = await context.Tags.FindAsync(entity.TagId);
        if (tag != null)
        {
            var prevWeight = tag.CachedWeight;
            tag.CachedWeight += delta;
            context.TagWeightLedgers!.Add(new TagWeightLedger
            {
                TagId = tag.Id,
                TagNameSnapshot = tag.Name,
                SourceType = "TagRelationUpdate",
                SourceId = entity.Id,
                PreviousWeight = prevWeight,
                NewWeight = tag.CachedWeight,
                Delta = delta,
                IsOwnerAction = true,
                Reason = "ユーザーによる直接ウェイト変更",
                OwnerId = currentUserId
            });

            context.TimelineEvents!.Add(new TimelineEvent
            {
                OwnerId = currentUserId,
                TargetType = "Item",
                TargetItemId = entity.ItemId,
                FollowedTagId = entity.TagId,
                EventType = "Update",
                PreviousWeight = entity.Weight - delta,
                NewWeight = entity.Weight
            });
        }

        await context.SaveChangesAsync();
        return UpdateWeightResult.Success;
    }

    public async Task<string?> SetTagWeightAsync(int relationId, int newWeight, string currentUserId)
    {
        await using var context = await dbFactory.CreateDbContextAsync();

        var entity = await context.TagRelations.FindAsync(relationId);
        if (entity == null) return "タグの関連付けが見つかりません。";

        if (entity.OwnerId != currentUserId)
            return "関連付けた本人ではないため、Weightを変更する権限がありません。";

        if (newWeight == entity.Weight) return null;

        var delta = newWeight - entity.Weight;
        entity.Weight = newWeight;
        entity.UpdatedDate = DateTime.UtcNow;

        var tag = await context.Tags.FindAsync(entity.TagId);
        if (tag != null)
        {
            var prevWeight = tag.CachedWeight;
            tag.CachedWeight += delta;
            context.TagWeightLedgers!.Add(new TagWeightLedger
            {
                TagId = tag.Id,
                TagNameSnapshot = tag.Name,
                SourceType = "TagRelationUpdate",
                SourceId = entity.Id,
                PreviousWeight = prevWeight,
                NewWeight = tag.CachedWeight,
                Delta = delta,
                IsOwnerAction = true,
                Reason = "ユーザーによる直接ウェイト一括変更",
                OwnerId = currentUserId
            });
        }

        await context.SaveChangesAsync();
        return null;
    }

    public async Task<string?> ChangeItemTagAsync(int relationId, int newTagId, int itemId, string currentUserId)
    {
        await using var context = await dbFactory.CreateDbContextAsync();

        var entity = await context.TagRelations.FindAsync(relationId);
        if (entity == null) return "タグの関連付けが見つかりません。";

        if (entity.OwnerId != currentUserId)
            return "関連付けた本人ではないため、変更する権限がありません。";

        if (entity.TagId == newTagId) return null;

        var alreadyExists = await context.TagRelations.AnyAsync(tr => tr.ItemId == itemId && tr.TagId == newTagId);
        if (alreadyExists) return "変更先のタグは既に追加されています。";

        entity.TagId = newTagId;
        entity.UpdatedDate = DateTime.UtcNow;

        await context.SaveChangesAsync();
        return null;
    }

    public async Task<string?> AddTagToTagAsync(int targetTagId, int tagId, string currentUserId)
    {
        await using var context = await dbFactory.CreateDbContextAsync();

        var alreadyExists = await context.TagRelationToTags
            .AnyAsync(tr => tr.TargetTagId == targetTagId && tr.TagId == tagId);
        if (alreadyExists) return "このタグは既に追加されています。";

        context.Set<TagRelationToTag>().Add(new TagRelationToTag
        {
            TargetTagId = targetTagId,
            TagId = tagId,
            Weight = 1,
            OwnerId = currentUserId
        });

        await context.SaveChangesAsync();
        return null;
    }

    public async Task<string?> RemoveTagToTagRelationAsync(int relationId, string currentUserId)
    {
        await using var context = await dbFactory.CreateDbContextAsync();

        var entity = await context.TagRelationToTags.FindAsync(relationId);
        if (entity == null) return "タグの関連付けが見つかりません。";

        if (entity.OwnerId != currentUserId)
            return "関連付けた本人ではないため、解除する権限がありません。";

        context.Remove(entity);
        await context.SaveChangesAsync();
        return null;
    }

    public async Task<string?> SetParentTagAsync(
        int parentTagId,
        int childTagId,
        string currentUserId,
        IReadOnlyList<Data.Tag> allTagsForCycleCheck)
    {
        if (childTagId == parentTagId)
            return "自分自身を親にすることはできません。";

        // 循環参照の簡易チェック（in-memory で実施）
        var parentTag = allTagsForCycleCheck.FirstOrDefault(t => t.Id == parentTagId);
        var current = parentTag?.ParentTagId;
        while (current != null)
        {
            if (current == childTagId)
                return "循環参照になるため親に設定できません。";
            var p = allTagsForCycleCheck.FirstOrDefault(t => t.Id == current);
            current = p?.ParentTagId;
        }

        await using var context = await dbFactory.CreateDbContextAsync();
        var entity = await context.Tags.FindAsync(childTagId);
        if (entity == null) return "対象タグが見つかりません。";

        if (entity.OwnerId != currentUserId)
            return "対象タグの作成者ではないため、親タグを変更する権限がありません。";

        entity.ParentTagId = parentTagId;
        entity.UpdatedDate = DateTime.UtcNow;

        await context.SaveChangesAsync();
        return null;
    }
}
