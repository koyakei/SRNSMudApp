#region

using System.Diagnostics.CodeAnalysis;
using System.Numerics.Tensors;
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
    /// <summary>ログインユーザー所有のタグおよびシステムタグを、テキスト + ベクトル類似度で検索する。</summary>
    Task<IReadOnlyList<Tag>> SearchUserTagsAsync(string userId, string? value, CancellationToken token = default);

    /// <summary>
    ///     CSV の各行 (カンマ区切りのタグ名階層) を親タグ配下にインポートする。
    /// </summary>
    /// <param name="userId">実行ユーザーID。</param>
    /// <param name="selectedParentTagName">親タグ名。</param>
    /// <param name="csvContent">CSVデータ。</param>
    /// <param name="asSystem">true の場合、システムタグ（Owner: "system", IsSystem: true）としてインポートする。</param>
    /// <returns>新規作成されたタグ数。</returns>
    Task<int> ImportCsvTagsAsync(string userId, string selectedParentTagName, string csvContent, bool asSystem = false);
}

public partial class ImportTagDataProvider(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    ITagEmbeddingService tagEmbeddingService) : IImportTagDataProvider
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory =
        dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
    private readonly ITagEmbeddingService _tagEmbeddingService =
        tagEmbeddingService ?? throw new ArgumentNullException(nameof(tagEmbeddingService));

    [GeneratedRegex(@"^[\x20-\x7E\u3000-\u30FF\u4E00-\u9FFF\uFF01-\uFF9F\u2200-\u22FF]+$")]
    private static partial Regex TagNameRegex();

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "ユーザー入力由来の任意の例外を UI 向けメッセージに変換するため広く捕捉する")]
    public async Task<IReadOnlyList<Tag>> SearchUserTagsAsync(
        string userId,
        string? value,
        CancellationToken token = default)
    {
        if (token.IsCancellationRequested)
        {
            return [];
        }

        await using ApplicationDbContext dbContext = await _dbContextFactory.CreateDbContextAsync(token);
        IQueryable<Tag> query = dbContext.Tags
            .Where(t => t.OwnerId == userId || t.IsSystem || t.OwnerId == "system")
            .AsQueryable();

        if (string.IsNullOrEmpty(value))
        {
            return await query.OrderBy(t => t.Name).AsNoTracking().Take(50).ToListAsync(token);
        }

        try
        {
            var queryVector = (await _tagEmbeddingService.GenerateEmbeddingAsync(value!)).ToArray();

            List<Tag> textMatches = await query
                .Where(x => x.Name.Contains(value!) || (x.Content != null && x.Content.Contains(value!)))
                .OrderBy(x => x.Name)
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
        catch (OperationCanceledException)
        {
            return [];
        }
        catch (Exception ex)
        {
            if (token.IsCancellationRequested)
            {
                return [];
            }

            Console.WriteLine($"Vector search failed: {ex.Message}");
            query = query.Where(x => x.Name.Contains(value!) || (x.Content != null && x.Content.Contains(value!)));
            return await query.OrderBy(x => x.Name).AsNoTracking().Take(50).ToListAsync(token);
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "ユーザー入力由来の任意の例外を UI 向けメッセージに変換するため広く捕捉する")]
    public async Task<int> ImportCsvTagsAsync(
        string userId,
        string selectedParentTagName,
        string csvContent,
        bool asSystem = false)
    {
        await using ApplicationDbContext dbContext = await _dbContextFactory.CreateDbContextAsync();

        var lines = csvContent.Split(["\r\n", "\r", "\n"], StringSplitOptions.RemoveEmptyEntries);

        var effectiveOwnerId = asSystem ? "system" : userId;

        // Load existing tags for this user / system
        Dictionary<string, Tag> existingTags = await dbContext.Tags
            .Where(t => t.OwnerId == effectiveOwnerId)
            .ToDictionaryAsync(t => t.Name, t => t);

        var createdCount = 0;

        // Begin transaction for safety
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction =
            await dbContext.Database.BeginTransactionAsync();

        try
        {
            Tag? baseParentTag = existingTags.TryGetValue(selectedParentTagName, out Tag? trackedBaseTag)
                ? trackedBaseTag
                : await dbContext.Tags.FirstOrDefaultAsync(t =>
                    (t.OwnerId == userId || t.IsSystem || t.OwnerId == "system") && t.Name == selectedParentTagName)
                    ?? await dbContext.Tags.FirstOrDefaultAsync(t => t.Name == Tag.RootTagName);

            foreach (var line in lines)
            {
                var tagNames = line.Split(',').Select(t => t.Trim()).Where(t => !string.IsNullOrEmpty(t)).ToList();
                switch (tagNames.Count)
                {
                    case 0: continue;
                    default:
                        break;
                }

                Tag? currentParentTag = baseParentTag;

                foreach (var tagName in tagNames)
                {
                    // Validate tag name
                    if (!TagNameRegex().IsMatch(tagName))
                    {
                        throw new InvalidOperationException($"不正なタグ名が含まれています: '{tagName}'");
                    }

                    if (!existingTags.TryGetValue(tagName, out Tag? tag))
                    {
                        {
                            HierarchyId? lastChildNode = currentParentTag == null
                                ? null
                                : await dbContext.Tags
                                    .Where(t => t.ParentTagId == currentParentTag.Id || t.Node.GetAncestor(1) == currentParentTag.Node)
                                    .OrderByDescending(t => t.Node)
                                    .Select(t => (HierarchyId?)t.Node)
                                    .FirstOrDefaultAsync();

                            // Tag doesn't exist, create it
                            var newTag = new Tag
                            {
                                Name = tagName,
                                OwnerId = effectiveOwnerId,
                                IsSystem = asSystem,
                                ParentTagId = currentParentTag?.Id,
                                Node = currentParentTag == null
                                    ? HierarchyId.GetRoot()
                                    : currentParentTag.Node.GetDescendant(lastChildNode, null),
                                CreatedDate = DateTime.UtcNow,
                                UpdatedDate = DateTime.UtcNow
                            };

                            try
                            {
                                ReadOnlyMemory<float> embedding = await _tagEmbeddingService.GenerateEmbeddingAsync(tagName);
                                newTag.Embedding = embedding.ToArray();
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Embedding generation failed: {ex.Message}");
                            }

                            _ = dbContext.Tags.Add(newTag);
                            _ = await dbContext.SaveChangesAsync();

                            existingTags[tagName] = newTag;
                            currentParentTag = newTag;
                            createdCount++;
                            continue;
                        }
                    }

                    if (currentParentTag != null && tag.ParentTagId != currentParentTag.Id)
                    {
                        // Avoid circular reference
                        if (!IsDescendantOrSelf(tag, currentParentTag!))
                        {
                            HierarchyId? lastChildNode = await dbContext.Tags
                                .Where(t => t.ParentTagId == currentParentTag.Id || t.Node.GetAncestor(1) == currentParentTag.Node)
                                .OrderByDescending(t => t.Node)
                                .Select(t => (HierarchyId?)t.Node)
                                .FirstOrDefaultAsync();

                            tag.ParentTagId = currentParentTag.Id;
                            tag.Node = currentParentTag.Node.GetDescendant(lastChildNode, null);
                        }
                    }

                    currentParentTag = tag;
                }
            }

            _ = await dbContext.SaveChangesAsync();
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
        => ReferenceEquals(parent, target) || parent.Id == target.Id || target.Node.IsDescendantOf(parent.Node);
}