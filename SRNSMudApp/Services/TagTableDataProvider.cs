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
    Task<List<Data.Tag>> GetAllTagsAsync();

    /// <summary>タグ間リレーションを追加する。既存の場合は <see cref="TagCardOperationResult.AlreadyExists" />。</summary>
    Task<TagCardOperationResult> AddRelationAsync(int targetTagId, int selectedTagId, string ownerId);

    /// <summary>タグ間リレーションを解除する。</summary>
    Task<TagCardOperationResult> RemoveRelationAsync(int relationId);

    /// <summary>タグを削除する。存在しない場合は false。</summary>
    Task<bool> DeleteTagAsync(int tagId);
}

public class TagTableDataProvider(IDbContextFactory<ApplicationDbContext> dbFactory) : ITagTableDataProvider
{
    public async Task<List<Data.Tag>> GetAllTagsAsync()
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();
        return await context.Tags.AsNoTracking().ToListAsync();
    }

    public async Task<TagCardOperationResult> AddRelationAsync(int targetTagId, int selectedTagId, string ownerId)
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();
        var alreadyExists = await context.TagRelationToTags
            .AnyAsync(tr => tr.TargetTagId == targetTagId && tr.TagId == selectedTagId);
        switch (alreadyExists)
        {
            case true:
                return TagCardOperationResult.AlreadyExists;
        }

        var newRelation = new Data.TagRelationToTag
        {
            TargetTagId = targetTagId,
            TagId = selectedTagId,
            Weight = 1,
            OwnerId = ownerId
        };

        context.Set<Data.TagRelationToTag>().Add(newRelation);
        await context.SaveChangesAsync();

        Data.Tag? tagFromDb = await context.Tags.FindAsync(selectedTagId);
        switch (tagFromDb)
        {
            case not null:
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
                    Reason = "タグにタグを追加",
                    OwnerId = ownerId
                });
                await context.SaveChangesAsync();
                break;
        }

        return TagCardOperationResult.Success;
    }

    public async Task<TagCardOperationResult> RemoveRelationAsync(int relationId)
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();
        Data.TagRelationToTag? entity = await context.TagRelationToTags.Include(tr => tr.Tag)
            .FirstOrDefaultAsync(tr => tr.Id == relationId);
        switch (entity)
        {
            case null:
                return TagCardOperationResult.NotFound;
        }

        switch (entity.Tag)
        {
            case not null:
            {
                Data.Tag tag = entity.Tag;
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
                    OwnerId = entity.OwnerId
                });
                break;
            }
        }

        context.Remove(entity);
        await context.SaveChangesAsync();
        return TagCardOperationResult.Success;
    }

    public async Task<bool> DeleteTagAsync(int tagId)
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();
        Data.Tag? tagToDelete = await context.Tags.FindAsync(tagId);
        switch (tagToDelete)
        {
            case not null:
                context.Tags.Remove(tagToDelete);
                await context.SaveChangesAsync();
                return true;
            default:
                return false;
        }
    }
}
