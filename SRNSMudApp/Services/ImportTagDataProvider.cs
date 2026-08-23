#region

using System.Numerics.Tensors;
using System.Text;
using System.Text.RegularExpressions;

using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;

using Tag = SRNSMudApp.Data.Tag;

#endregion

namespace SRNSMudApp.Services;

/// <summary>
///     ImportTag コンポーネント用のデータアクセスを分離するインターフェース。
///     コンポーネントから DbContext への直接依存を断ち、単体テストでモック可能にする。
/// </summary>
public interface IImportTagDataProvider
{
    /// <summary>ログインユーザー所有のタグを、テキスト + ベクトル類似度で検索する。</summary>
    Task<List<Tag>> SearchUserTagsAsync(string userId, string? value, CancellationToken token = default);

    /// <summary>
    ///     CSV の各行 (カンマ区切りのタグ名階層) を親タグ配下にインポートする。
    /// </summary>
    /// <returns>新規作成されたタグ数。</returns>
    Task<int> ImportCsvTagsAsync(string userId, string selectedParentTagName, string csvContent);
}

public class ImportTagDataProvider(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    ITagEmbeddingService tagEmbeddingService) : IImportTagDataProvider
{
    private static readonly Regex TagNameRegex = new(@"^[\x20-\x7E\u3000-\u30FF\u4E00-\u9FFF\uFF01-\uFF9F]+$");

    public async Task<List<Tag>> SearchUserTagsAsync(
        string userId,
        string? value,
        CancellationToken token = default)
    {
        await using ApplicationDbContext dbContext = await dbContextFactory.CreateDbContextAsync(token);
        IQueryable<Tag> query = dbContext.Tags.Where(t => t.OwnerId == userId).AsQueryable();

        switch (string.IsNullOrEmpty(value))
        {
            case true:
                return await query.AsNoTracking().Take(50).ToListAsync(token);
        }

        try
        {
            float[] queryVector = (await tagEmbeddingService.GenerateEmbeddingAsync(value!)).ToArray();

            List<Tag> textMatches = await query
                .Where(x => x.Name.Contains(value!) || (x.Content != null && x.Content.Contains(value!)))
                .AsNoTracking()
                .ToListAsync(token);

            List<Tag> vectorTags = await query.Where(x => x.Embedding != null).AsNoTracking().ToListAsync(token);

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
        catch (Exception ex)
        {
            Console.WriteLine($"Vector search failed: {ex.Message}");
            query = query.Where(x => x.Name.Contains(value!) || (x.Content != null && x.Content.Contains(value!)));
            return await query.AsNoTracking().Take(50).ToListAsync(token);
        }
    }

    public async Task<int> ImportCsvTagsAsync(
        string userId,
        string selectedParentTagName,
        string csvContent)
    {
        await using ApplicationDbContext dbContext = await dbContextFactory.CreateDbContextAsync();

        var lines = csvContent.Split(["\r\n", "\r", "\n"], StringSplitOptions.RemoveEmptyEntries);

        // Load existing tags for this user
        Dictionary<string, Tag> existingTags = await dbContext.Tags
            .Where(t => t.OwnerId == userId)
            .ToDictionaryAsync(t => t.Name, t => t);

        var createdCount = 0;

        // Begin transaction for safety
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction =
            await dbContext.Database.BeginTransactionAsync();

        try
        {
            Tag? baseParentTag = null;
            switch (existingTags.TryGetValue(selectedParentTagName, out Tag? trackedBaseTag))
            {
                case true:
                    baseParentTag = trackedBaseTag;
                    break;
            }

            foreach (var line in lines)
            {
                List<string> tagNames = line.Split(',').Select(t => t.Trim()).Where(t => !string.IsNullOrEmpty(t)).ToList();
                switch (tagNames.Count)
                {
                    case 0: continue;
                }

                Tag? currentParentTag = baseParentTag;

                foreach (var tagName in tagNames)
                {
                    // Validate tag name
                    switch (TagNameRegex.IsMatch(tagName))
                    {
                        case false:
                            throw new Exception($"不正なタグ名が含まれています: '{tagName}'");
                    }

                    switch (existingTags.TryGetValue(tagName, out Tag? tag))
                    {
                        case false:
                        {
                            // Tag doesn't exist, create it
                            var newTag = new Tag
                            {
                                Name = tagName,
                                OwnerId = userId,
                                ParentTag = currentParentTag,
                                CreatedDate = DateTime.UtcNow,
                                UpdatedDate = DateTime.UtcNow
                            };

                            try
                            {
                                var embedding = await tagEmbeddingService.GenerateEmbeddingAsync(tagName);
                                newTag.Embedding = embedding.ToArray();
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Embedding generation failed: {ex.Message}");
                            }

                            dbContext.Tags.Add(newTag);

                            existingTags[tagName] = newTag;
                            currentParentTag = newTag;
                            createdCount++;
                            continue;
                        }
                    }

                    switch (currentParentTag != null && !ReferenceEquals(tag.ParentTag, currentParentTag))
                    {
                        case true:
                            // Avoid circular reference
                            switch (!IsDescendantOrSelf(tag, currentParentTag!))
                            {
                                case true:
                                    tag.ParentTag = currentParentTag;
                                    break;
                            }

                            break;
                    }

                    currentParentTag = tag;
                }
            }

            await dbContext.SaveChangesAsync();
            await transaction.CommitAsync();

            return createdCount;
        }
        catch (Exception)
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static bool IsDescendantOrSelf(Tag parent, Tag target)
    {
        switch (ReferenceEquals(parent, target))
        {
            case true: return true;
        }

        Tag? current = target;
        while (current?.ParentTag != null)
        {
            switch (ReferenceEquals(current.ParentTag, parent))
            {
                case true: return true;
            }

            current = current.ParentTag;
        }

        return false;
    }
}
