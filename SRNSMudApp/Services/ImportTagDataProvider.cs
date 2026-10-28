#pragma warning disable CA1848

#region

using System.Diagnostics.CodeAnalysis;
using System.Numerics.Tensors;
using System.Text;
using System.Text.RegularExpressions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using SRNSMudApp.Data;

using Tag = SRNSMudApp.Data.Tag;

#endregion

namespace SRNSMudApp.Services;

/// <summary>
///     タグインポートの結果（新規作成件数、更新件数）。
///     既存コードとの互換性のため int への暗黙の型変換（CreatedCount）をサポートする。
/// </summary>
public sealed record TagImportResult(int CreatedCount, int UpdatedCount)
{
    public static implicit operator int(TagImportResult result) => result?.CreatedCount ?? 0;
    public int ToInt32() => CreatedCount;
}

/// <summary>
///     ImportTag コンポーネント用のデータアクセスを分離するインターフェース。
///     コンポーネントから DbContext への直接依存を断ち、単体テストでモック可能にする。
/// </summary>
public interface IImportTagDataProvider
{
    /// <summary>ログインユーザー所有のタグおよびシステムタグを、テキスト + ベクトル類似度で検索する。</summary>
    Task<IReadOnlyList<Tag>> SearchUserTagsAsync(string userId, string? value, CancellationToken token = default);

    /// <summary>
    ///     CSV / TSV の各行 (カンマまたはタブ区切りのタグ名階層) を親タグ配下にインポートする。
    /// </summary>
    /// <param name="userId">実行ユーザーID。</param>
    /// <param name="selectedParentTagName">親タグ名。</param>
    /// <param name="csvContent">CSV/TSVデータ。</param>
    /// <param name="asSystem">true の場合、システムタグ（Owner: "system", IsSystem: true）としてインポートする。</param>
    /// <returns>新規作成および更新されたタグ数の結果オブジェクト。</returns>
    Task<TagImportResult> ImportCsvTagsAsync(string userId, string selectedParentTagName, string csvContent, bool asSystem = false);
}

