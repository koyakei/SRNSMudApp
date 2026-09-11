using Microsoft.EntityFrameworkCore;

using Moq;

using SRNSMudApp.Data;
using SRNSMudApp.Services;
using SRNSMudApp.Tests.TestSupport;

namespace SRNSMudApp.Tests.Services;

public class TagCqsServiceTests : IAsyncLifetime
{
    private MsSqlTestDatabase _sharedDb = null!;

    public async Task InitializeAsync()
    {
        _sharedDb = await SharedMsSqlTestDatabase.GetInstanceAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private (ApplicationDbContext dbContext, TagSearchQueryService queryService, TagCommandService commandService, Mock<ITagEmbeddingService> embeddingMock, string tid) CreateScope()
    {
        var tid = Guid.NewGuid().ToString("N")[..8];
        var dbContext = new ApplicationDbContext(_sharedDb.Options);
        var mockDbFactory = new Mock<IDbContextFactory<ApplicationDbContext>>();
        mockDbFactory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new ApplicationDbContext(_sharedDb.Options));

        var embeddingMock = new Mock<ITagEmbeddingService>();
        var queryService = new TagSearchQueryService(mockDbFactory.Object, embeddingMock.Object);
        var commandService = new TagCommandService(mockDbFactory.Object, embeddingMock.Object);
        return (dbContext, queryService, commandService, embeddingMock, tid);
    }

