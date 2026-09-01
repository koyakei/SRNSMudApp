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
    public async Task GetAvailableRightAssetsAsync_ShouldReturnOnlyValidUnburnedAssets()
    {
        var (dbContext, provider, _, tid) = CreateScope();
        await using (dbContext)
        {
            var userId = $"u_{tid}";
            var otherUser = $"other_{tid}";
            await dbContext.SeedUsersAsync(userId, otherUser);

            var tag = new Tag { Name = $"Tag_{tid}", OwnerId = userId };
            var otherTag = new Tag { Name = $"OtherTag_{tid}", OwnerId = userId };
            dbContext.Tags.AddRange(tag, otherTag);
            await dbContext.SaveChangesAsync();

            var validAsset1 = new RightAsset { OwnerId = userId, TargetTagId = tag.Id, Amount = 5, IsBurned = false };
            var validAsset2 = new RightAsset { OwnerId = userId, TargetTagId = tag.Id, Amount = 10, IsBurned = false };
            var burnedAsset = new RightAsset { OwnerId = userId, TargetTagId = tag.Id, Amount = 3, IsBurned = true };
            var zeroAmountAsset = new RightAsset { OwnerId = userId, TargetTagId = tag.Id, Amount = 0, IsBurned = false };
            var otherUserAsset = new RightAsset { OwnerId = otherUser, TargetTagId = tag.Id, Amount = 5, IsBurned = false };
            var otherTagAsset = new RightAsset { OwnerId = userId, TargetTagId = otherTag.Id, Amount = 5, IsBurned = false };

            dbContext.RightAssets.AddRange(validAsset1, validAsset2, burnedAsset, zeroAmountAsset, otherUserAsset, otherTagAsset);
            await dbContext.SaveChangesAsync();

            var result = await provider.GetAvailableRightAssetsAsync(userId, tag.Id);

            Assert.Equal(2, result.Count);
            Assert.Equal(10, result[0].Amount); // Ordered by Amount descending
            Assert.Equal(5, result[1].Amount);
            Assert.DoesNotContain(result, r => r.Id == burnedAsset.Id);
            Assert.DoesNotContain(result, r => r.Id == zeroAmountAsset.Id);
            Assert.DoesNotContain(result, r => r.Id == otherUserAsset.Id);
            Assert.DoesNotContain(result, r => r.Id == otherTagAsset.Id);
        }
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