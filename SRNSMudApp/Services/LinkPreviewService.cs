using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.Logging;

using SRNSMudApp.Models;

namespace SRNSMudApp.Services;

/// <summary>
///     登録された複数の <see cref="ILinkPreviewProvider"/> を順次評価し、
///     インメモリキャッシュを併用してプレビューデータを提供する複合コーディネーターサービス（Composite / Strategy Coordinator）。
/// </summary>
public sealed partial class LinkPreviewService : ILinkPreviewService
{
    private readonly ConcurrentDictionary<string, LinkPreviewData> _cache = new();
    private readonly IReadOnlyList<ILinkPreviewProvider> _providers;
    private readonly ILogger<LinkPreviewService> _logger;

    /// <summary>
    ///     <see cref="LinkPreviewService"/> クラスの新しいインスタンスを初期化する。
    /// </summary>
    /// <param name="providers">利用可能なプレビュープロバイダーのコレクション。</param>
    /// <param name="logger">ロガー。</param>
    public LinkPreviewService(
        IEnumerable<ILinkPreviewProvider> providers,
        ILogger<LinkPreviewService> logger)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(logger);

        _providers = [.. providers.OrderBy(p => p.Order)];
        _logger = logger;
    }

    /// <inheritdoc />
    [SuppressMessage("Design", "CA1054:URI parameters should not be strings", Justification = "Handles relative internal and external URLs")]
    public Task<LinkPreviewData> GetPreviewAsync(string url) => GetPreviewAsync(url, CancellationToken.None);

    /// <inheritdoc />
    [SuppressMessage("Design", "CA1054:URI parameters should not be strings", Justification = "Handles relative internal and external URLs")]
    public async Task<LinkPreviewData> GetPreviewAsync(string url, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return new LinkPreviewData { IsSuccess = false };
        }

        var normalizedUrl = NormalizeUrl(url);

        if (_cache.TryGetValue(normalizedUrl, out LinkPreviewData? cachedData))
        {
            return cachedData;
        }

        if (!Uri.TryCreate(normalizedUrl, UriKind.RelativeOrAbsolute, out Uri? uri))
        {
            return new LinkPreviewData { Url = url, IsSuccess = false };
        }

        foreach (var provider in _providers)
        {
            if (!provider.CanHandle(uri))
            {
                continue;
            }

            try
            {
                var result = await provider.GetPreviewAsync(uri, url, cancellationToken);
                if (result.IsSuccess)
                {
                    _cache[normalizedUrl] = result;
                    return result;
                }
            }
#pragma warning disable CA1031
            catch (Exception ex)
            {
                LogProviderFailed(_logger, provider.GetType().Name, url, ex);
            }
#pragma warning restore CA1031
        }

        return new LinkPreviewData { Url = url, IsSuccess = false };
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to resolve preview using {Provider} for {Url}")]
    private static partial void LogProviderFailed(ILogger logger, string provider, string url, Exception ex);

    private static string NormalizeUrl(string url)
    {
        if (!url.StartsWith('/', StringComparison.Ordinal) &&
            !url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return "https://" + url;
        }

        return url;
    }
}