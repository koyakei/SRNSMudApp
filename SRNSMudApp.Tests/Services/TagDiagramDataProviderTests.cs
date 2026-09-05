using Microsoft.EntityFrameworkCore;

using Moq;

using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;
using SRNSMudApp.Tests.TestSupport;

namespace SRNSMudApp.Tests.Services;

public class TagDiagramDataProviderTests : IAsyncLifetime
{
    private MsSqlTestDatabase _sharedDb = null!;

    public async Task InitializeAsync()
    {
        _sharedDb = await SharedMsSqlTestDatabase.GetInstanceAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private (ApplicationDbContext dbContext, TagDiagramDataProvider provider, Mock<ITagEdgeService> edgeServiceMock, string tid) CreateScope()
    {
        var tid = Guid.NewGuid().ToString("N")[..8];
        var dbContext = new ApplicationDbContext(_sharedDb.Options);
        var mockDbFactory = new Mock<IDbContextFactory<ApplicationDbContext>>();
        mockDbFactory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new ApplicationDbContext(_sharedDb.Options));

        var edgeServiceMock = new Mock<ITagEdgeService>();
        var provider = new TagDiagramDataProvider(mockDbFactory.Object, edgeServiceMock.Object);
        return (dbContext, provider, edgeServiceMock, tid);
    }

    [Fact]
    public void Constructor_NullArguments_ThrowsArgumentNullException()
    {
        var mockDbFactory = new Mock<IDbContextFactory<ApplicationDbContext>>();
        var edgeServiceMock = new Mock<ITagEdgeService>();

        Assert.Throws<ArgumentNullException>(() => new TagDiagramDataProvider(null!, edgeServiceMock.Object));
        Assert.Throws<ArgumentNullException>(() => new TagDiagramDataProvider(mockDbFactory.Object, null!));
    }

    [Fact]
    public async Task LoadAllTagsAsync_ShouldReturnTags_OrderedByName_ExcludingVoteAndReactionTags()
    {
        var (dbContext, provider, _, tid) = CreateScope();
        await using (dbContext)
        {
            var userId = $"u_{tid}";
            await dbContext.SeedUsersAsync(userId);

            var tagZ = new Tag { Name = $"ZTag_{tid}", OwnerId = userId };
            var tagA = new Tag { Name = $"ATag_{tid}", OwnerId = userId };
            var voteTag = new Tag { Name = Tag.VoteTagNames.First(), OwnerId = userId };

            dbContext.Tags.AddRange(tagZ, tagA, voteTag);
            await dbContext.SaveChangesAsync();

            var result = await provider.LoadAllTagsAsync();

            Assert.Contains(result, t => t.Name == tagA.Name);
            Assert.Contains(result, t => t.Name == tagZ.Name);
            Assert.DoesNotContain(result, t => t.Name == voteTag.Name);

            var tagAIndex = result.FindIndex(t => t.Name == tagA.Name);
            var tagZIndex = result.FindIndex(t => t.Name == tagZ.Name);
            Assert.True(tagAIndex < tagZIndex);
        }
    }

    [Fact]
    public async Task LoadAllEdgesAsync_ShouldDelegateToTagEdgeService()
    {
        var (_, provider, edgeServiceMock, _) = CreateScope();
        var fakeEdges = new List<TagEdge>
        {
            new() { Id = 1, SourceTagId = 10, TargetTagId = 20, OwnerId = "user1" }
        };
        edgeServiceMock.Setup(s => s.GetAllEdgesAsync()).ReturnsAsync(fakeEdges);

        var result = await provider.LoadAllEdgesAsync();

        Assert.Single(result);
        Assert.Equal(1, result[0].Id);
        edgeServiceMock.Verify(s => s.GetAllEdgesAsync(), Times.Once);
    }

    [Fact]
    public async Task GetAvailableRightAssetsAsync_ShouldDelegateToTagEdgeService()
    {
        var (_, provider, edgeServiceMock, _) = CreateScope();
        var fakeAssets = new List<RightAsset>
        {
            new() { Id = 1, OwnerId = "u1", TargetTagId = 10, Amount = 5 }
        };
        edgeServiceMock.Setup(s => s.GetAvailableRightAssetsAsync("u1", 10)).ReturnsAsync(fakeAssets);

        var result = await provider.GetAvailableRightAssetsAsync("u1", 10);

        Assert.Single(result);
        Assert.Equal(1, result[0].Id);
        edgeServiceMock.Verify(s => s.GetAvailableRightAssetsAsync("u1", 10), Times.Once);
    }

    [Fact]
    public async Task DelegationMethods_ShouldCallTagEdgeService()
    {
        var (_, provider, edgeServiceMock, _) = CreateScope();

        edgeServiceMock.Setup(s => s.CreateEdgeAsync(1, 2, "u1"))
            .ReturnsAsync(new Success<TagEdge>(new TagEdge { Id = 100, OwnerId = "u1" }));
        edgeServiceMock.Setup(s => s.DeleteEdgeAsync(100, "u1"))
            .ReturnsAsync(new Success<bool>(true));
        edgeServiceMock.Setup(s => s.AttachTagToEdgeAsync(100, 3, 50, "u1", 2))
            .ReturnsAsync(new Success<TagEdgeTagAttachment>(new TagEdgeTagAttachment { Id = 200, OwnerId = "u1" }));
        edgeServiceMock.Setup(s => s.DetachTagFromEdgeAsync(200, "u1"))
            .ReturnsAsync(new Success<bool>(true));

        var createRes = await provider.CreateEdgeAsync(1, 2, "u1");
        Assert.True(createRes is Success<TagEdge> cs && cs.Value.Id == 100);

        var deleteRes = await provider.DeleteEdgeAsync(100, "u1");
        Assert.True(deleteRes is Success<bool> ds && ds.Value);

        var attachRes = await provider.AttachTagToEdgeAsync(100, 3, 50, "u1", 2);
        Assert.True(attachRes is Success<TagEdgeTagAttachment> ats && ats.Value.Id == 200);

        var detachRes = await provider.DetachTagFromEdgeAsync(200, "u1");
        Assert.True(detachRes is Success<bool> dts && dts.Value);
    }
}