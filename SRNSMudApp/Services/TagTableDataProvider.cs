#region

using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;

#endregion

namespace SRNSMudApp.Services;

/// <summary>
///     TagTable コンポーネント用のデータアクセスを分離するインターフェース。
///     コンポーネントから DbContext への直接依存を断ち、単体テストでモック可能にする。
/// </summary>
public interface ITagTableDataProvider
{
    Task<List<Tag>> GetAllTagsAsync();

    /// <summary>タグ間リレーションを追加する。既存の場合は <see cref="TagCardOperationResult.AlreadyExists" />。</summary>
    Task<TagCardOperationResult> AddRelationAsync(int targetTagId, int selectedTagId, string ownerId);

    /// <summary>タグ間リレーションを解除する。</summary>
    Task<TagCardOperationResult> RemoveRelationAsync(int relationId);

    /// <summary>タグを削除する。存在しない場合は false。</summary>
    Task<bool> DeleteTagAsync(int tagId);
}

public class TagTableDataProvider(IDbContextFactory<ApplicationDbContext> dbFactory) : ITagTableDataProvider
{
    public async Task<List<Tag>> GetAllTagsAsync()
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();
        return await context.Tags.AsNoTracking().ToListAsync();
    }

    public async Task<TagCardOperationResult> AddRelationAsync(int targetTagId, int selectedTagId, string ownerId)
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
                TargetTagId = targetTagId,
                SourceType = "TagRelationToTagInsert",
                SourceId = null,
                PreviousWeight = prevWeight,
                NewWeight = tagFromDb.CachedWeight,
                Delta = 1,
                IsOwnerAction = true,
                Reason = "タグにタグを追加",
                OwnerId = ownerId
            });
            _ = await context.SaveChangesAsync();
        }

        return TagCardOperationResult.Success;
    }

    public async Task<TagCardOperationResult> RemoveRelationAsync(int relationId)
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();
        TagRelationToTag? entity = await context.TagRelationToTags.Include(tr => tr.Tag)
            .FirstOrDefaultAsync(tr => tr.Id == relationId);
        if (entity is null)
        {
            return TagCardOperationResult.NotFound;
        }

        if (entity.Tag is not null)
        {
            Tag tag = entity.Tag;
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
                OwnerId = entity.OwnerId
            });
        }

        _ = context.Remove(entity);
        _ = await context.SaveChangesAsync();
        return TagCardOperationResult.Success;
    }

    public async Task<bool> DeleteTagAsync(int tagId)
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();
        Tag? tagToDelete = await context.Tags.FindAsync(tagId);
        switch (tagToDelete)
        {
            case not null:
                _ = context.Tags.Remove(tagToDelete);
                _ = await context.SaveChangesAsync();
                return true;
            default:
                return false;
        }
    }
}