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
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
    public async Task<int> ImportItemsWithTagsAsync(string userId, IReadOnlyList<string[]> linesToProcess)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();

        return await context.Database.ExecuteWithStrategyAsync(async () =>
        {
            var allTagNames = linesToProcess
                .SelectMany(values => values.Skip(1))
                .Where(tagName => !string.IsNullOrWhiteSpace(tagName))
                .Distinct()
                .ToList();

            Dictionary<string, Tag> existingTags = await context.Tags
                .Where(t => t.OwnerId == userId && allTagNames.Contains(t.Name))
                .ToDictionaryAsync(t => t.Name, t => t);

            // 存在しない新規タグを宣言的に生成し、検索マップとコンテキストに追加
            List<Tag> newTags = [.. allTagNames
                .Where(name => !existingTags.ContainsKey(name))
                .Select(tagName => new Tag { Name = tagName, OwnerId = userId })];

            foreach (var tag in newTags)
            {
                existingTags[tag.Name] = tag;
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
                    OwnerId = userId
                };
                _ = context.Items.Add(newItem);

                // アイテムに関連付けるタグを抽出し、TagRelation を生成して設定
                newItem.TagRelations = [.. values.Skip(1)
                    .Where(tn => !string.IsNullOrWhiteSpace(tn))
                    .Distinct()
                    .Select(tn => existingTags.GetValueOrDefault(tn))
                    .Where(tag => tag is not null)
                    .Select(tag => new TagRelation
                    {
                        Item = newItem,
                        Tag = tag!,
                        Weight = 1,
                        OwnerId = userId
                    })];

                importedItemCount++;
            }

            _ = await context.SaveChangesAsync();

            return importedItemCount;
        });
    }

    public async Task<List<Invitation>> GetInvitationsAsync()
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();
        return await context.Invitations!
            .OrderByDescending(i => i.CreatedDate)
            .ToListAsync();
    }

    public async Task CreateInvitationAsync(Invitation invitation)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();
        _ = context.Invitations!.Add(invitation);
        _ = await context.SaveChangesAsync();
    }

    public async Task DeleteInvitationAsync(Invitation invitation)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();
        _ = context.Invitations!.Remove(invitation);
        _ = await context.SaveChangesAsync();
    }
}