    [Fact]
    public async Task SearchTagsWithFallbackAsync_WhenTokenCancelled_ReturnsEmptyListWithoutThrowing()
    {
        var (_, queryService, _, _, _) = CreateScope();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await queryService.SearchTagsWithFallbackAsync("any", cts.Token);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task SearchTagsWithFallbackAsync_WhenValueIsEmpty_ReturnsTagsOrderedByName()
    {
        var (dbContext, queryService, _, _, tid) = CreateScope();
        await using (dbContext)
        {
            var userId = $"u_{tid}";
            await dbContext.SeedUsersAsync(userId);

            var tagB = new Tag { Name = $"B_Tag_{tid}", OwnerId = userId };
            var tagA = new Tag { Name = $"A_Tag_{tid}", OwnerId = userId };
            dbContext.Tags.AddRange(tagB, tagA);
            await dbContext.SaveChangesAsync();

            var result = await queryService.SearchTagsWithFallbackAsync(null);

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
        var (dbContext, queryService, _, embeddingMock, tid) = CreateScope();
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

            var result = await queryService.SearchTagsWithFallbackAsync(keyword);

            Assert.Equal(2, result.Count);
            Assert.Contains(result, t => t.Id == tag1.Id);
            Assert.Contains(result, t => t.Id == tag2.Id);
            Assert.DoesNotContain(result, t => t.Id == tagUnrelated.Id);
        }
    }

    [Fact]
    public async Task UpdateTagAsync_WhenAllowedUserGroupIdsProvided_ShouldSyncAutoApproveUserGroups()
    {
        var (dbContext, _, commandService, _, tid) = CreateScope();
        await using (dbContext)
        {
            var userId = $"u_{tid}";
            await dbContext.SeedUsersAsync(userId);

            var group1 = new UserGroup { Name = $"G1_{tid}", OwnerId = userId };
            var group2 = new UserGroup { Name = $"G2_{tid}", OwnerId = userId };
            var group3 = new UserGroup { Name = $"G3_{tid}", OwnerId = userId };
            dbContext.UserGroups.AddRange(group1, group2, group3);

            var tag = new Tag { Name = $"Tag_{tid}", OwnerId = userId };
            dbContext.Tags.Add(tag);
            await dbContext.SaveChangesAsync();

            bool updated = await commandService.UpdateTagAsync(tag.Id, $"Tag_{tid}", "content", false, [group1.Id, group2.Id]);
            Assert.True(updated);

            dbContext.ChangeTracker.Clear();
            var loadedTag = await dbContext.Tags
                .Include(t => t.AutoApproveUserGroups)
                .FirstOrDefaultAsync(t => t.Id == tag.Id);
            Assert.NotNull(loadedTag);
            Assert.Equal(2, loadedTag.AutoApproveUserGroups.Count);
            Assert.Contains(loadedTag.AutoApproveUserGroups, g => g.UserGroupId == group1.Id);
            Assert.Contains(loadedTag.AutoApproveUserGroups, g => g.UserGroupId == group2.Id);

            bool updated2 = await commandService.UpdateTagAsync(tag.Id, $"Tag_{tid}", "content", false, [group1.Id, group3.Id]);
            Assert.True(updated2);

            dbContext.ChangeTracker.Clear();
            var reloadedTag = await dbContext.Tags
                .Include(t => t.AutoApproveUserGroups)
                .FirstOrDefaultAsync(t => t.Id == tag.Id);
            Assert.NotNull(reloadedTag);
            Assert.Equal(2, reloadedTag.AutoApproveUserGroups.Count);
            Assert.Contains(reloadedTag.AutoApproveUserGroups, g => g.UserGroupId == group1.Id);
            Assert.Contains(reloadedTag.AutoApproveUserGroups, g => g.UserGroupId == group3.Id);
            Assert.DoesNotContain(reloadedTag.AutoApproveUserGroups, g => g.UserGroupId == group2.Id);
        }
    }

    [Fact]
    public async Task UpdateTagAsync_WhenNameIsUnchanged_ShouldNotRegenerateEmbedding()
    {
        var (dbContext, _, commandService, embeddingMock, tid) = CreateScope();
        await using (dbContext)
        {
            var userId = $"u_{tid}";
            await dbContext.SeedUsersAsync(userId);
            var tag = new Tag { Name = $"Tag_{tid}", OwnerId = userId };
            dbContext.Tags.Add(tag);
            await dbContext.SaveChangesAsync();

            Assert.True(await commandService.UpdateTagAsync(tag.Id, tag.Name, "Updated content"));

            embeddingMock.Verify(e => e.GenerateEmbeddingAsync(It.IsAny<string>()), Times.Never);
        }
    }

    [Fact]
    public async Task UpdateTagAsync_WhenNameChanges_ShouldRegenerateEmbeddingAfterSavingName()
    {
        var (dbContext, _, commandService, embeddingMock, tid) = CreateScope();
        await using (dbContext)
        {
            var userId = $"u_{tid}";
            await dbContext.SeedUsersAsync(userId);
            var tag = new Tag { Name = $"OldTag_{tid}", OwnerId = userId };
            dbContext.Tags.Add(tag);
            await dbContext.SaveChangesAsync();

            embeddingMock
                .Setup(e => e.GenerateEmbeddingAsync($"NewTag_{tid}"))
                .ReturnsAsync(new ReadOnlyMemory<float>([1.0f, 2.0f]));

            Assert.True(await commandService.UpdateTagAsync(tag.Id, $"NewTag_{tid}", tag.Content));

            embeddingMock.Verify(e => e.GenerateEmbeddingAsync($"NewTag_{tid}"), Times.Once);
            dbContext.ChangeTracker.Clear();
            var savedTag = await dbContext.Tags.FindAsync(tag.Id);
            Assert.NotNull(savedTag);
            Assert.Equal($"NewTag_{tid}", savedTag.Name);
            Assert.Equal([1.0f, 2.0f], savedTag.Embedding);
        }
    }

    /// <summary>
    ///     自動承認委任グループが未指定（空）の状態でタグの内容を更新した際、外部キー制約違反とならず
    ///     自動承認グループが空として保存されることを検証する。
    /// </summary>
    [Fact]
    public async Task UpdateTagAsync_WhenAllowedUserGroupIdsIsEmpty_ShouldUpdateContentAndClearAutoApproveUserGroups()
    {
        var (dbContext, _, commandService, _, tid) = CreateScope();
        await using (dbContext)
        {
            var userId = $"u_{tid}";
            await dbContext.SeedUsersAsync(userId);

            var tag = new Tag
            {
                Name = $"Tag_{tid}",
                Content = "Initial Content",
                OwnerId = userId,
                AutoAcceptIncomingTaggingRequests = false,
            };
            dbContext.Tags.Add(tag);
            await dbContext.SaveChangesAsync();

            // When editing tag content without selecting any auto-approve groups (empty collection)
            bool updated = await commandService.UpdateTagAsync(tag.Id, tag.Name, "Updated Content", false, []);
            Assert.True(updated);

            dbContext.ChangeTracker.Clear();
            var reloaded = await dbContext.Tags.FindAsync(tag.Id);
            Assert.NotNull(reloaded);
            Assert.Equal("Updated Content", reloaded.Content);
            Assert.Empty(reloaded.AutoApproveUserGroups);
        }
    }

    /// <summary>
    ///     設定済みの自動承認委任グループを空にしてタグを更新した際、
    ///     設定済みの自動承認グループが正常に解除されることを検証する。
    /// </summary>
    [Fact]
    public async Task UpdateTagAsync_WhenClearingExistingAutoApproveUserGroup_ShouldClearAutoApproveUserGroups()
    {
        var (dbContext, _, commandService, _, tid) = CreateScope();
        await using (dbContext)
        {
            var userId = $"u_{tid}";
            await dbContext.SeedUsersAsync(userId);

            var group = new UserGroup { Name = $"G_{tid}", OwnerId = userId };
            dbContext.UserGroups.Add(group);
            await dbContext.SaveChangesAsync();

            var tag = new Tag
            {
                Name = $"Tag_{tid}",
                Content = "Initial Content",
                OwnerId = userId,
                AutoAcceptIncomingTaggingRequests = false,
            };
            tag.AutoApproveUserGroups.Add(new TagAutoApproveUserGroup
            {
                Tag = tag,
                UserGroupId = group.Id,
                OwnerId = userId
            });
            dbContext.Tags.Add(tag);
            await dbContext.SaveChangesAsync();

            // Clear allowed user groups
            bool updated = await commandService.UpdateTagAsync(tag.Id, tag.Name, "Updated Content", false, []);
            Assert.True(updated);

            dbContext.ChangeTracker.Clear();
            var reloaded = await dbContext.Tags.FindAsync(tag.Id);
            Assert.NotNull(reloaded);
            Assert.Equal("Updated Content", reloaded.Content);
            Assert.Empty(reloaded.AutoApproveUserGroups);
        }
    }
}