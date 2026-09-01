using Microsoft.EntityFrameworkCore;

using Moq;

using SRNSMudApp.Data;
using SRNSMudApp.Services;
using SRNSMudApp.Tests.TestSupport;

namespace SRNSMudApp.Tests.Services;

public class TagDialogDataProviderTests : IAsyncLifetime
{
    private MsSqlTestDatabase _sharedDb = null!;

    public async Task InitializeAsync()
    {
        _sharedDb = await SharedMsSqlTestDatabase.GetInstanceAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private (ApplicationDbContext dbContext, TagDialogDataProvider provider, Mock<ITagEmbeddingService> embeddingMock, string tid) CreateScope()
    {
        var tid = Guid.NewGuid().ToString("N")[..8];
        var dbContext = new ApplicationDbContext(_sharedDb.Options);
        var mockDbFactory = new Mock<IDbContextFactory<ApplicationDbContext>>();
        mockDbFactory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new ApplicationDbContext(_sharedDb.Options));

        var embeddingMock = new Mock<ITagEmbeddingService>();
        var provider = new TagDialogDataProvider(mockDbFactory.Object, embeddingMock.Object);
        return (dbContext, provider, embeddingMock, tid);
    }

    [Fact]
    public async Task SearchTagsWithFallbackAsync_WhenTokenCancelled_ReturnsEmptyListWithoutThrowing()
    {
        var (_, provider, _, _) = CreateScope();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await provider.SearchTagsWithFallbackAsync("any", cts.Token);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task SearchTagsWithFallbackAsync_WhenValueIsEmpty_ReturnsTagsOrderedByName()
    {
        var (dbContext, provider, _, tid) = CreateScope();
        await using (dbContext)
        {
            var userId = $"u_{tid}";
            await dbContext.SeedUsersAsync(userId);

            var tagB = new Tag { Name = $"B_Tag_{tid}", OwnerId = userId };
            var tagA = new Tag { Name = $"A_Tag_{tid}", OwnerId = userId };
            dbContext.Tags.AddRange(tagB, tagA);
            await dbContext.SaveChangesAsync();

            var result = await provider.SearchTagsWithFallbackAsync(null);

            Assert.Contains(result, t => t.Name == tagA.Name);
            Assert.Contains(result, t => t.Name == tagB.Name);

            var idxA = result.FindIndex(t => t.Name == tagA.Name);
            var idxB = result.FindIndex(t => t.Name == tagB.Name);
            Assert.True(idxA < idxB, "Results should be ordered by Name");
        }
    }

    [Fact]
    public async Task SearchTagsWithFallbackAsync_WhenVectorSearchFails_FallsBackToTextSearch()
    {
        var (dbContext, provider, embeddingMock, tid) = CreateScope();
        await using (dbContext)
        {
            var userId = $"u_{tid}";
            await dbContext.SeedUsersAsync(userId);

            var keyword = $"kw_{tid}";
            var tag1 = new Tag { Name = $"Alpha_{keyword}", Content = "Some content", OwnerId = userId };
            var tag2 = new Tag { Name = "Other", Content = $"Has_{keyword}_here", OwnerId = userId };
            var tagUnrelated = new Tag { Name = "Unrelated", OwnerId = userId };
            dbContext.Tags.AddRange(tag1, tag2, tagUnrelated);
            await dbContext.SaveChangesAsync();

            embeddingMock.Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>()))
                .ThrowsAsync(new InvalidOperationException("API error"));

            var result = await provider.SearchTagsWithFallbackAsync(keyword);

            Assert.Equal(2, result.Count);
            Assert.Contains(result, t => t.Id == tag1.Id);
            Assert.Contains(result, t => t.Id == tag2.Id);
            Assert.DoesNotContain(result, t => t.Id == tagUnrelated.Id);
        }
    }
}