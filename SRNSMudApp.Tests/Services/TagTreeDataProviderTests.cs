#region

using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;

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

    [Fact]
    public async Task RequestTagMoveAsync_CreatesTaggingRequestWithMoveContractAndProposedStatus()
    {
        var (context, provider, testUserId, systemUserId, tid) = await CreateScopeAsync();
        await using (context)
        {
            var otherUserId = $"other_{tid}";
            await context.SeedUsersAsync(otherUserId);

            var tagToMove = new Tag { Name = $"MovedTag_{tid}", OwnerId = otherUserId };
            var newParentTag = new Tag { Name = $"ParentTag_{tid}", OwnerId = systemUserId };
            context.Tags.AddRange(tagToMove, newParentTag);
            _ = await context.SaveChangesAsync();

            Result<TaggingRequestEntity> result = await provider.RequestTagMoveAsync(testUserId, tagToMove.Id, newParentTag.Id);

            if (result is not Success<TaggingRequestEntity> success)
            {
                Assert.Fail("Result is not Success");
                return;
            }

            TaggingRequestEntity req = success.Value;
            Assert.Equal(ContractTypes.Move, req.ContractType);
            Assert.Equal(TaggingRequestType.Move, req.RequestType);
            Assert.Equal(testUserId, req.RequesterUserId);
            Assert.Equal(otherUserId, req.TagOwnerUserId);
            Assert.Equal(tagToMove.Id, req.RequestedTagId);
            Assert.Equal(TradeStatus.Proposed, req.Status);
            Assert.True(req.Payload is TagMovePayload payload && payload.NewParentTagId == newParentTag.Id);

            // Also check DB persistence
            TaggingRequestEntity? inDb = await context.TaggingRequestEntities.FirstOrDefaultAsync(r => r.Id == req.Id);
            Assert.NotNull(inDb);
            Assert.Equal(ContractTypes.Move, inDb.ContractType);
        }
    }

    [Fact]
    public async Task LoadPendingTagMovesAsync_ReturnsProposedMoveRequestsOnly()
    {
        var (context, provider, testUserId, systemUserId, tid) = await CreateScopeAsync();
        await using (context)
        {
            var otherUserId = $"other_{tid}";
            await context.SeedUsersAsync(otherUserId);

            var tag = new Tag { Name = $"Tag_{tid}", OwnerId = otherUserId };
            var parentTag = new Tag { Name = $"Parent_{tid}", OwnerId = systemUserId };
            context.Tags.AddRange(tag, parentTag);
            _ = await context.SaveChangesAsync();

            Result<TaggingRequestEntity> reqResult = await provider.RequestTagMoveAsync(testUserId, tag.Id, parentTag.Id);
            Assert.True(reqResult is Success<TaggingRequestEntity>);

            List<PendingTagMoveDto> moves = await provider.LoadPendingTagMovesAsync();

            PendingTagMoveDto? matching = moves.FirstOrDefault(m => m.TagId == tag.Id);
            Assert.NotNull(matching);
            Assert.Equal(parentTag.Id, matching.NewParentTagId);
            Assert.Equal(testUserId, matching.RequesterUserId);
            Assert.Equal(otherUserId, matching.TagOwnerUserId);
            Assert.Equal(tag.Name, matching.TagName);
        }
    }

    [Fact]
    public async Task CancelTagMoveAsync_UpdatesStatusToCanceled_WhenAuthorized()
    {
        var (context, provider, testUserId, systemUserId, tid) = await CreateScopeAsync();
        await using (context)
        {
            var otherUserId = $"other_{tid}";
            await context.SeedUsersAsync(otherUserId);

            var tag = new Tag { Name = $"Tag_{tid}", OwnerId = otherUserId };
            var parentTag = new Tag { Name = $"Parent_{tid}", OwnerId = systemUserId };
            context.Tags.AddRange(tag, parentTag);
            _ = await context.SaveChangesAsync();

            Result<TaggingRequestEntity> reqResult = await provider.RequestTagMoveAsync(testUserId, tag.Id, parentTag.Id);
            if (reqResult is not Success<TaggingRequestEntity> s)
            {
                Assert.Fail("Failed to create move request");
                return;
            }

            Result<string> cancelResult = await provider.CancelTagMoveAsync(s.Value.Id, testUserId);
            Assert.True(cancelResult is Success<string>);

            TaggingRequestEntity? inDb = await context.TaggingRequestEntities.FirstOrDefaultAsync(r => r.Id == s.Value.Id);
            Assert.NotNull(inDb);
            Assert.Equal(TradeStatus.Canceled, inDb.Status);
        }
    }

    [Fact]
    public async Task CancelTagMoveAsync_ReturnsFailure_WhenUnauthorized()
    {
        var (context, provider, testUserId, systemUserId, tid) = await CreateScopeAsync();
        await using (context)
        {
            var otherUserId = $"other_{tid}";
            var thirdUserId = $"third_{tid}";
            await context.SeedUsersAsync(otherUserId, thirdUserId);

            var tag = new Tag { Name = $"Tag_{tid}", OwnerId = otherUserId };
            var parentTag = new Tag { Name = $"Parent_{tid}", OwnerId = systemUserId };
            context.Tags.AddRange(tag, parentTag);
            _ = await context.SaveChangesAsync();

            Result<TaggingRequestEntity> reqResult = await provider.RequestTagMoveAsync(testUserId, tag.Id, parentTag.Id);
            if (reqResult is not Success<TaggingRequestEntity> s)
            {
                Assert.Fail("Failed to create move request");
                return;
            }

            Result<string> cancelResult = await provider.CancelTagMoveAsync(s.Value.Id, thirdUserId);
            Assert.True(cancelResult is Failure);
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