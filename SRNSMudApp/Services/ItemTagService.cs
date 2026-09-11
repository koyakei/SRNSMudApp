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

public record SameTag;
public record DifferentTag;
[SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "Union type handled by C# compiler")]
public readonly union TagComparisonState(SameTag, DifferentTag);

public record SameWeight;
public record DifferentWeight;
[SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "Union type handled by C# compiler")]
public readonly union WeightComparisonState(SameWeight, DifferentWeight);

/// <summary>
///     アイテムとタグの関連付け（TagRelation）、タグ間の関連付け（TagRelationToTag）、
///     およびタグ付けリクエストに対する返信・タイムラインイベント・ウェイト台帳の管理を担当するドメインサービス。
/// </summary>
public class ItemTagService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    ITimelineRecorder timelineRecorder,
    ITagWeightLedgerService tagWeightLedgerService) : IItemTagService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
    private readonly ITimelineRecorder _timelineRecorder =
        timelineRecorder ?? throw new ArgumentNullException(nameof(timelineRecorder));
    private readonly ITagWeightLedgerService _tagWeightLedgerService =
        tagWeightLedgerService ?? throw new ArgumentNullException(nameof(tagWeightLedgerService));
    private static AuthorizationState CheckAuth(bool isAuthorized, string unauthMessage) =>
        isAuthorized switch
        {
            true => new OperationAuthorized(),
            false => new OperationUnauthorized(unauthMessage)
        };

    public async Task<string?> AddTagToItemAsync(int itemId, int tagId, string currentUserId)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();

        Tag? tagFromDb = await context.Tags.FirstOrDefaultAsync(t => t.Id == tagId);
        var tagOption = Option<Tag>.Create(tagFromDb);

        if (tagOption is not Some<Tag> someTag)
        {
            return "タグが見つかりません。";
        }

        AuthorizationState authorization = CheckAuth(
            someTag.Value.GetKind() is not UserCustomTag custom || custom.OwnerId == currentUserId,
            "タグの作成者ではないため、追加する権限がありません。");
        if (authorization is OperationUnauthorized unauthorized)
        {
            return unauthorized.Reason;
        }

        return await ProcessAddTagRelation(context, itemId, tagId, currentUserId, someTag.Value);
    }

    private async Task<string?> ProcessAddTagRelation(ApplicationDbContext context, int itemId, int tagId, string currentUserId, Tag tagFromDb)
    {
        var alreadyExists = await context.TagRelations.AnyAsync(tr => tr.ItemId == itemId && tr.TagId == tagId);
        if (alreadyExists)
        {
            return "このタグは既に追加されています。";
        }

        return await ExecuteAddTagRelationAsync(context, itemId, tagId, currentUserId, tagFromDb);
    }

    private async Task<string?> ExecuteAddTagRelationAsync(ApplicationDbContext context, int itemId, int tagId, string currentUserId, Tag tagFromDb)
    {
        await using var transaction = await context.Database.BeginTransactionAsync();
        try
        {
            var newRelation = new TagRelation { ItemId = itemId, TagId = tagId, Weight = 1, OwnerId = currentUserId };
            _ = context.TagRelations.Add(newRelation);

            _timelineRecorder.RecordTagRelationAdded(context, currentUserId, itemId, tagId, 1);

            _ = await context.SaveChangesAsync();

            _tagWeightLedgerService.RecordItemTagWeightChange(context, tagFromDb, itemId, "TagRelationInsert", newRelation.Id, 1, "タグの新規追加", currentUserId);

            _ = await context.SaveChangesAsync();
            await transaction.CommitAsync();
            return null;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<string?> RemoveTagRelationAsync(int relationId, string currentUserId)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();

        TagRelation? relation = await context.TagRelations.FindAsync(relationId);
        var relationOption = Option<TagRelation>.Create(relation);

        if (relationOption is not Some<TagRelation> someRelation)
        {
            return "タグの関連付けが見つかりません。";
        }

        AuthorizationState authorization = CheckAuth(someRelation.Value.OwnerId == currentUserId, "関連付けた本人ではないため、解除する権限がありません。");
        if (authorization is OperationUnauthorized unauthorized)
        {
            return unauthorized.Reason;
        }

        return await ExecuteRemoveTagRelationAsync(context, someRelation.Value, currentUserId);
    }

    private async Task<string?> ExecuteRemoveTagRelationAsync(ApplicationDbContext context, TagRelation relation, string currentUserId)
    {
        await using var transaction = await context.Database.BeginTransactionAsync();
        try
        {
            _timelineRecorder.RecordTagRelationDeleted(context, currentUserId, relation.ItemId, relation.TagId, relation.Weight);

            Tag? tag = await context.Tags.FindAsync(relation.TagId);
            var tagOption = Option<Tag>.Create(tag);

            if (tagOption is Some<Tag> someTag)
            {
                _tagWeightLedgerService.RecordItemTagWeightChange(context, someTag.Value, relation.ItemId, "TagRelationDelete", relation.Id, -relation.Weight, "タグの削除", currentUserId);
            }

            _ = context.Remove(relation);
            _ = await context.SaveChangesAsync();
            await transaction.CommitAsync();
            return null;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<UpdateWeightResult> UpdateTagWeightAsync(int relationId, int delta, string currentUserId)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();

        TagRelation? entity = await context.TagRelations.FindAsync(relationId);
        var entityOption = Option<TagRelation>.Create(entity);

        if (entityOption is not Some<TagRelation> someRelation)
        {
            return UpdateWeightResult.NotFound;
        }

        if (CheckAuth(someRelation.Value.OwnerId == currentUserId, "") is OperationUnauthorized)
        {
            return UpdateWeightResult.NoPermission;
        }

        return await ExecuteUpdateTagWeightAsync(context, someRelation.Value, delta, currentUserId);
    }

    private async Task<UpdateWeightResult> ExecuteUpdateTagWeightAsync(ApplicationDbContext context, TagRelation entity, int delta, string currentUserId)
    {
        entity.Weight += delta;
        entity.UpdatedDate = DateTime.UtcNow;

        Tag? tag = await context.Tags.FindAsync(entity.TagId);
        var tagOption = Option<Tag>.Create(tag);

        if (tagOption is Some<Tag> someTag)
        {
            ProcessTagWeightUpdate(context, someTag.Value, entity, delta, currentUserId);
        }

        _ = await context.SaveChangesAsync();
        return UpdateWeightResult.Success;
    }

    private void ProcessTagWeightUpdate(ApplicationDbContext context, Tag tag, TagRelation entity, int delta, string currentUserId)
    {
        _tagWeightLedgerService.RecordItemTagWeightChange(context, tag, entity.ItemId, "TagRelationUpdate", entity.Id, delta, "ユーザーによる直接ウェイト変更", currentUserId);

        _timelineRecorder.RecordTagRelationUpdated(context, currentUserId, entity.ItemId, entity.TagId, entity.Weight - delta, entity.Weight);
    }

    public async Task<string?> SetTagWeightAsync(int relationId, int newWeight, string currentUserId)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();

        TagRelation? entity = await context.TagRelations.FindAsync(relationId);
        var entityOption = Option<TagRelation>.Create(entity);

        if (entityOption is not Some<TagRelation> someRelation)
        {
            return "タグの関連付けが見つかりません。";
        }

        AuthorizationState authorization = CheckAuth(someRelation.Value.OwnerId == currentUserId, "関連付けた本人ではないため、Weightを変更する権限がありません。");
        if (authorization is OperationUnauthorized unauthorized)
        {
            return unauthorized.Reason;
        }

        if (someRelation.Value.Weight == newWeight)
        {
            return null;
        }

        return await ExecuteSetTagWeightAsync(context, someRelation.Value, newWeight, currentUserId);
    }

    private async Task<string?> ExecuteSetTagWeightAsync(ApplicationDbContext context, TagRelation entity, int newWeight, string currentUserId)
    {
        var delta = newWeight - entity.Weight;
        entity.Weight = newWeight;
        entity.UpdatedDate = DateTime.UtcNow;

        Tag? tag = await context.Tags.FindAsync(entity.TagId);
        var tagOption = Option<Tag>.Create(tag);

        if (tagOption is Some<Tag> someTag)
        {
            _tagWeightLedgerService.RecordItemTagWeightChange(context, someTag.Value, entity.ItemId, "TagRelationUpdate", entity.Id, delta, "ユーザーによる直接ウェイト一括変更", currentUserId);
        }

        _ = await context.SaveChangesAsync();
        return null;
    }

    public async Task<string?> ChangeItemTagAsync(int relationId, int newTagId, int itemId, string currentUserId)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();

        TagRelation? entity = await context.TagRelations.FindAsync(relationId);
        var entityOption = Option<TagRelation>.Create(entity);

        if (entityOption is not Some<TagRelation> someRelation)
        {
            return "タグの関連付けが見つかりません。";
        }

        AuthorizationState authorization = CheckAuth(someRelation.Value.OwnerId == currentUserId, "関連付けた本人ではないため、変更する権限がありません。");
        if (authorization is OperationUnauthorized unauthorized)
        {
            return unauthorized.Reason;
        }

        if (someRelation.Value.TagId == newTagId)
        {
            return null;
        }

        return await ProcessChangeItemTagRelation(context, someRelation.Value, newTagId, itemId);
    }

    private static async Task<string?> ProcessChangeItemTagRelation(ApplicationDbContext context, TagRelation entity, int newTagId, int itemId)
    {
        var alreadyExists = await context.TagRelations.AnyAsync(tr => tr.ItemId == itemId && tr.TagId == newTagId);
        if (alreadyExists)
        {
            return "変更先のタグは既に追加されています。";
        }

        return await ExecuteChangeItemTagAsync(context, entity, newTagId);
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
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();

        var alreadyExists = await context.TagRelationToTags.AnyAsync(tr => tr.TargetTagId == targetTagId && tr.TagId == tagId);

        if (alreadyExists)
        {
            return "このタグは既に追加されています。";
        }

        return await ExecuteAddTagToTagAsync(context, targetTagId, tagId, currentUserId);
    }

    private async Task<string?> ExecuteAddTagToTagAsync(ApplicationDbContext context, int targetTagId, int tagId, string currentUserId)
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

        if (tagOption is Some<Tag> someTag)
        {
            await ExecuteAddTagToTagLedgerAsync(context, someTag.Value, targetTagId, currentUserId);
        }

        return null;
    }

    private async Task ExecuteAddTagToTagLedgerAsync(ApplicationDbContext context, Tag tagFromDb, int targetTagId, string currentUserId)
    {
        _tagWeightLedgerService.RecordTagToTagWeightChange(context, tagFromDb, targetTagId, "TagRelationToTagInsert", null, 1, "タグの新規追加", currentUserId);
        _ = await context.SaveChangesAsync();
    }

    public async Task<string?> RemoveTagToTagRelationAsync(int relationId, string currentUserId)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();

        TagRelationToTag? entity = await context.TagRelationToTags.FindAsync(relationId);
        var entityOption = Option<TagRelationToTag>.Create(entity);

        if (entityOption is not Some<TagRelationToTag> someRelation)
        {
            return "タグの関連付けが見つかりません。";
        }

        AuthorizationState authorization = CheckAuth(someRelation.Value.OwnerId == currentUserId, "関連付けた本人ではないため、解除する権限がありません。");
        if (authorization is OperationUnauthorized unauthorized)
        {
            return unauthorized.Reason;
        }

        return await ExecuteRemoveTagToTagRelationAsync(context, someRelation.Value, currentUserId);
    }

    private async Task<string?> ExecuteRemoveTagToTagRelationAsync(ApplicationDbContext context, TagRelationToTag entity, string currentUserId)
    {
        Tag? tag = await context.Tags.FindAsync(entity.TagId);
        var tagOption = Option<Tag>.Create(tag);

        if (tagOption is Some<Tag> someTag)
        {
            _tagWeightLedgerService.RecordTagToTagWeightChange(context, someTag.Value, entity.TargetTagId, "TagRelationToTagDelete", null, -entity.Weight, "タグの関連付け解除", currentUserId);
        }

        _ = context.Remove(entity);
        _ = await context.SaveChangesAsync();
        return null;
    }

    public async Task<string?> SetParentTagAsync(int parentTagId, int childTagId, string currentUserId, IReadOnlyList<Tag> allTagsForCycleCheck)
    {
        if (childTagId == parentTagId)
        {
            return "自分自身を親にすることはできません。";
        }

        return await ProcessParentTagCycleCheck(parentTagId, childTagId, currentUserId, allTagsForCycleCheck);
    }

    private async Task<string?> ProcessParentTagCycleCheck(int parentTagId, int childTagId, string currentUserId, IReadOnlyList<Tag> allTagsForCycleCheck)
    {
        var hasCycle = false;
        Tag? parentTag = allTagsForCycleCheck.FirstOrDefault(t => t.Id == parentTagId);
        var current = parentTag?.ParentTagId;

        while (current != null)
        {
            if (current == childTagId)
            {
                hasCycle = true;
            }

            current = hasCycle ? null : allTagsForCycleCheck.FirstOrDefault(t => t.Id == current)?.ParentTagId;
        }

        if (hasCycle)
        {
            return "循環参照になるため親に設定できません。";
        }

        return await ExecuteSetParentTagAsync(parentTagId, childTagId, currentUserId);
    }

    private async Task<string?> ExecuteSetParentTagAsync(int parentTagId, int childTagId, string currentUserId)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();
        Tag? entity = await context.Tags.FindAsync(childTagId);
        var entityOption = Option<Tag>.Create(entity);

        if (entityOption is not Some<Tag> someEntity)
        {
            return "対象タグが見つかりません。";
        }

        AuthorizationState authorization = CheckAuth(someEntity.Value.OwnerId == currentUserId, "対象タグの作成者ではないため、親タグを変更する権限がありません。");
        if (authorization is OperationUnauthorized unauthorized)
        {
            return unauthorized.Reason;
        }

        return await ProcessSaveParentTag(context, someEntity.Value, parentTagId);
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
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();
        return await context.TaggingRequestEntities!
            .Include(tr => tr.Target)
            .ThenInclude(t => t.Item)
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
            .Where(tr => tr.Target.Item.Id == itemId)
            .OrderByDescending(tr => tr.CreatedDate)
            .ToListAsync();
    }
}