public partial class ImportTagDataProvider(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    ITagEmbeddingService tagEmbeddingService,
    ILogger<ImportTagDataProvider>? logger = null) : IImportTagDataProvider
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory =
        dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
    private readonly ITagEmbeddingService _tagEmbeddingService =
        tagEmbeddingService ?? throw new ArgumentNullException(nameof(tagEmbeddingService));
    private readonly ILogger<ImportTagDataProvider> _logger =
        logger ?? NullLogger<ImportTagDataProvider>.Instance;

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

            _logger.LogWarning(ex, "Vector search failed: {Message}", ex.Message);
            query = query.Where(x => x.Name.Contains(value!) || (x.Content != null && x.Content.Contains(value!)));
            return await query.OrderBy(x => x.Name).AsNoTracking().Take(50).ToListAsync(token);
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "ユーザー入力由来の任意の例外を UI 向けメッセージに変換するため広く捕捉する")]
    public async Task<TagImportResult> ImportCsvTagsAsync(
        string userId,
        string selectedParentTagName,
        string csvContent,
        bool asSystem = false)
    {
        await using ApplicationDbContext dbContext = await _dbContextFactory.CreateDbContextAsync();

        List<List<string>> records = ParseCsv(csvContent);

        var effectiveOwnerId = asSystem ? "system" : userId;

        // Load existing tags for this user / system with case-insensitive and trimmed names
        List<Tag> loadedTags = await dbContext.Tags
            .Where(t => t.OwnerId == effectiveOwnerId)
            .ToListAsync();

        Dictionary<string, Tag> existingTags = new(StringComparer.OrdinalIgnoreCase);
        foreach (var t in loadedTags)
        {
            var trimmedName = t.Name.Trim();
            _ = existingTags.TryAdd(trimmedName, t);
            if (!existingTags.ContainsKey(t.Name))
            {
                existingTags[t.Name] = t;
            }
        }

        var createdCount = 0;
        HashSet<int> updatedTagIds = [];

        // Execute with retry strategy instead of manual transaction
        return await dbContext.Database.ExecuteWithStrategyAsync(async () =>
        {
            var trimmedParentTagName = selectedParentTagName.Trim();
            Tag? baseParentTag = existingTags.TryGetValue(trimmedParentTagName, out Tag? trackedBaseTag)
                ? trackedBaseTag
                : await dbContext.Tags.FirstOrDefaultAsync(t =>
                    (t.OwnerId == userId || t.IsSystem || t.OwnerId == "system") && (t.Name == trimmedParentTagName || t.Name == selectedParentTagName))
                ?? await dbContext.Tags.FirstOrDefaultAsync(t => t.Name == Tag.RootTagName);

            foreach (var fields in records)
            {
                List<string> tagNames;
                var leafContent = string.Empty;

                // 3列以上の場合は末尾2列を Content とし、それより前をタグ階層とする。
                // 2列以下の場合は全列をタグ階層とし、Content は空とする。
                if (fields.Count >= 3)
                {
                    tagNames = fields.Take(fields.Count - 2).Where(t => !string.IsNullOrEmpty(t)).ToList();
                    var contentCol1 = fields[^2].Trim();
                    var contentCol2 = fields[^1].Trim();

                    leafContent = (string.IsNullOrEmpty(contentCol1), string.IsNullOrEmpty(contentCol2)) switch
                    {
                        (false, false) => $"{contentCol1}\n{contentCol2}",
                        (false, true) => contentCol1,
                        (true, false) => contentCol2,
                        _ => string.Empty
                    };
                }
                else
                {
                    tagNames = fields.Where(t => !string.IsNullOrEmpty(t)).ToList();
                }

                if (tagNames.Count == 0)
                {
                    continue;
                }

                Tag? currentParentTag = baseParentTag;

                for (var i = 0; i < tagNames.Count; i++)
                {
                    var tagName = tagNames[i].Trim();
                    var isLeaf = i == tagNames.Count - 1;

                    // Validate tag name
                    if (!TagNameRegex().IsMatch(tagName))
                    {
                        throw new InvalidOperationException($"不正なタグ名が含まれています: '{tagName}'");
                    }

                    // 親タグ自身が CSV の先頭列に指定されている場合はスキップして親として扱う
                    if (i == 0 && baseParentTag != null && baseParentTag.Name.Trim().Equals(tagName, StringComparison.OrdinalIgnoreCase))
                    {
                        if (isLeaf && !string.IsNullOrEmpty(leafContent))
                        {
                            if (baseParentTag.Content != leafContent)
                            {
                                baseParentTag.Content = leafContent;
                                baseParentTag.UpdatedDate = DateTime.UtcNow;
                                if (baseParentTag.Id > 0)
                                {
                                    _ = updatedTagIds.Add(baseParentTag.Id);
                                }
                            }
                        }
                        currentParentTag = baseParentTag;
                        continue;
                    }

                    if (!existingTags.TryGetValue(tagName, out Tag? tag))
                    {
                        // メモリ内辞書にない場合でも、SQL Server の照合順序（末尾空白無視など）で DB に存在しないか再確認
                        tag = await dbContext.Tags.FirstOrDefaultAsync(t =>
                            t.OwnerId == effectiveOwnerId && t.Name == tagName);

                        if (tag != null)
                        {
                            _ = existingTags.TryAdd(tagName, tag);
                            _ = existingTags.TryAdd(tag.Name, tag);
                        }
                    }

                    if (tag == null)
                    {
                        HierarchyId? lastChildNode = currentParentTag == null
                            ? null
                            : await dbContext.Tags
                                .Where(t => t.Node.GetAncestor(1) == currentParentTag.Node)
                                .OrderByDescending(t => t.Node)
                                .Select(t => (HierarchyId?)t.Node)
                                .FirstOrDefaultAsync();

                        // Tag doesn't exist, create it
                        var newTag = new Tag
                        {
                            Name = tagName,
                            Content = isLeaf ? leafContent : string.Empty,
                            OwnerId = effectiveOwnerId,
                            IsSystem = asSystem,
                            ParentTagId = currentParentTag?.Id,
                            Node = currentParentTag == null
                                ? HierarchyId.GetRoot()
                                : GetSafeChildNode(currentParentTag.Node, lastChildNode),
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
                            _logger.LogWarning(ex, "Embedding generation failed: {Message}", ex.Message);
                        }

                        _ = dbContext.Tags.Add(newTag);
                        try
                        {
                            _ = await dbContext.SaveChangesAsync();
                            existingTags[tagName] = newTag;
                            currentParentTag = newTag;
                            createdCount++;
                            continue;
                        }
                        catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.SqlClient.SqlException sqlEx && (sqlEx.Number == 2601 || sqlEx.Number == 2627))
                        {
                            // 万が一同一キーの重複が発生した場合は、DBから既存タグを再取得してリカバリする
                            dbContext.Entry(newTag).State = EntityState.Detached;
                            tag = await dbContext.Tags.FirstOrDefaultAsync(t =>
                                t.OwnerId == effectiveOwnerId && t.Name == tagName);

                            if (tag != null)
                            {
                                existingTags[tagName] = tag;
                                _ = existingTags.TryAdd(tag.Name, tag);
                            }
                            else
                            {
                                throw;
                            }
                        }
                    }

                    var hasUpdated = false;

                    if (currentParentTag != null && tag.ParentTagId != currentParentTag.Id)
                    {
                        // Avoid circular reference
                        if (!IsDescendantOrSelf(tag, currentParentTag!))
                        {
                            HierarchyId? lastChildNode = await dbContext.Tags
                                .Where(t => t.Node.GetAncestor(1) == currentParentTag.Node)
                                .OrderByDescending(t => t.Node)
                                .Select(t => (HierarchyId?)t.Node)
                                .FirstOrDefaultAsync();

                            tag.ParentTagId = currentParentTag.Id;
                            tag.Node = GetSafeChildNode(currentParentTag.Node, lastChildNode);
                            hasUpdated = true;
                        }
                    }

                    // 既存タグが最下層タグかつ Content が指定されている場合は更新
                    if (isLeaf && !string.IsNullOrEmpty(leafContent))
                    {
                        if (tag.Content != leafContent)
                        {
                            tag.Content = leafContent;
                            tag.UpdatedDate = DateTime.UtcNow;
                            hasUpdated = true;
                        }
                    }

                    if (hasUpdated && tag.Id > 0)
                    {
                        _ = updatedTagIds.Add(tag.Id);
                    }

                    currentParentTag = tag;
                }
            }

            _ = await dbContext.SaveChangesAsync();

            return new TagImportResult(createdCount, updatedTagIds.Count);
        });
    }

    private static bool IsDescendantOrSelf(Tag parent, Tag target)
        => ReferenceEquals(parent, target) || parent.Id == target.Id || target.Node.IsDescendantOf(parent.Node);

    /// <summary>
    ///     親ノードの配下に安全に新しい子ノードを生成する。
    ///     lastChildNode が直下の子（lastChildNode.GetAncestor(1) == parentNode）であるか検証し、
    ///     不整合がある場合は parentNode.GetDescendant(null, null) にフォールバックしてエラー 24008 を防止する。
    /// </summary>
    internal static HierarchyId GetSafeChildNode(HierarchyId parentNode, HierarchyId? lastChildNode)
    {
        if (lastChildNode != null)
        {
            try
            {
                if (lastChildNode.GetAncestor(1) == parentNode)
                {
                    return parentNode.GetDescendant(lastChildNode, null);
                }
            }
            catch (Exception ex) when (ex is Microsoft.SqlServer.Types.HierarchyIdException or InvalidOperationException or ArgumentException)
            {
                // 例外発生時は null, null で安全に生成
            }
        }

        return parentNode.GetDescendant(null, null);
    }

    /// <summary>
    ///     ダブルクォートによる囲みやエスケープ、改行を含むフィールドに対応した CSV / TSV パース。
    ///     カンマ（,）およびタブ（\t）の区切り文字に自動対応。
    /// </summary>
    private static List<List<string>> ParseCsv(string csvContent)
    {
        List<List<string>> records = [];
        if (string.IsNullOrWhiteSpace(csvContent))
        {
            return records;
        }

        // 区切り文字の自動判定: タブが存在しカンマ以上含まれる場合はタブ区切り（TSV）と判定
        var commaCount = 0;
        var tabCount = 0;
        for (var idx = 0; idx < Math.Min(csvContent.Length, 2000); idx++)
        {
            var ch = csvContent[idx];
            if (ch == ',')
            {
                commaCount++;
            }
            else if (ch == '\t')
            {
                tabCount++;
            }
        }
        var delimiter = tabCount > commaCount ? '\t' : ',';

        List<string> currentRecord = [];
        var currentField = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < csvContent.Length; i++)
        {
            var c = csvContent[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < csvContent.Length && csvContent[i + 1] == '"')
                    {
                        currentField.Append('"');
                        i++; // エスケープされた引用符をスキップ
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    currentField.Append(c);
                }
            }
            else
            {
                switch (c)
                {
                    case '"':
                        inQuotes = true;
                        break;
                    case '\r':
                        if (i + 1 < csvContent.Length && csvContent[i + 1] == '\n')
                        {
                            i++;
                        }
                        currentRecord.Add(currentField.ToString().Trim());
                        currentField.Clear();
                        if (currentRecord.Any(f => !string.IsNullOrEmpty(f)))
                        {
                            records.Add(currentRecord);
                        }
                        currentRecord = [];
                        break;
                    case '\n':
                        currentRecord.Add(currentField.ToString().Trim());
                        currentField.Clear();
                        if (currentRecord.Any(f => !string.IsNullOrEmpty(f)))
                        {
                            records.Add(currentRecord);
                        }
                        currentRecord = [];
                        break;
                    default:
                        if (c == delimiter)
                        {
                            currentRecord.Add(currentField.ToString().Trim());
                            currentField.Clear();
                        }
                        else
                        {
                            currentField.Append(c);
                        }
                        break;
                }
            }
        }

        currentRecord.Add(currentField.ToString().Trim());
        if (currentRecord.Any(f => !string.IsNullOrEmpty(f)))
        {
            records.Add(currentRecord);
        }

        return records;
    }
}