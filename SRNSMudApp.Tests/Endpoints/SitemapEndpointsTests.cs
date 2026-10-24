#region

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using Sidio.Sitemap.Core;

using SRNSMudApp.Services.Sitemap;
using SRNSMudApp.Tests.TestSupport;

#endregion

namespace SRNSMudApp.Tests.Endpoints;

public class SitemapEndpointsTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    [Fact]
    public async Task GetSitemapIndex_ReturnsValidXmlWithChildSitemaps()
    {
        // Arrange
        var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/sitemap.xml");

        // Assert
        _ = response.EnsureSuccessStatusCode();
        Assert.Equal("application/xml", response.Content.Headers.ContentType?.MediaType);

        var xml = await response.Content.ReadAsStringAsync();
        Assert.Contains("<sitemapindex", xml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sitemap-pages.xml", xml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sitemap-articles.xml", xml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetStaticPagesSitemap_ReturnsValidXmlWithStaticUrls()
    {
        // Arrange
        var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/sitemap-pages.xml");

        // Assert
        _ = response.EnsureSuccessStatusCode();
        Assert.Equal("application/xml", response.Content.Headers.ContentType?.MediaType);

        var xml = await response.Content.ReadAsStringAsync();
        Assert.Contains("<urlset", xml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<loc>", xml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/about</loc>", xml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/contact</loc>", xml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetArticlesSitemap_ReturnsValidXmlWithDynamicArticleUrls()
    {
        // Arrange
        var mockProvider = new Mock<IArticleSitemapProvider>();
        _ = mockProvider
            .Setup(p => p.GetNodesAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new SitemapNode("https://example.com/article/101")
                {
                    Priority = 0.7m,
                    ChangeFrequency = ChangeFrequency.Weekly,
                },
                new SitemapNode("https://example.com/article/202")
                {
                    Priority = 0.7m,
                    ChangeFrequency = ChangeFrequency.Weekly,
                },
            ]);

        var customFactory = factory.WithWebHostBuilder(builder =>
        {
            _ = builder.ConfigureServices(services =>
            {
                _ = services.AddScoped(_ => mockProvider.Object);
            });
        });

        var client = customFactory.CreateClient();

        // Act
        var response = await client.GetAsync("/sitemap-articles.xml");

        // Assert
        _ = response.EnsureSuccessStatusCode();
        Assert.Equal("application/xml", response.Content.Headers.ContentType?.MediaType);

        var xml = await response.Content.ReadAsStringAsync();
        Assert.Contains("<urlset", xml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/article/101</loc>", xml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/article/202</loc>", xml, StringComparison.OrdinalIgnoreCase);
    }
}