#region

using Microsoft.EntityFrameworkCore;
using SRNSMudApp.Data;
using SRNSMudApp.Services;
using SRNSMudApp.Tests.TestSupport;
using Xunit;

#endregion

namespace SRNSMudApp.Tests.Services;

public class TagTreeDataProviderTests : IAsyncLifetime
{
    private MsSqlTestDatabase _sharedDb = null!;

    public async Task InitializeAsync()
    {
        _sharedDb = await SharedMsSqlTestDatabase.GetInstanceAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(ApplicationDbContext context, TagTreeDataProvider provider, string testUserId, string systemUserId, string tid)> CreateScopeAsync()
    {
        var tid = Guid.NewGuid().ToString("N")[..8];
        var context = new ApplicationDbContext(_sharedDb.Options);
        var provider = new TagTreeDataProvider(new SingleContextDbFactory(_sharedDb.Options));

        var testUserId = $"user_{tid}";
        var systemUserId = $"sys_{tid}";
        await context.SeedUsersAsync(testUserId, systemUserId);

        return (context, provider, testUserId, systemUserId, tid);
    }

    [Fact]
    public async Task LoadTagsAsync_WhenRootIsSystemClassificationTag_IncludesSystemRootAndChildren()
    {
        var (context, provider, testUserId, systemUserId, tid) = await CreateScopeAsync();
        await using (context)
        {
            var rootTag = new Tag { Name = $"SystemRoot_{tid}", IsSystem = true, OwnerId = systemUserId };
            context.Tags.Add(rootTag);
            _ = await context.SaveChangesAsync();

            var child1 = new Tag
            {
                Name = $"UserChild1_{tid}",
                ParentTagId = rootTag.Id,
                IsSystem = false,
                OwnerId = testUserId
            };
            var child2 = new Tag
            {
                Name = $"UserChild2_{tid}",
                ParentTagId = rootTag.Id,
                IsSystem = false,
                OwnerId = testUserId
            };
            context.Tags.AddRange(child1, child2);
            _ = await context.SaveChangesAsync();

            List<Tag> tags = await provider.LoadTagsAsync();

            Assert.Contains(tags, t => t.Id == rootTag.Id);
            Assert.Contains(tags, t => t.Id == child1.Id);
            Assert.Contains(tags, t => t.Id == child2.Id);
        }
    }

    [Fact]
    public async Task LoadTagsAsync_ExcludesVoteAndReactionTagsButKeepsOtherSystemAndUserTags()
    {
        var (context, provider, testUserId, systemUserId, tid) = await CreateScopeAsync();
        await using (context)
        {
            var systemTag = new Tag { Name = $"SystemOnly_{tid}", IsSystem = true, OwnerId = systemUserId };
            var voteTag = new Tag { Name = "good", IsSystem = true, OwnerId = systemUserId };
            var reactionTag = new Tag { Name = "真実", IsSystem = true, OwnerId = systemUserId };
            var userTag = new Tag { Name = $"UserVisible_{tid}", IsSystem = false, OwnerId = testUserId };
            context.Tags.AddRange(systemTag, voteTag, reactionTag, userTag);
            _ = await context.SaveChangesAsync();

            List<Tag> tags = await provider.LoadTagsAsync();

            Assert.Contains(tags, t => t.Id == systemTag.Id);
            Assert.Contains(tags, t => t.Id == userTag.Id);
            Assert.DoesNotContain(tags, t => t.Id == voteTag.Id);
            Assert.DoesNotContain(tags, t => t.Id == reactionTag.Id);
        }
    }

    private sealed class SingleContextDbFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);

        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ApplicationDbContext(options));
    }
}
