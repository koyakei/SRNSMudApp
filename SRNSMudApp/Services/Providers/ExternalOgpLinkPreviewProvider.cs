using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

using HtmlAgilityPack;

using Microsoft.Extensions.Logging;

using SRNSMudApp.Models;

namespace SRNSMudApp.Services.Providers;

/// <summary>
///     外部 Web サイト (HTTP / HTTPS) の URL に対応し、
///     <see cref="HttpClient"/> による HTML 取得と OGP メタタグ解析によってプレビューを生成するプロバイダー。
/// </summary>
public partial class ExternalOgpLinkPreviewProvider : ILinkPreviewProvider
{
    public const string HttpClientName = "ExternalOgpLinkPreview";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ExternalOgpLinkPreviewProvider> _logger;

    /// <summary>
    ///     DI 推奨コンストラクター。<see cref="IHttpClientFactory"/> を受け取ることで
    ///     シングルトンによる Transient/Scoped な HttpClient のキャプティブ化 (CAPTIVE-01) と DNS Staleness を防止する。
    /// </summary>
    public ExternalOgpLinkPreviewProvider(
        IHttpClientFactory httpClientFactory,
        ILogger<ExternalOgpLinkPreviewProvider> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    ///     単体テスト用のコンストラクター。
    ///     DI の CallSiteFactory によるコンストラクター多重定義の曖昧性例外を防止するため internal とする。
    /// </summary>
    internal ExternalOgpLinkPreviewProvider(
        HttpClient httpClient,
        ILogger<ExternalOgpLinkPreviewProvider> logger)
        : this(new SingleHttpClientFactory(httpClient ?? throw new ArgumentNullException(nameof(httpClient))), logger)
    {
    }

    private sealed class SingleHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    /// <inheritdoc />
    public int Order => 100;

    /// <inheritdoc />
    public bool CanHandle(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return uri.IsAbsoluteUri &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    /// <inheritdoc />
    [SuppressMessage("Design", "CA1054:URI parameters should not be strings", Justification = "Original URL representation")]
    public async Task<LinkPreviewData> GetPreviewAsync(Uri uri, string originalUrl, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(originalUrl);

        var preview = new LinkPreviewData { Url = originalUrl };

        try
        {
            HttpClient client = _httpClientFactory.CreateClient(HttpClientName);

            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            if (!request.Headers.Contains("User-Agent"))
            {
                request.Headers.TryAddWithoutValidation("User-Agent", "SRNSMudApp-LinkPreviewBot/1.0");
            }

            using HttpResponseMessage response =
                await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return preview;
            }

            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (contentType?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == false)
            {
                return preview;
            }

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
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

            // 相対画像パスの解決
            if (!string.IsNullOrEmpty(preview.ImageUrl) &&
                !preview.ImageUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase) &&
                Uri.TryCreate(uri, preview.ImageUrl, out Uri? absoluteImageUri))
            {
                preview.ImageUrl = absoluteImageUri.ToString();
            }
        }
#pragma warning disable CA1031
        catch (Exception ex)
        {
            LogFetchFailed(_logger, originalUrl, ex.Message, ex);
        }
#pragma warning restore CA1031

        return preview;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to fetch link preview for {Url}: {Message}")]
    private static partial void LogFetchFailed(ILogger logger, string url, string message, Exception ex);

    private static string? GetMetaTagContent(HtmlDocument doc, string property)
    {
        HtmlNode? node = doc.DocumentNode.SelectSingleNode($"//meta[@property='{property}']") ??
                         doc.DocumentNode.SelectSingleNode($"//meta[@name='{property}']");

        return node?.GetAttributeValue("content", string.Empty)?.Trim();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}