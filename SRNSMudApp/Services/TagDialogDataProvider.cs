#region

using System.Diagnostics.CodeAnalysis;
using System.Numerics.Tensors;

using Microsoft.EntityFrameworkCore;

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

    /// <summary>ベクトルを生成せずにタグを作成する (旧 AddTag ページの挙動を維持)。</summary>
    Task CreateTagWithoutEmbeddingAsync(Tag newTag);

    /// <summary>タグ名・内容を更新し、ベクトルを再生成する。対象が存在しない場合は false。</summary>
    Task<bool> UpdateTagAsync(int tagId, string name, string? content);

    /// <summary>全タグを対象にテキスト+ベクトル検索を行う (失敗時はテキスト検索にフォールバック、最大 50 件)。</summary>
    Task<List<Tag>> SearchTagsWithFallbackAsync(string? value, CancellationToken token = default);

    /// <summary>タグ一覧 (Owner / TargetTagRelations 込み) を取得する。</summary>
    Task<List<Tag>> GetTagsWithDetailsAsync();
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
        var queryVector = (await tagEmbeddingService.GenerateEmbeddingAsync(searchText)).ToArray();

        await using ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync();

        List<Tag> textMatches = await dbContext.Tags
            .Where(x => x.Name.Contains(searchText) || x.Content.Contains(searchText))
            .AsNoTracking()
            .ToListAsync();

        List<Tag> vectorTags = await dbContext.Tags.Where(x => x.Embedding != null).AsNoTracking().ToListAsync();

        var vectorMatches = vectorTags
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

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "ユーザー入力由来の任意の例外を UI 向けメッセージに変換するため広く捕捉する")]
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
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "ユーザー入力由来の任意の例外を UI 向けメッセージに変換するため広く捕捉する")]
    public async Task<bool> UpdateTagAsync(int tagId, string name, string? content)
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();
        Tag? tagToUpdate = await context.Tags.FindAsync(tagId);
        if (tagToUpdate is null)
        {
            return false;
        }

        tagToUpdate.Name = name;
        tagToUpdate.Content = content ?? "";

        // タグ名が変更された場合などに備え、ベクトルも再生成する
        try
        {
            var embedding = await tagEmbeddingService.GenerateEmbeddingAsync(name);
            tagToUpdate.Embedding = embedding.ToArray();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to generate embedding on edit: {ex.Message}");
        }

        await context.SaveChangesAsync();
        return true;
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "ユーザー入力由来の任意の例外を UI 向けメッセージに変換するため広く捕捉する")]
    public async Task<List<Tag>> SearchTagsWithFallbackAsync(string? value, CancellationToken token = default)
    {
        await using ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync(token);
        IQueryable<Tag> query = dbContext.Tags.AsQueryable();

        if (string.IsNullOrEmpty(value))
        {
            return await query.AsNoTracking().Take(50).ToListAsync(token);
        }

        try
        {
            var queryVector = (await tagEmbeddingService.GenerateEmbeddingAsync(value)).ToArray();

            List<Tag> textMatches = await query
                .Where(x => x.Name.Contains(value) || x.Content.Contains(value))
                .AsNoTracking()
                .ToListAsync(token);

            List<Tag> vectorTags = await query.Where(x => x.Embedding != null).AsNoTracking().ToListAsync(token);

            var vectorMatches = vectorTags
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
        catch (Exception ex)
        {
            Console.WriteLine($"Vector search failed: {ex.Message}");
            query = query.Where(x =>
                x.Name.Contains(value) ||
                x.Content.Contains(value)
            );
            return await query.AsNoTracking().Take(50).ToListAsync(token);
        }
    }

    public async Task<List<Tag>> GetTagsWithDetailsAsync()
    {
        await using ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync();
        return await dbContext.Tags
            .Include(t => t.Owner)
            .Include(t => t.TargetTagRelations)
            .ThenInclude(tr => tr.Tag)
            .ThenInclude(t => t.Owner)
            .AsNoTracking()
            .ToListAsync();
    }
    public async Task CreateTagWithoutEmbeddingAsync(Tag newTag)
    {
        await using ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync();
        dbContext.Tags.Add(newTag);
        await dbContext.SaveChangesAsync();
    }
}