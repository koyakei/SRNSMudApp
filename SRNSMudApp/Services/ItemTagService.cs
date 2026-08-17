#region

using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;

#endregion

namespace SRNSMudApp.Services;

/// <summary>
///     IItemTagService の EF Core 実装。
///     ItemCard.razor の @code から DbFactory を直接使っていたロジックをここに集約する。
/// </summary>
public class ItemTagService(IDbContextFactory<ApplicationDbContext> dbFactory) : IItemTagService
{
    public async Task<string?> AddTagToItemAsync(int itemId, int tagId, string currentUserId)
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();

        Tag? tagFromDb = await context.Tags.FirstOrDefaultAsync(t => t.Id == tagId);
        if (tagFromDb == null)
        {
            return "タグが見つかりません。";
        }

        if (tagFromDb.OwnerId != currentUserId)
        {
            return "タグの作成者ではないため、追加する権限がありません。";
        }

        var alreadyExists = await context.TagRelations.AnyAsync(tr => tr.ItemId == itemId && tr.TagId == tagId);
        if (alreadyExists)
        {
            return "このタグは既に追加されています。";
        }

        var newRelation = new TagRelation { ItemId = itemId, TagId = tagId, Weight = 1, OwnerId = currentUserId };
        context.TagRelations.Add(newRelation);

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

        var prevWeight = tagFromDb.CachedWeight;
        tagFromDb.CachedWeight += 1;

        context.TagWeightLedgers!.Add(new TagWeightLedger
        {
            TagId = tagFromDb.Id,
            TagNameSnapshot = tagFromDb.Name,
            SourceType = "TagRelationInsert",
            SourceId = newRelation.Id,
            PreviousWeight = prevWeight,
            NewWeight = tagFromDb.CachedWeight,
            Delta = 1,
            IsOwnerAction = true,
            Reason = "タグの新規追加",
            OwnerId = currentUserId
        });

