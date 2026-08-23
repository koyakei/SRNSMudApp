#region

using System.Numerics.Tensors;

using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;

// 名前空間の内側でエイリアスして Data.Tag を確実に解決させる
using SRNSMudApp.Data;

using Tag = SRNSMudApp.Data.Tag;

#endregion

namespace SRNSMudApp.Services;

/// <summary>
///     タグ選択・作成ダイアログ (TagAddDialog) 用のデータアクセスを分離するインターフェース。
///     コンポーネントから DbContext への直接依存を断ち、単体テストでモック可能にする。
/// </summary>
public interface ITagDialogDataProvider
{
    Task<List<Tag>> GetAllTagsAsync();

    /// <summary>全タグを対象にテキスト + ベクトル類似度で検索する。</summary>
    Task<List<Tag>> SearchTagsAsync(string searchText);

    Task<Tag?> FindTagByNameAsync(string tagName);

    Task CreateTagAsync(Tag newTag);
}

public class TagDialogDataProvider(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    ITagEmbeddingService tagEmbeddingService) : ITagDialogDataProvider
{
    public async Task<List<Tag>> GetAllTagsAsync()
    {
        await using ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync();
        return await dbContext.Tags.AsNoTracking().ToListAsync();
    }

    public async Task<List<Tag>> SearchTagsAsync(string searchText)
    {
        float[] queryVector = (await tagEmbeddingService.GenerateEmbeddingAsync(searchText)).ToArray();

        await using ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync();

        List<Tag> textMatches = await dbContext.Tags
            .Where(x => x.Name.Contains(searchText) || x.Content.Contains(searchText))
            .AsNoTracking()
            .ToListAsync();

        List<Tag> vectorTags = await dbContext.Tags.Where(x => x.Embedding != null).AsNoTracking().ToListAsync();

        List<Tag> vectorMatches = vectorTags
            .Where(x => x.Embedding.Length == queryVector.Length)
            .OrderByDescending(x => TensorPrimitives.CosineSimilarity(x.Embedding, queryVector))
            .Take(50)
            .ToList();

        return
        [
            .. textMatches.Concat(vectorMatches)
                .DistinctBy(x => x.Id)
                .Take(50)
        ];
    }

    public async Task<Tag?> FindTagByNameAsync(string tagName)
    {
        await using ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync();
        return await dbContext.Tags.FirstOrDefaultAsync(t => t.Name == tagName);
    }

    public async Task CreateTagAsync(Tag newTag)
    {
        try
        {
            var embedding =
                await tagEmbeddingService.GenerateEmbeddingAsync($"{newTag.Name} {newTag.Content}");
            newTag.Embedding = embedding.ToArray();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Embedding generation failed: {ex.Message}");
        }

        await using ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync();
        dbContext.Tags.Add(newTag);
        await dbContext.SaveChangesAsync();
    }
}
