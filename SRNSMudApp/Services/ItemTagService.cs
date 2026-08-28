#region

using System.Diagnostics.CodeAnalysis;

using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;

#endregion

// CA1508: union 型 (Option<T> / CheckAuth 結果など) の網羅的パターンマッチでは、先行アームの後の
// Some / エラー型アームが静的に「常に真」とみなされるが、網羅性確保のためアームは必須。
// 解析器の誤検知のため、ファイル単位で抑制する。
#pragma warning disable CA1508

// IDE0010 / IDE0072: union 型・enum の網羅的 switch に対する「Populate switch」は、
// 全ケース列挙済み・default 併記済みでも解消されない解析器の誤検知のため抑制する。
#pragma warning disable IDE0010, IDE0072

namespace SRNSMudApp.Services;

public record OperationAuthorized;
public record OperationUnauthorized(string Reason);
[SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "Union type handled by C# compiler")]
public readonly union AuthorizationState(OperationAuthorized, OperationUnauthorized);

public record TagRelationExists;
public record TagRelationDoesNotExist;
[SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "Union type handled by C# compiler")]
public readonly union TagRelationState(TagRelationExists, TagRelationDoesNotExist);

public record SameTag;
public record DifferentTag;
[SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "Union type handled by C# compiler")]
public readonly union TagComparisonState(SameTag, DifferentTag);

public record SameWeight;
public record DifferentWeight;
[SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "Union type handled by C# compiler")]
public readonly union WeightComparisonState(SameWeight, DifferentWeight);

public class ItemTagService(IDbContextFactory<ApplicationDbContext> dbFactory) : IItemTagService
{
    private static AuthorizationState CheckAuth(bool isAuthorized, string unauthMessage) =>
        isAuthorized switch
        {
            true => new OperationAuthorized(),
            false => new OperationUnauthorized(unauthMessage)
        };

    private static TagRelationState CheckTagRelation(bool exists) =>
        exists switch
        {
            true => new TagRelationExists(),
            false => new TagRelationDoesNotExist()
        };

    public async Task<string?> AddTagToItemAsync(int itemId, int tagId, string currentUserId)
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();

        Tag? tagFromDb = await context.Tags.FirstOrDefaultAsync(t => t.Id == tagId);
        var tagOption = Option<Tag>.Create(tagFromDb);