        await context.SaveChangesAsync();
        return null;
    }

    public async Task<string?> RemoveTagRelationAsync(int relationId, string currentUserId)
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();

        TagRelation? relation = await context.TagRelations.FindAsync(relationId);
        if (relation == null)
        {
            return "タグの関連付けが見つかりません。";
        }

        if (relation.OwnerId != currentUserId)
        {
            return "関連付けた本人ではないため、解除する権限がありません。";
        }

        context.TimelineEvents!.Add(new TimelineEvent
        {
            OwnerId = currentUserId,
            TargetType = "Item",
            TargetItemId = relation.ItemId,
            FollowedTagId = relation.TagId,
            EventType = "Delete",
            PreviousWeight = relation.Weight
        });

        Tag? tag = await context.Tags.FindAsync(relation.TagId);
        if (tag != null)
        {
            var prevWeight = tag.CachedWeight;
            tag.CachedWeight -= relation.Weight;
            context.TagWeightLedgers!.Add(new TagWeightLedger
            {
                TagId = tag.Id,
                TagNameSnapshot = tag.Name,
                SourceType = "TagRelationDelete",
                SourceId = relation.Id,
                PreviousWeight = prevWeight,
                NewWeight = tag.CachedWeight,
                Delta = -relation.Weight,
                IsOwnerAction = true,
                Reason = "タグの削除",
                OwnerId = currentUserId
            });
        }

        context.Remove(relation);

        await context.SaveChangesAsync();
        return null;
    }

    public async Task<UpdateWeightResult> UpdateTagWeightAsync(int relationId, int delta, string currentUserId)
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();

        TagRelation? entity = await context.TagRelations.FindAsync(relationId);
        if (entity == null)
        {
            return UpdateWeightResult.NotFound;
        }

        if (entity.OwnerId != currentUserId)
        {
            return UpdateWeightResult.NoPermission;
        }

        entity.Weight += delta;
        entity.UpdatedDate = DateTime.UtcNow;

        Tag? tag = await context.Tags.FindAsync(entity.TagId);
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
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();

        TagRelation? entity = await context.TagRelations.FindAsync(relationId);
        if (entity == null)
        {
            return "タグの関連付けが見つかりません。";
        }

        if (entity.OwnerId != currentUserId)
        {
            return "関連付けた本人ではないため、Weightを変更する権限がありません。";
        }

        if (newWeight == entity.Weight)
        {
            return null;
        }

        var delta = newWeight - entity.Weight;
        entity.Weight = newWeight;
        entity.UpdatedDate = DateTime.UtcNow;

        Tag? tag = await context.Tags.FindAsync(entity.TagId);
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
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();

        TagRelation? entity = await context.TagRelations.FindAsync(relationId);
        if (entity == null)
        {
            return "タグの関連付けが見つかりません。";
        }

        if (entity.OwnerId != currentUserId)
        {
            return "関連付けた本人ではないため、変更する権限がありません。";
        }

        if (entity.TagId == newTagId)
        {
            return null;
        }

        var alreadyExists = await context.TagRelations.AnyAsync(tr => tr.ItemId == itemId && tr.TagId == newTagId);
        if (alreadyExists)
        {
            return "変更先のタグは既に追加されています。";
        }

        entity.TagId = newTagId;
        entity.UpdatedDate = DateTime.UtcNow;

        await context.SaveChangesAsync();
        return null;
    }

    public async Task<string?> AddTagToTagAsync(int targetTagId, int tagId, string currentUserId)
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();

        var alreadyExists = await context.TagRelationToTags
            .AnyAsync(tr => tr.TargetTagId == targetTagId && tr.TagId == tagId);
        if (alreadyExists)
        {
            return "このタグは既に追加されています。";
        }

        var newRelation = new TagRelationToTag
        {
            TargetTagId = targetTagId, TagId = tagId, Weight = 1, OwnerId = currentUserId
        };
        context.Set<TagRelationToTag>().Add(newRelation);

        await context.SaveChangesAsync();

        Tag? tagFromDb = await context.Tags.FindAsync(tagId);
        if (tagFromDb != null)
        {
            var prevWeight = tagFromDb.CachedWeight;
            tagFromDb.CachedWeight += 1;

            context.TagWeightLedgers!.Add(new TagWeightLedger
            {
                TagId = tagFromDb.Id,
                TagNameSnapshot = tagFromDb.Name,
                SourceType = "TagRelationToTagInsert",
                SourceId = newRelation.Id,
                PreviousWeight = prevWeight,
                NewWeight = tagFromDb.CachedWeight,
                Delta = 1,
                IsOwnerAction = true,
                Reason = "タグの新規追加",
                OwnerId = currentUserId
            });

            await context.SaveChangesAsync();
        }

        return null;
    }

    public async Task<string?> RemoveTagToTagRelationAsync(int relationId, string currentUserId)
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();

        TagRelationToTag? entity = await context.TagRelationToTags.FindAsync(relationId);
        if (entity == null)
        {
            return "タグの関連付けが見つかりません。";
        }

        if (entity.OwnerId != currentUserId)
        {
            return "関連付けた本人ではないため、解除する権限がありません。";
        }

        Tag? tag = await context.Tags.FindAsync(entity.TagId);
        if (tag != null)
        {
            var prevWeight = tag.CachedWeight;
            tag.CachedWeight -= entity.Weight;

            context.TagWeightLedgers!.Add(new TagWeightLedger
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
                OwnerId = currentUserId
            });
        }

        context.Remove(entity);
        await context.SaveChangesAsync();
        return null;
    }

    public async Task<string?> SetParentTagAsync(
        int parentTagId,
        int childTagId,
        string currentUserId,
        IReadOnlyList<Tag> allTagsForCycleCheck)
    {
        if (childTagId == parentTagId)
        {
            return "自分自身を親にすることはできません。";
        }

        // 循環参照の簡易チェック（in-memory で実施）
        Tag? parentTag = allTagsForCycleCheck.FirstOrDefault(t => t.Id == parentTagId);
        var current = parentTag?.ParentTagId;
        while (current != null)
        {
            if (current == childTagId)
            {
                return "循環参照になるため親に設定できません。";
            }

            Tag? p = allTagsForCycleCheck.FirstOrDefault(t => t.Id == current);
            current = p?.ParentTagId;
        }

        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();
        Tag? entity = await context.Tags.FindAsync(childTagId);
        if (entity == null)
        {
            return "対象タグが見つかりません。";
        }

        if (entity.OwnerId != currentUserId)
        {
            return "対象タグの作成者ではないため、親タグを変更する権限がありません。";
        }

        entity.ParentTagId = parentTagId;
        entity.UpdatedDate = DateTime.UtcNow;

        await context.SaveChangesAsync();
        return null;
    }

    public async Task<List<TaggingRequestEntity>> GetTaggingRequestsForItemAsync(int itemId)
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();
        return await context.TaggingRequestEntities!
            .Include(tr => tr.RequestedTag)
            .Include(tr => tr.Owner) // リクエスト作成者
            .Include(tr => tr.RequestItem)
            .ThenInclude(i => i!.Owner)
            .Include(tr => tr.RequestItem)
            .ThenInclude(i => i!.TagRelations)
            .ThenInclude(tr => tr.Tag)
            .Include(tr => tr.Replies)
            .ThenInclude(r => r.Owner) // リプライ作成者
            .Include(tr => tr.Replies)
            .ThenInclude(r => r.TagRelations) // ItemCard向け
            .ThenInclude(tr => tr.Tag)
            .Where(tr => tr.TargetItemId == itemId)
            .OrderByDescending(tr => tr.CreatedDate)
            .ToListAsync();
    }

    public async Task<Item?> AddReplyToRequestAsync(int requestId, string userId, string message)
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();
        var reply = new Item
        {
            TaggingRequestEntityId = requestId, OwnerId = userId, Content = message, CreatedDate = DateTime.UtcNow
        };

        context.Items!.Add(reply);
        await context.SaveChangesAsync();

        // UIの即時更新用に、保存したリプライに関連情報を結合して返す
        return await context.Items
            .Include(r => r.Owner)
            .Include(r => r.TagRelations)
            .ThenInclude(tr => tr.Tag)
            .FirstOrDefaultAsync(r => r.Id == reply.Id);
    }

    public async Task<List<Item>> GetItemRepliesAsync(int parentItemId)
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();
        return await context.Items!
            .Include(i => i.Owner)
            .Include(i => i.TagRelations)
            .ThenInclude(tr => tr.Tag)
            .Where(i => i.ParentItemId == parentItemId)
            .OrderBy(i => i.CreatedDate)
            .ToListAsync();
    }

    public async Task<Item?> AddItemReplyAsync(int parentItemId, string content, string userId)
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();
        var replyItem = new Item
        {
            Content = content,
            OwnerId = userId,
            ParentItemId = parentItemId,
            CreatedDate = DateTime.UtcNow,
            UpdatedDate = DateTime.UtcNow
        };

        context.Items!.Add(replyItem);
        await context.SaveChangesAsync();

        return await context.Items
            .Include(i => i.Owner)
            .FirstOrDefaultAsync(i => i.Id == replyItem.Id);
    }
}