using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;

// 兄弟名前空間との衝突を避けるためのエイリアス
using Tag = SRNSMudApp.Data.Tag;

namespace SRNSMudApp.Services;

/// <summary>タグ一括削除の結果。</summary>
public sealed record TagTreeDeleteResult(
    bool HasDeleted,
    int DeletedCount,
    List<string> UnauthorizedNames,
    List<string> SystemNames);

/// <summary>
///     TagTree コンポーネント用のデータアクセスを分離するインターフェース。
///     コンポーネントから DbContext への直接依存を断ち、単体テストでモック可能にする。
/// </summary>
public interface ITagTreeDataProvider
{
    /// <summary>システムタグ以外を読み込み、循環参照があれば検出して DB も修復する。</summary>
    Task<List<Tag>> LoadTagsAsync();

    Task<TagTreeDeleteResult> DeleteTagsAsync(string userId, IReadOnlyList<int> selectedIds);

    /// <summary>子タグを追加する。一意制約違反などは例外として伝搬する。</summary>
    Task AddTagAsync(Tag tag);

    /// <summary>タグの親を変更する。対象が存在しない場合は false。</summary>
    Task<bool> UpdateParentAsync(int tagId, int? parentTagId);
}

public class TagTreeDataProvider(IDbContextFactory<ApplicationDbContext> dbFactory) : ITagTreeDataProvider
{
    public async Task<List<Tag>> LoadTagsAsync()
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();
        List<Tag> tags = await context.Tags.Where(t => !t.IsSystem).ToListAsync();

        // 循環参照を検出して解除する（DB上のデータも修復する）
        List<Tag> repaired = Components.Tag.TagTreeViewModel.DetectAndBreakCycles(tags);
        foreach (Tag repairedTag in repaired)
        {
            Tag? dbTag = await context.Tags.FindAsync(repairedTag.Id);
            switch (dbTag)
            {
                case not null:
                    dbTag.ParentTagId = null;
                    break;
            }
        }

        switch (repaired.Count > 0)
        {
            case true:
                await context.SaveChangesAsync();
                break;
        }

        return tags;
    }

    public async Task<TagTreeDeleteResult> DeleteTagsAsync(string userId, IReadOnlyList<int> selectedIds)
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync();

        try
        {
            List<Tag> selectedTagsFromDb = await context.Tags.Where(t => selectedIds.Contains(t.Id)).ToListAsync();

            var unauthorizedNames = selectedTagsFromDb
                .Where(t => t.OwnerId != userId)
                .Select(t => t.Name)
                .ToList();
            var systemNames = selectedTagsFromDb
                .Where(t => t.IsSystem)
                .Select(t => t.Name)
                .ToList();
            var authorizedIds = selectedTagsFromDb
                .Where(t => t.OwnerId == userId && !t.IsSystem)
                .Select(t => t.Id)
                .ToList();

            var hasDeleted = false;
            var deletedCount = 0;

            switch (authorizedIds.Count > 0)
            {
                case true:
                    {
                        List<Tag> tagsToDelete = await context.Tags
                            .Where(t => authorizedIds.Contains(t.Id) && t.OwnerId == userId)
                            .ToListAsync();

                        switch (tagsToDelete.Count > 0)
                        {
                            case true:
                                {
                                    // 関連する TagRelationToTag (DeleteBehavior.Restrict) を手動で削除
                                    List<TagRelationToTag> relationsToDelete = await context.TagRelationToTags
                                        .Where(tr =>
                                            authorizedIds.Contains(tr.TagId) || authorizedIds.Contains(tr.TargetTagId))
                                        .ToListAsync();

                                    switch (relationsToDelete.Count > 0)
                                    {
                                        case true:
                                            context.TagRelationToTags.RemoveRange(relationsToDelete);
                                            break;
                                    }

                                    // 削除対象のタグを親に持つ子タグを取得し、ルートノード（ParentTagId = null）に変更する
                                    List<Tag> orphanedChildren = await context.Tags
                                        .Where(t => t.ParentTagId != null &&
                                                    authorizedIds.Contains(t.ParentTagId.Value) &&
                                                    !authorizedIds.Contains(t.Id))
                                        .ToListAsync();

                                    foreach (Tag child in orphanedChildren)
                                    {
                                        child.ParentTagId = null;
                                    }

                                    // 自己参照外部キー制約エラーを避けるため、一旦親タグ参照を解除して保存
                                    foreach (Tag tag in tagsToDelete)
                                    {
                                        tag.ParentTagId = null;
                                    }

                                    await context.SaveChangesAsync();

                                    // タグ本体の削除
                                    context.Tags.RemoveRange(tagsToDelete);
                                    await context.SaveChangesAsync();

                                    hasDeleted = true;
                                    deletedCount = tagsToDelete.Count;
                                    break;
                                }
                        }

                        await transaction.CommitAsync();
                        break;
                    }
            }

            return new TagTreeDeleteResult(hasDeleted, deletedCount, unauthorizedNames, systemNames);
        }
        catch (Exception)
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task AddTagAsync(Tag tag)
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();
        context.Tags.Add(tag);
        await context.SaveChangesAsync();
    }

    public async Task<bool> UpdateParentAsync(int tagId, int? parentTagId)
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();
        Tag? tagToUpdate = await context.Tags.FindAsync(tagId);
        switch (tagToUpdate)
        {
            case not null:
                tagToUpdate.ParentTagId = parentTagId;
                tagToUpdate.UpdatedDate = DateTime.UtcNow;
                await context.SaveChangesAsync();
                return true;
            default:
                return false;
        }
    }
}