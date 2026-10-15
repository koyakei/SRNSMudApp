#region

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using Sidio.Sitemap.Core;
using Sidio.Sitemap.Core.Services;

using SRNSMudApp.Services.Sitemap;

#endregion

namespace SRNSMudApp.Endpoints;

/// <summary>
/// サイトマップ関連のエンドポイントを登録する拡張クラス
/// Sitemap Indexおよび分割された子サイトマップをマッピングします
/// </summary>
public static class SitemapEndpoints
{
    private const string XmlContentType = "application/xml; charset=utf-8";

    /// <summary>
    /// サイトマップエンドポイントをルートビルダーに登録します
    /// </summary>
    /// <param name="app">エンドポイントルートビルダー</param>
    /// <returns>チェーン用のエンドポイントルートビルダー</returns>
    public static IEndpointRouteBuilder MapSitemapEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup(string.Empty);

        // 1. 親サイトマップ (Sitemap Index)
        _ = group.MapGet("/sitemap.xml", async (
            HttpRequest request,
            ISitemapIndexService sitemapIndexService,
            CancellationToken cancellationToken) =>
        {
            Uri baseUri = new($"{request.Scheme}://{request.Host}");

            // 分割された各子サイトマップのインデックスノードを作成
            SitemapIndex sitemapIndex = new([
                new SitemapIndexNode(new Uri(baseUri, "/sitemap-pages.xml").ToString())
                {
                    LastModified = DateTime.UtcNow,
                },
                new SitemapIndexNode(new Uri(baseUri, "/sitemap-articles.xml").ToString())
                {
                    LastModified = DateTime.UtcNow,
                },
            ]);

            var xml = await sitemapIndexService.SerializeAsync(sitemapIndex, cancellationToken);
            return Results.Content(xml, XmlContentType);
        })
        .WithName("GetSitemapIndex")
        .ExcludeFromDescription();

        // 2. 固定ページ用サイトマップ
        _ = group.MapGet("/sitemap-pages.xml", async (
            HttpRequest request,
            IStaticPageSitemapProvider staticPageProvider,
            ISitemapService sitemapService,
            CancellationToken cancellationToken) =>
        {
            Uri baseUri = new($"{request.Scheme}://{request.Host}");
            var nodes = staticPageProvider.GetNodes(baseUri);
            Sitemap sitemap = new(nodes);

            var xml = await sitemapService.SerializeAsync(sitemap, cancellationToken);
            return Results.Content(xml, XmlContentType);
        })
        .WithName("GetStaticPagesSitemap")
        .ExcludeFromDescription();

        // 3. 記事用サイトマップ (DBから動的生成)
        _ = group.MapGet("/sitemap-articles.xml", async (
            HttpRequest request,
            IArticleSitemapProvider articleProvider,
            ISitemapService sitemapService,
            CancellationToken cancellationToken) =>
        {
            Uri baseUri = new($"{request.Scheme}://{request.Host}");
            var nodes = await articleProvider.GetNodesAsync(baseUri, cancellationToken);
            Sitemap sitemap = new(nodes);

            var xml = await sitemapService.SerializeAsync(sitemap, cancellationToken);
            return Results.Content(xml, XmlContentType);
        })
        .WithName("GetArticlesSitemap")
        .ExcludeFromDescription();

        return app;
    }
}