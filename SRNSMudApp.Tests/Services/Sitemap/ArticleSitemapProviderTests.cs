#region

using Microsoft.EntityFrameworkCore;

using Sidio.Sitemap.Core;

using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Services.Sitemap;
using SRNSMudApp.Tests.TestSupport;

#endregion

namespace SRNSMudApp.Tests.Services.Sitemap;

public class ArticleSitemapProviderTests
{
    private static async Task<AppDbContext> CreateTestDbContextAsync()
    {
        var testDb = await SharedMsSqlTestDatabase.GetInstanceAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(testDb.ConnectionString)
            .Options;

        var context = new AppDbContext(options);
        _ = await context.Database.ExecuteSqlRawAsync("""
            IF OBJECT_ID(N'Articles', N'U') IS NULL
            BEGIN
                CREATE TABLE [Articles] (
                    [Id] int NOT NULL IDENTITY,
                    [Title] nvarchar(max) NOT NULL,
                    [Slug] nvarchar(max) NOT NULL,
                    [UpdatedAt] datetime2 NULL,
                    [CreatedAt] datetime2 NOT NULL,
                    [IsPublished] bit NOT NULL,
                    CONSTRAINT [PK_Articles] PRIMARY KEY ([Id])
                );
            END
            """);
        return context;
    }

    [Fact]
    public async Task GetNodesAsync_ReturnsOnlyPublishedArticles()
    {
        // Arrange
        await using var context = await CreateTestDbContextAsync();

        var article1 = new Article
        {
            Title = "公開記事1",
            Slug = "published-1",
            IsPublished = true,
            CreatedAt = DateTime.UtcNow.AddDays(-2),
            UpdatedAt = DateTime.UtcNow.AddDays(-1),
        };
        var article2 = new Article
        {
            Title = "非公開記事",
            Slug = "draft-1",
            IsPublished = false,
            CreatedAt = DateTime.UtcNow.AddDays(-3),
        };
        var article3 = new Article
        {
            Title = "公開記事2",
            Slug = "published-2",
            IsPublished = true,
            CreatedAt = DateTime.UtcNow,
        };

        context.Articles.AddRange(article1, article2, article3);
        _ = await context.SaveChangesAsync();

        var provider = new ArticleSitemapProvider(context);

        // Act
        var nodes = await provider.GetNodesAsync("https://example.com");

        // Assert
        Assert.NotNull(nodes);
        Assert.Contains(nodes, n => n.Url == $"https://example.com/article/{article1.Id}" && n.Priority == 0.7m && n.ChangeFrequency == ChangeFrequency.Weekly);
        Assert.Contains(nodes, n => n.Url == $"https://example.com/article/{article3.Id}");
        Assert.DoesNotContain(nodes, n => n.Url == $"https://example.com/article/{article2.Id}");
    }

    [Fact]
    public async Task GetNodesAsync_WithUri_ReturnsExpectedNodes()
    {
        // Arrange
        await using var context = await CreateTestDbContextAsync();

        var article = new Article
        {
            Title = "URIテスト記事",
            IsPublished = true,
            CreatedAt = DateTime.UtcNow,
        };
        _ = context.Articles.Add(article);
        _ = await context.SaveChangesAsync();

        var provider = new ArticleSitemapProvider(context);

        // Act
        var nodes = await provider.GetNodesAsync(new Uri("https://example.com"));

        // Assert
        Assert.Contains(nodes, n => n.Url == $"https://example.com/article/{article.Id}");
    }

    [Fact]
    public async Task GetNodesAsync_WithInvalidBaseUrl_ThrowsArgumentException()
    {
        // Arrange
        await using var context = await CreateTestDbContextAsync();
        var provider = new ArticleSitemapProvider(context);

        // Act & Assert
        _ = await Assert.ThrowsAsync<ArgumentException>(() => provider.GetNodesAsync(""));
    }

    [Fact]
    public async Task GetNodesAsync_WithNullUri_ThrowsArgumentNullException()
    {
        // Arrange
        await using var context = await CreateTestDbContextAsync();
        var provider = new ArticleSitemapProvider(context);

        // Act & Assert
        _ = await Assert.ThrowsAsync<ArgumentNullException>(() => provider.GetNodesAsync((Uri)null!));
    }
}