        return await (tagOption switch
        {
            None => Task.FromResult<string?>("タグが見つかりません。"),
            Some<Tag> someTag => CheckAuth(someTag.Value.GetKind() is not UserCustomTag custom || custom.OwnerId == currentUserId, "タグの作成者ではないため、追加する権限がありません。") switch
            {
                OperationUnauthorized unauth => Task.FromResult<string?>(unauth.Reason),
                OperationAuthorized => ProcessAddTagRelation(context, itemId, tagId, currentUserId, someTag.Value)
            },
            null => Task.FromResult<string?>("タグが見つかりません。")
        });
    }

    private static async Task<string?> ProcessAddTagRelation(ApplicationDbContext context, int itemId, int tagId, string currentUserId, Tag tagFromDb)
    {
        var alreadyExists = await context.TagRelations.AnyAsync(tr => tr.ItemId == itemId && tr.TagId == tagId);
        return await (CheckTagRelation(alreadyExists) switch
        {
            TagRelationExists => Task.FromResult<string?>("このタグは既に追加されています。"),
            TagRelationDoesNotExist => ExecuteAddTagRelationAsync(context, itemId, tagId, currentUserId, tagFromDb)
        });
    }

    private static async Task<string?> ExecuteAddTagRelationAsync(ApplicationDbContext context, int itemId, int tagId, string currentUserId, Tag tagFromDb)
    {
        var newRelation = new TagRelation { ItemId = itemId, TagId = tagId, Weight = 1, OwnerId = currentUserId };
        _ = context.TagRelations.Add(newRelation);

        _ = context.TimelineEvents.Add(new TimelineEvent
        {
            OwnerId = currentUserId,
            Target = new ItemTarget(itemId),
            FollowedTagId = tagId,
            EventType = "Insert",
            NewWeight = 1
        });

        _ = await context.SaveChangesAsync();

        var prevWeight = tagFromDb.CachedWeight;
        tagFromDb.CachedWeight++;

        _ = context.TagWeightLedgers.Add(new TagWeightLedger
        {
            TagId = tagFromDb.Id,
            TagNameSnapshot = tagFromDb.Name,
            ItemId = itemId,
            SourceType = "TagRelationInsert",
            SourceId = newRelation.Id,
            PreviousWeight = prevWeight,
            NewWeight = tagFromDb.CachedWeight,
            Delta = 1,
            IsOwnerAction = true,
            Reason = "タグの新規追加",
            OwnerId = currentUserId
        });

        _ = await context.SaveChangesAsync();
        return null;
    }

    public async Task<string?> RemoveTagRelationAsync(int relationId, string currentUserId)
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();

        TagRelation? relation = await context.TagRelations.FindAsync(relationId);
        var relationOption = Option<TagRelation>.Create(relation);

        return await (relationOption switch
        {
            None => Task.FromResult<string?>("タグの関連付けが見つかりません。"),
            Some<TagRelation> someRel => CheckAuth(someRel.Value.OwnerId == currentUserId, "関連付けた本人ではないため、解除する権限がありません。") switch
            {
                OperationUnauthorized unauth => Task.FromResult<string?>(unauth.Reason),
                OperationAuthorized => ExecuteRemoveTagRelationAsync(context, someRel.Value, currentUserId)
            },
            null => Task.FromResult<string?>("タグの関連付けが見つかりません。")
        });
    }

    private static async Task<string?> ExecuteRemoveTagRelationAsync(ApplicationDbContext context, TagRelation relation, string currentUserId)
    {
        _ = context.TimelineEvents.Add(new TimelineEvent
        {
            OwnerId = currentUserId,
            Target = new ItemTarget(relation.ItemId),
            FollowedTagId = relation.TagId,
            EventType = "Delete",
            PreviousWeight = relation.Weight
        });

        Tag? tag = await context.Tags.FindAsync(relation.TagId);
        var tagOption = Option<Tag>.Create(tag);

        _ = tagOption switch
        {
            Some<Tag> someTag => ProcessTagWeightRemoval(context, someTag.Value, relation, currentUserId),
            _ => true
        };

        _ = context.Remove(relation);
        _ = await context.SaveChangesAsync();
        return null;
    }

    private static bool ProcessTagWeightRemoval(ApplicationDbContext context, Tag tag, TagRelation relation, string currentUserId)
    {
        var prevWeight = tag.CachedWeight;
        tag.CachedWeight -= relation.Weight;
        _ = context.TagWeightLedgers.Add(new TagWeightLedger
        {
            TagId = tag.Id,
            TagNameSnapshot = tag.Name,
            ItemId = relation.ItemId,
            SourceType = "TagRelationDelete",
            SourceId = relation.Id,
            PreviousWeight = prevWeight,
            NewWeight = tag.CachedWeight,
            Delta = -relation.Weight,
            IsOwnerAction = true,
            Reason = "タグの削除",
            OwnerId = currentUserId
        });
        return true;
    }

    public async Task<UpdateWeightResult> UpdateTagWeightAsync(int relationId, int delta, string currentUserId)
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();

        TagRelation? entity = await context.TagRelations.FindAsync(relationId);
        var entityOption = Option<TagRelation>.Create(entity);

        return await (entityOption switch
        {
            None => Task.FromResult(UpdateWeightResult.NotFound),
            Some<TagRelation> someRel => CheckAuth(someRel.Value.OwnerId == currentUserId, "") switch
            {
                OperationUnauthorized => Task.FromResult(UpdateWeightResult.NoPermission),
                OperationAuthorized => ExecuteUpdateTagWeightAsync(context, someRel.Value, delta, currentUserId)
            },
            null => Task.FromResult(UpdateWeightResult.NotFound)
        });
    }

    private static async Task<UpdateWeightResult> ExecuteUpdateTagWeightAsync(ApplicationDbContext context, TagRelation entity, int delta, string currentUserId)
    {
        entity.Weight += delta;
        entity.UpdatedDate = DateTime.UtcNow;

        Tag? tag = await context.Tags.FindAsync(entity.TagId);
        var tagOption = Option<Tag>.Create(tag);

        _ = tagOption switch
        {
            Some<Tag> someTag => ProcessTagWeightUpdate(context, someTag.Value, entity, delta, currentUserId),
            _ => true
        };

        _ = await context.SaveChangesAsync();
        return UpdateWeightResult.Success;
    }

    private static bool ProcessTagWeightUpdate(ApplicationDbContext context, Tag tag, TagRelation entity, int delta, string currentUserId)
    {
        var prevWeight = tag.CachedWeight;
        tag.CachedWeight += delta;
        _ = context.TagWeightLedgers.Add(new TagWeightLedger
        {
            TagId = tag.Id,
            TagNameSnapshot = tag.Name,
            ItemId = entity.ItemId,
            SourceType = "TagRelationUpdate",
            SourceId = entity.Id,
            PreviousWeight = prevWeight,
            NewWeight = tag.CachedWeight,
            Delta = delta,
            IsOwnerAction = true,
            Reason = "ユーザーによる直接ウェイト変更",
            OwnerId = currentUserId
        });

        _ = context.TimelineEvents.Add(new TimelineEvent
        {
            OwnerId = currentUserId,
            Target = new ItemTarget(entity.ItemId),
            FollowedTagId = entity.TagId,
            EventType = "Update",
            PreviousWeight = entity.Weight - delta,
            NewWeight = entity.Weight
        });
        return true;
    }

    public async Task<string?> SetTagWeightAsync(int relationId, int newWeight, string currentUserId)
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();

        TagRelation? entity = await context.TagRelations.FindAsync(relationId);
        var entityOption = Option<TagRelation>.Create(entity);

        return await (entityOption switch
        {
            None => Task.FromResult<string?>("タグの関連付けが見つかりません。"),
            Some<TagRelation> someRel => CheckAuth(someRel.Value.OwnerId == currentUserId, "関連付けた本人ではないため、Weightを変更する権限がありません。") switch
            {
                OperationUnauthorized unauth => Task.FromResult<string?>(unauth.Reason),
                OperationAuthorized => (someRel.Value.Weight == newWeight) switch
                {
                    true => (WeightComparisonState)new SameWeight(),
                    false => new DifferentWeight()
                } switch
                {
                    SameWeight => Task.FromResult<string?>(null),
                    DifferentWeight => ExecuteSetTagWeightAsync(context, someRel.Value, newWeight, currentUserId)
                }
            },
            null => Task.FromResult<string?>("タグの関連付けが見つかりません。")
        });
    }

    private static async Task<string?> ExecuteSetTagWeightAsync(ApplicationDbContext context, TagRelation entity, int newWeight, string currentUserId)
    {
        var delta = newWeight - entity.Weight;
        entity.Weight = newWeight;
        entity.UpdatedDate = DateTime.UtcNow;

        Tag? tag = await context.Tags.FindAsync(entity.TagId);
        var tagOption = Option<Tag>.Create(tag);

        _ = tagOption switch
        {
            Some<Tag> someTag => ProcessTagWeightSet(context, someTag.Value, entity, delta, currentUserId),
            _ => true
        };

        _ = await context.SaveChangesAsync();
        return null;
    }

    private static bool ProcessTagWeightSet(ApplicationDbContext context, Tag tag, TagRelation entity, int delta, string currentUserId)
    {
        var prevWeight = tag.CachedWeight;
        tag.CachedWeight += delta;
        _ = context.TagWeightLedgers.Add(new TagWeightLedger
        {
            TagId = tag.Id,
            TagNameSnapshot = tag.Name,
            ItemId = entity.ItemId,
            SourceType = "TagRelationUpdate",
            SourceId = entity.Id,
            PreviousWeight = prevWeight,
            NewWeight = tag.CachedWeight,
            Delta = delta,
            IsOwnerAction = true,
            Reason = "ユーザーによる直接ウェイト一括変更",
            OwnerId = currentUserId
        });
        return true;
    }

    public async Task<string?> ChangeItemTagAsync(int relationId, int newTagId, int itemId, string currentUserId)
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();

        TagRelation? entity = await context.TagRelations.FindAsync(relationId);
        var entityOption = Option<TagRelation>.Create(entity);

        return await (entityOption switch
        {
            None => Task.FromResult<string?>("タグの関連付けが見つかりません。"),
            Some<TagRelation> someRel => CheckAuth(someRel.Value.OwnerId == currentUserId, "関連付けた本人ではないため、変更する権限がありません。") switch
            {
                OperationUnauthorized unauth => Task.FromResult<string?>(unauth.Reason),
                OperationAuthorized => (someRel.Value.TagId == newTagId) switch
                {
                    true => (TagComparisonState)new SameTag(),
                    false => new DifferentTag()
                } switch
                {
                    SameTag => Task.FromResult<string?>(null),
                    DifferentTag => ProcessChangeItemTagRelation(context, someRel.Value, newTagId, itemId)
                }
            },
            null => Task.FromResult<string?>("タグの関連付けが見つかりません。")
        });
    }

    private static async Task<string?> ProcessChangeItemTagRelation(ApplicationDbContext context, TagRelation entity, int newTagId, int itemId)
    {
        var alreadyExists = await context.TagRelations.AnyAsync(tr => tr.ItemId == itemId && tr.TagId == newTagId);
        return await (CheckTagRelation(alreadyExists) switch
        {
            TagRelationExists => Task.FromResult<string?>("変更先のタグは既に追加されています。"),
            TagRelationDoesNotExist => ExecuteChangeItemTagAsync(context, entity, newTagId)
        });
    }

    private static async Task<string?> ExecuteChangeItemTagAsync(ApplicationDbContext context, TagRelation entity, int newTagId)
    {
        entity.TagId = newTagId;
        entity.UpdatedDate = DateTime.UtcNow;

        _ = await context.SaveChangesAsync();
        return null;
    }

    public async Task<string?> AddTagToTagAsync(int targetTagId, int tagId, string currentUserId)
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();

        var alreadyExists = await context.TagRelationToTags.AnyAsync(tr => tr.TargetTagId == targetTagId && tr.TagId == tagId);

        return await (CheckTagRelation(alreadyExists) switch
        {
            TagRelationExists => Task.FromResult<string?>("このタグは既に追加されています。"),
            TagRelationDoesNotExist => ExecuteAddTagToTagAsync(context, targetTagId, tagId, currentUserId)
        });
    }

    private static async Task<string?> ExecuteAddTagToTagAsync(ApplicationDbContext context, int targetTagId, int tagId, string currentUserId)
    {
        var newRelation = new TagRelationToTag
        {
            TargetTagId = targetTagId,
            TagId = tagId,
            Weight = 1,
            OwnerId = currentUserId
        };
        _ = context.Set<TagRelationToTag>().Add(newRelation);
        _ = await context.SaveChangesAsync();

        Tag? tagFromDb = await context.Tags.FindAsync(tagId);
        var tagOption = Option<Tag>.Create(tagFromDb);

        await (tagOption switch
        {
            Some<Tag> someTag => ExecuteAddTagToTagLedgerAsync(context, someTag.Value, targetTagId, currentUserId),
            _ => Task.CompletedTask
        });

        return null;
    }

    private static async Task ExecuteAddTagToTagLedgerAsync(ApplicationDbContext context, Tag tagFromDb, int targetTagId, string currentUserId)
    {
        var prevWeight = tagFromDb.CachedWeight;
        tagFromDb.CachedWeight++;

        _ = context.TagWeightLedgers.Add(new TagWeightLedger
        {
            TagId = tagFromDb.Id,
            TagNameSnapshot = tagFromDb.Name,
            TargetTagId = targetTagId,
            SourceType = "TagRelationToTagInsert",
            SourceId = null,
            PreviousWeight = prevWeight,
            NewWeight = tagFromDb.CachedWeight,
            Delta = 1,
            IsOwnerAction = true,
            Reason = "タグの新規追加",
            OwnerId = currentUserId
        });

        _ = await context.SaveChangesAsync();
    }

    public async Task<string?> RemoveTagToTagRelationAsync(int relationId, string currentUserId)
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();

        TagRelationToTag? entity = await context.TagRelationToTags.FindAsync(relationId);
        var entityOption = Option<TagRelationToTag>.Create(entity);

        return await (entityOption switch
        {
            None => Task.FromResult<string?>("タグの関連付けが見つかりません。"),
            Some<TagRelationToTag> someRel => CheckAuth(someRel.Value.OwnerId == currentUserId, "関連付けた本人ではないため、解除する権限がありません。") switch
            {
                OperationUnauthorized unauth => Task.FromResult<string?>(unauth.Reason),
                OperationAuthorized => ExecuteRemoveTagToTagRelationAsync(context, someRel.Value, currentUserId)
            },
            null => Task.FromResult<string?>("タグの関連付けが見つかりません。")
        });
    }

    private static async Task<string?> ExecuteRemoveTagToTagRelationAsync(ApplicationDbContext context, TagRelationToTag entity, string currentUserId)
    {
        Tag? tag = await context.Tags.FindAsync(entity.TagId);
        var tagOption = Option<Tag>.Create(tag);

        _ = tagOption switch
        {
            Some<Tag> someTag => ProcessTagToTagWeightRemoval(context, someTag.Value, entity, currentUserId),
            _ => true
        };

        _ = context.Remove(entity);
        _ = await context.SaveChangesAsync();
        return null;
    }

    private static bool ProcessTagToTagWeightRemoval(ApplicationDbContext context, Tag tag, TagRelationToTag entity, string currentUserId)
    {
        var prevWeight = tag.CachedWeight;
        tag.CachedWeight -= entity.Weight;

        _ = context.TagWeightLedgers.Add(new TagWeightLedger
        {
            TagId = tag.Id,
            TagNameSnapshot = tag.Name,
            TargetTagId = entity.TargetTagId,
            SourceType = "TagRelationToTagDelete",
            SourceId = null,
            PreviousWeight = prevWeight,
            NewWeight = tag.CachedWeight,
            Delta = -entity.Weight,
            IsOwnerAction = true,
            Reason = "タグの関連付け解除",
            OwnerId = currentUserId
        });
        return true;
    }

    public async Task<string?> SetParentTagAsync(int parentTagId, int childTagId, string currentUserId, IReadOnlyList<Tag> allTagsForCycleCheck)
    {
        return await ((childTagId == parentTagId) switch
        {
            true => (TagComparisonState)new SameTag(),
            false => new DifferentTag()
        } switch
        {
            SameTag => Task.FromResult<string?>("自分自身を親にすることはできません。"),
            DifferentTag => ProcessParentTagCycleCheck(parentTagId, childTagId, currentUserId, allTagsForCycleCheck)
        });
    }

    private async Task<string?> ProcessParentTagCycleCheck(int parentTagId, int childTagId, string currentUserId, IReadOnlyList<Tag> allTagsForCycleCheck)
    {
        var hasCycle = false;
        Tag? parentTag = allTagsForCycleCheck.FirstOrDefault(t => t.Id == parentTagId);
        var current = parentTag?.ParentTagId;

        while (current != null)
        {
            hasCycle = (current == childTagId) switch
            {
                true => true,
                false => hasCycle
            };

            current = hasCycle switch
            {
                true => null, // Break loop
                false => allTagsForCycleCheck.FirstOrDefault(t => t.Id == current)?.ParentTagId
            };
        }

        return await (hasCycle switch
        {
            true => Task.FromResult<string?>("循環参照になるため親に設定できません。"),
            false => ExecuteSetParentTagAsync(parentTagId, childTagId, currentUserId)
        });
    }

    private async Task<string?> ExecuteSetParentTagAsync(int parentTagId, int childTagId, string currentUserId)
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();
        Tag? entity = await context.Tags.FindAsync(childTagId);
        var entityOption = Option<Tag>.Create(entity);

        return await (entityOption switch
        {
            None => Task.FromResult<string?>("対象タグが見つかりません。"),
            Some<Tag> someEntity => CheckAuth(someEntity.Value.OwnerId == currentUserId, "対象タグの作成者ではないため、親タグを変更する権限がありません。") switch
            {
                OperationUnauthorized unauth => Task.FromResult<string?>(unauth.Reason),
                OperationAuthorized => ProcessSaveParentTag(context, someEntity.Value, parentTagId)
            },
            null => Task.FromResult<string?>("対象タグが見つかりません。")
        });
    }

    private static async Task<string?> ProcessSaveParentTag(ApplicationDbContext context, Tag entity, int parentTagId)
    {
        entity.ParentTagId = parentTagId;
        entity.UpdatedDate = DateTime.UtcNow;

        _ = await context.SaveChangesAsync();
        return null;
    }

    public async Task<IReadOnlyList<TaggingRequestEntity>> GetTaggingRequestsForItemAsync(int itemId)
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
            TaggingRequestEntityId = requestId,
            OwnerId = userId,
            Content = message,
            CreatedDate = DateTime.UtcNow
        };

        _ = context.Items!.Add(reply);
        _ = await context.SaveChangesAsync();

        return await context.Items
            .Include(r => r.Owner)
            .Include(r => r.TagRelations)
            .ThenInclude(tr => tr.Tag)
            .FirstOrDefaultAsync(r => r.Id == reply.Id);
    }

    public async Task<IReadOnlyList<Item>> GetItemRepliesAsync(int parentItemId)
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

        _ = context.Items!.Add(replyItem);
        _ = await context.SaveChangesAsync();

        return await context.Items
            .Include(i => i.Owner)
            .FirstOrDefaultAsync(i => i.Id == replyItem.Id);
    }
}