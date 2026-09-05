#region

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

using HtmlAgilityPack;

using SRNSMudApp.Models;

#endregion

namespace SRNSMudApp.Services;

public partial class LinkPreviewService
{
    private readonly ConcurrentDictionary<string, LinkPreviewData> _cache = new();
    private readonly HttpClient _httpClient;

    public LinkPreviewService(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "SRNSMudApp-LinkPreviewBot/1.0");
    }

    [SuppressMessage("Design", "CA1054")]
    public async Task<LinkPreviewData> GetPreviewAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return new LinkPreviewData { IsSuccess = false };
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

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}