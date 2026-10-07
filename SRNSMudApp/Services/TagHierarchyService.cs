#pragma warning disable CA1848

using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Json;
using System.Numerics.Tensors;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

using SRNSMudApp.Data;

namespace SRNSMudApp.Services;

/// <summary>
///     新規タグの親タグ階層をベクトル検索とLLM（Chrome Built-in AI / Prompt API または Gemini）を用いて判定・サジェストする実装クラス。
/// </summary>
public class TagHierarchyService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    ITagEmbeddingService tagEmbeddingService,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<TagHierarchyService>? logger = null) : ITagHierarchyService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
    private readonly ITagEmbeddingService _tagEmbeddingService =
        tagEmbeddingService ?? throw new ArgumentNullException(nameof(tagEmbeddingService));
    private readonly IHttpClientFactory _httpClientFactory =
        httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    private readonly IConfiguration _configuration =
        configuration ?? throw new ArgumentNullException(nameof(configuration));
    private readonly ILogger<TagHierarchyService> _logger =
        logger ?? NullLogger<TagHierarchyService>.Instance;

    /// <inheritdoc />
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "ローカルLLMおよびGemini APIの失敗時はベクトル類似度最上位タグへ安全にフォールバックするため")]
    public async Task<Tag?> SuggestParentTagAsync(
        string newTagName,
        IJSRuntime? jsRuntime,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(newTagName))
        {
            return null;
        }

        await using ApplicationDbContext db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        // 既存タグ一覧を取得（VoteTag/ReactionTag/RootTagは除外）
        List<Tag> allTags = await db.Tags
            .Where(t => !Tag.VoteTagNames.Contains(t.Name) &&
                        !Tag.ReactionTagNames.Contains(t.Name) &&
                        t.Name != Tag.RootTagName)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        if (allTags.Count == 0)
        {
            return null;
        }

        // 2階層目（Level >= 2）より下の階層を優先。存在しない場合は Level >= 1 を対象とする
        List<Tag> targetPool = allTags.Where(t => t.Node != null && t.Node.GetLevel() >= 2).ToList();
        if (targetPool.Count == 0)
        {
            targetPool = allTags.Where(t => t.Node != null && t.Node.GetLevel() >= 1).ToList();
        }
        if (targetPool.Count == 0)
        {
            targetPool = allTags;
        }

        // ベクトル類似度による候補タグの選定
        ReadOnlyMemory<float> newTagEmbedding = await _tagEmbeddingService.GenerateEmbeddingAsync(newTagName);
        float[] queryVector = newTagEmbedding.ToArray();

        List<Tag> candidateTags = targetPool
            .Where(t => t.Embedding != null && t.Embedding.Length == queryVector.Length)
            .OrderByDescending(t => TensorPrimitives.CosineSimilarity(t.Embedding, queryVector))
            .Take(5)
            .ToList();

        if (candidateTags.Count == 0)
        {
            return targetPool.FirstOrDefault();
        }

        if (candidateTags.Count == 1)
        {
            return candidateTags[0];
        }

        List<string> candidateNames = candidateTags.Select(t => t.Name).ToList();

        // 1. Chrome Built-in AI (Prompt API) による判定を試みる
        if (jsRuntime != null)
        {
            try
            {
                // LEAK-01: 動的インポートした JS モジュールを確実に破棄し、Circuit およびブラウザのハンドルリークを防止する
                await using IJSObjectReference module = await jsRuntime.InvokeAsync<IJSObjectReference>(
                    "import", cancellationToken, "./js/tagHierarchy.js");

                // 新規タグも含めて判定させる
                List<string> promptCandidates = [newTagName, .. candidateNames];
                JsonElement result = await module.InvokeAsync<JsonElement>(
                    "determineHierarchyLocal", cancellationToken, promptCandidates);

                Tag? matched = FindMatchedTagFromLlmResult(result, candidateTags, newTagName);
                if (matched != null)
                {
                    return matched;
                }
            }
            catch (Exception ex)
            {
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation(ex, "Chrome Built-in AI is unavailable, falling back: {Message}", ex.Message);
                }
            }
        }

        // 2. Gemini API による判定フォールバック
        string? apiKey = _configuration["Gemini:ApiKey"] ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            try
            {
                Tag? geminiMatched = await DetermineHierarchyViaGeminiAsync(newTagName, candidateTags, apiKey, cancellationToken);
                if (geminiMatched != null)
                {
                    return geminiMatched;
                }
            }
            catch (Exception ex)
            {
                if (_logger.IsEnabled(LogLevel.Warning))
                {
                    _logger.LogWarning(ex, "Gemini API fallback failed: {Message}", ex.Message);
                }
            }
        }

        // 3. 最終フォールバック: ベクトル類似度が最も高いタグを採用
        return candidateTags[0];
    }

    /// <inheritdoc />
    public async Task<HierarchyId> DetermineNewNodeAsync(int parentTagId, CancellationToken cancellationToken = default)
    {
        await using ApplicationDbContext db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        Tag? parentTag = await db.Tags.FindAsync([parentTagId], cancellationToken);
        if (parentTag is null)
        {
            return HierarchyId.GetRoot();
        }

        HierarchyId parentNode = parentTag.Node ?? HierarchyId.GetRoot();

        HierarchyId? lastChild = await db.Tags
            .Where(t => t.Node.GetAncestor(1) == parentNode)
            .OrderByDescending(t => t.Node)
            .Select(t => (HierarchyId?)t.Node)
            .FirstOrDefaultAsync(cancellationToken);

        return parentNode.GetDescendant(lastChild, null);
    }

    private static Tag? FindMatchedTagFromLlmResult(JsonElement result, List<Tag> candidates, string newTagName)
    {
        // 形式: { "root": "...", "parent_of": { "子タグ": "親タグ" } }
        if (result.TryGetProperty("parent_of", out JsonElement parentOfElement) &&
            parentOfElement.ValueKind == JsonValueKind.Object)
        {
            if (parentOfElement.TryGetProperty(newTagName, out JsonElement parentElement))
            {
                string? parentName = parentElement.GetString();
                Tag? matched = candidates.FirstOrDefault(c => string.Equals(c.Name, parentName, StringComparison.OrdinalIgnoreCase));
                if (matched != null)
                {
                    return matched;
                }
            }
        }

        if (result.TryGetProperty("root", out JsonElement rootElement))
        {
            string? rootName = rootElement.GetString();
            Tag? matched = candidates.FirstOrDefault(c => string.Equals(c.Name, rootName, StringComparison.OrdinalIgnoreCase));
            if (matched != null)
            {
                return matched;
            }
        }

        return null;
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "HttpClient from IHttpClientFactory does not need disposal, but using is safe")]
    private async Task<Tag?> DetermineHierarchyViaGeminiAsync(
        string newTagName,
        List<Tag> candidateTags,
        string apiKey,
        CancellationToken cancellationToken)
    {
        using HttpClient client = _httpClientFactory.CreateClient();
        string url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent?key={apiKey}";

        List<string> candidateNames = candidateTags.Select(t => t.Name).ToList();
        var requestBody = new
        {
            contents = new[]
            {
                new
                {
                    parts = new[]
                    {
                        new
                        {
                            text = $"あなたは概念の包含関係を分析する分類器です。新規タグ「{newTagName}」の親概念（上位概念）として最も適切なものを、以下のタグ候補の中から1つ選び、JSON形式 {{ \"parent\": \"親タグ名\" }} で出力してください。タグ候補: {JsonSerializer.Serialize(candidateNames)}"
                        }
                    }
                }
            }
        };

        HttpResponseMessage response = await client.PostAsJsonAsync(url, requestBody, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        using JsonDocument doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);

        if (doc.RootElement.TryGetProperty("candidates", out JsonElement candidatesElement) &&
            candidatesElement.GetArrayLength() > 0)
        {
            JsonElement firstCandidate = candidatesElement[0];
            if (firstCandidate.TryGetProperty("content", out JsonElement contentElement) &&
                contentElement.TryGetProperty("parts", out JsonElement partsElement) &&
                partsElement.GetArrayLength() > 0)
            {
                string? text = partsElement[0].GetProperty("text").GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    string cleaned = text.Trim();
                    if (cleaned.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
                    {
                        cleaned = cleaned[7..];
                    }
                    else if (cleaned.StartsWith("```", StringComparison.OrdinalIgnoreCase))
                    {
                        cleaned = cleaned[3..];
                    }
                    if (cleaned.EndsWith("```", StringComparison.OrdinalIgnoreCase))
                    {
                        cleaned = cleaned[..^3];
                    }
                    cleaned = cleaned.Trim();

                    using JsonDocument parsedJson = JsonDocument.Parse(cleaned);
                    if (parsedJson.RootElement.TryGetProperty("parent", out JsonElement parentElem))
                    {
                        string? parentName = parentElem.GetString();
                        return candidateTags.FirstOrDefault(t => string.Equals(t.Name, parentName, StringComparison.OrdinalIgnoreCase));
                    }
                }
            }
        }

        return null;
    }
}