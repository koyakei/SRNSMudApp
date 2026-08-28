#region

using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;

#endregion

namespace SRNSMudApp.Services;

/// <summary>
///     残りの管理・インポート系コンポーネント用のデータアクセスを分離するインターフェース。
///     コンポーネントから DbContext への直接依存を断ち、単体テストでモック可能にする。
/// </summary>
public interface IAdminDataProvider
{
    /// <summary>CSV (1列目: アイテム本文、2列目以降: タグ名) からアイテムとタグを一括インポートする。</summary>
    Task<int> ImportItemsWithTagsAsync(string userId, IReadOnlyList<string[]> linesToProcess);

    Task<List<Invitation>> GetInvitationsAsync();

    Task CreateInvitationAsync(Invitation invitation);

    Task DeleteInvitationAsync(Invitation invitation);
}

public class AdminDataProvider(IDbContextFactory<ApplicationDbContext> dbFactory) : IAdminDataProvider
{
    public async Task<int> ImportItemsWithTagsAsync(string userId, IReadOnlyList<string[]> linesToProcess)
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync();
        try
        {
            var allTagNames = linesToProcess
                .SelectMany(values => values.Skip(1))
                .Where(tagName => !string.IsNullOrWhiteSpace(tagName))
                .Distinct()
                .ToList();

            Dictionary<string, Tag> existingTags = await context.Tags
                .Where(t => t.OwnerId == userId && allTagNames.Contains(t.Name))
                .ToDictionaryAsync(t => t.Name, t => t);

            IEnumerable<string> newTagNames = allTagNames.Where(name => !existingTags.ContainsKey(name));
            List<Tag> newTags = [];
            foreach (var tagName in newTagNames)
            {
                var newTag = new Tag { Name = tagName, OwnerId = userId };
                newTags.Add(newTag);
                existingTags[tagName] = newTag;
            }

            if (newTags.Count != 0)
            {
                context.Tags.AddRange(newTags);
            }

            var importedItemCount = 0;
            foreach (var values in linesToProcess)
            {
                var content = values[0];
                var newItem = new Item
                {
                    Content = content,
                    OwnerId = userId,
                    TagRelations = []
                };
                _ = context.Items.Add(newItem);

                IEnumerable<string> tagNamesInLine =
                    values.Skip(1).Where(tn => !string.IsNullOrWhiteSpace(tn)).Distinct();
                foreach (var tagName in tagNamesInLine)
                {
                    if (!existingTags.TryGetValue(tagName, out Tag? tag))
                    {
                        continue;
                    }

                    var newRelation = new TagRelation
                    {
                        Item = newItem,
                        Tag = tag!,
                        Weight = 1,
                        OwnerId = userId
                    };
                    newItem.TagRelations.Add(newRelation);
                }

                importedItemCount++;
            }

            _ = await context.SaveChangesAsync();
            await transaction.CommitAsync();

            return importedItemCount;
        }
        catch (Exception)
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<List<Invitation>> GetInvitationsAsync()
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();
        return await context.Invitations!
            .OrderByDescending(i => i.CreatedDate)
            .ToListAsync();
    }

    public async Task CreateInvitationAsync(Invitation invitation)
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();
        _ = context.Invitations!.Add(invitation);
        _ = await context.SaveChangesAsync();
    }

    public async Task DeleteInvitationAsync(Invitation invitation)
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();
        _ = context.Invitations!.Remove(invitation);
        _ = await context.SaveChangesAsync();
    }
}