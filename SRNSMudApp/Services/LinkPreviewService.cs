#region

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

using HtmlAgilityPack;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using SRNSMudApp.Data;
using SRNSMudApp.Models;

#endregion

namespace SRNSMudApp.Services;

public partial class LinkPreviewService
{
    private readonly ConcurrentDictionary<string, LinkPreviewData> _cache = new();
    private readonly HttpClient _httpClient;
    private readonly IServiceScopeFactory _scopeFactory;

    public LinkPreviewService(HttpClient httpClient, IServiceScopeFactory scopeFactory)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "SRNSMudApp-LinkPreviewBot/1.0");
    }

    [SuppressMessage("Design", "CA1054")]
    public async Task<LinkPreviewData> GetPreviewAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return new LinkPreviewData { IsSuccess = false };
        }

        if (Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out Uri? uri))
        {
            var path = uri.IsAbsoluteUri ? uri.AbsolutePath : uri.ToString().Split('?')[0];

            if (path.StartsWith("/ItemDetail/", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(path.AsSpan("/ItemDetail/".Length), out int itemId))
                {
                    return await GetItemPreviewAsync(itemId, url);
                }
            }
            else if (path.StartsWith("/TagDetail/", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(path.AsSpan("/TagDetail/".Length), out int tagId))
                {
                    return await GetTagPreviewAsync(tagId, url);
                }
            }
        }

        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://" + url;
        }

        if (_cache.TryGetValue(url, out LinkPreviewData? cachedData))
        {
            return cachedData;
        }

        var preview = new LinkPreviewData { Url = url };

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using HttpResponseMessage response =
                await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

            if (!response.IsSuccessStatusCode)
            {
                return preview; // Return default/failed
            }

            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (contentType?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == false)
            {
                return preview; // Not an HTML page
            }

            // Using stream to avoid loading massive files if possible, though HtmlDocument loads the whole stream
            await using Stream stream = await response.Content.ReadAsStreamAsync();
            var doc = new HtmlDocument();
            doc.Load(stream);

            preview.Title = GetMetaTagContent(doc, "og:title") ??
                            doc.DocumentNode.SelectSingleNode("//title")?.InnerText?.Trim() ?? string.Empty;

            var ogDesc = GetMetaTagContent(doc, "og:description") ??
                         GetMetaTagContent(doc, "description") ?? string.Empty;

            var bodyText = "";
            HtmlNode? bodyNode = doc.DocumentNode.SelectSingleNode("//body");
            if (bodyNode != null)
            {
                HtmlNodeCollection? nodesToRemove = bodyNode.SelectNodes(".//script | .//style | .//noscript");
                if (nodesToRemove != null)
                {
                    foreach (HtmlNode node in nodesToRemove)
                    {
                        node.Remove();
                    }
                }

                var text = HtmlEntity.DeEntitize(bodyNode.InnerText);
                bodyText = WhitespaceRegex().Replace(text, " ").Trim();
            }

            preview.Description = bodyText.Length > 100
                ? bodyText.Length > 400 ? $"{bodyText.AsSpan(0, 400)}..." : bodyText
                : ogDesc.Length > 400
                    ? $"{ogDesc.AsSpan(0, 400)}..."
                    : ogDesc;

            preview.ImageUrl = GetMetaTagContent(doc, "og:image") ?? string.Empty;
            preview.SiteName = GetMetaTagContent(doc, "og:site_name") ?? string.Empty;
            preview.IsSuccess = !string.IsNullOrEmpty(preview.Title);

            // Handle relative image URLs if needed
            if (!string.IsNullOrEmpty(preview.ImageUrl) &&
                !preview.ImageUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                if (Uri.TryCreate(url, UriKind.Absolute, out Uri? baseUri) &&
                    Uri.TryCreate(baseUri, preview.ImageUrl, out Uri? absoluteImageUri))
                {
                    preview.ImageUrl = absoluteImageUri.ToString();
                }
            }

            _cache[url] = preview;
        }
#pragma warning disable CA1031
        catch (Exception ex)
        {
            // Log error if logger is added
            Console.WriteLine($"Error fetching link preview for {url}: {ex.Message}");
        }
#pragma warning restore CA1031

        return preview;
    }

    private static string? GetMetaTagContent(HtmlDocument doc, string property)
    {
        HtmlNode? node = doc.DocumentNode.SelectSingleNode($"//meta[@property='{property}']") ??
                         doc.DocumentNode.SelectSingleNode($"//meta[@name='{property}']");

        return node?.GetAttributeValue("content", string.Empty)?.Trim();
    }

    private async Task<LinkPreviewData> GetItemPreviewAsync(int itemId, string originalUrl)
    {
        if (_cache.TryGetValue(originalUrl, out var cachedData))
        {
            return cachedData;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var item = await db.Items
            .Include(i => i.TagRelations)
                .ThenInclude(tr => tr.Tag)
                    .ThenInclude(t => t.Owner)
            .FirstOrDefaultAsync(i => i.Id == itemId);

        if (item == null)
        {
            return new LinkPreviewData { Url = originalUrl, IsSuccess = false };
        }

        var tags = item.TagRelations
            .Where(tr => tr.Tag != null && !tr.Tag.IsSystem)
            .OrderByDescending(tr => tr.Weight)
            .Take(3)
            .Select(tr =>
            {
                var ownerName = tr.Tag.Owner?.UserName ?? (tr.Tag.GetKind() is Models.Unions.SystemClassificationTag ? "system" : "unknown");
                return $"{tr.Tag.Name} ({ownerName})";
            })
            .ToList();

        var text = WhitespaceRegex().Replace(item.Content, " ").Trim();
        if (text.Length > 200) text = $"{text.AsSpan(0, 200)}...";

        var preview = new LinkPreviewData
        {
            Url = originalUrl,
            Title = $"Item #{item.Id}",
            Description = text + (tags.Count > 0 ? $" | Tags: {string.Join(", ", tags)}" : ""),
            SiteName = "SRNSMudApp",
            IsSuccess = true
        };

        _cache[originalUrl] = preview;
        return preview;
    }

    private async Task<LinkPreviewData> GetTagPreviewAsync(int tagId, string originalUrl)
    {
        if (_cache.TryGetValue(originalUrl, out var cachedData))
        {
            return cachedData;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var tag = await db.Tags
            .Include(t => t.Owner)
            .FirstOrDefaultAsync(t => t.Id == tagId);

        if (tag == null)
        {
            return new LinkPreviewData { Url = originalUrl, IsSuccess = false };
        }

        var text = string.IsNullOrWhiteSpace(tag.Content) ? "タグ詳細" : WhitespaceRegex().Replace(tag.Content, " ").Trim();
        if (text.Length > 200) text = $"{text.AsSpan(0, 200)}...";
        var ownerName = tag.Owner?.UserName ?? (tag.GetKind() is Models.Unions.SystemClassificationTag ? "system" : "unknown");

        var preview = new LinkPreviewData
        {
            Url = originalUrl,
            Title = $"Tag: {tag.Name} ({ownerName})",
            Description = text,
            SiteName = "SRNSMudApp",
            IsSuccess = true
        };

        _cache[originalUrl] = preview;
        return preview;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}