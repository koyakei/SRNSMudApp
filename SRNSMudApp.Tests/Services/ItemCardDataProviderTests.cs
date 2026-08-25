#region

using Microsoft.EntityFrameworkCore;
using SRNSMudApp.Data;
using SRNSMudApp.Services;
using SRNSMudApp.Tests.TestSupport;
using Xunit;

#endregion

namespace SRNSMudApp.Tests.Services;

/// <summary>
/// <see cref="ItemCardDataProvider" /> の投票トグルロジックの単体テスト (MSSQL Testcontainers)。
/// </summary>
public class ItemCardDataProviderTests : IAsyncLifetime
{
    private MsSqlTestDatabase _sharedDb = null!;

    public async Task InitializeAsync()
    {
        _sharedDb = await SharedMsSqlTestDatabase.GetInstanceAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(ApplicationDbContext db, ItemCardDataProvider sut, string userId, int goodTagId, int itemId, string tid)> CreateScopeAsync()
    {
        var tid = Guid.NewGuid().ToString("N")[..8];
        var db = new ApplicationDbContext(_sharedDb.Options);
        var sut = new ItemCardDataProvider(new DbContextFactoryStub(_sharedDb.Options));

        var userId = $"voter_{tid}";
        var systemId = $"sys_{tid}";
        var authorId = $"author_{tid}";

        await db.SeedUsersAsync(userId, systemId, authorId);

        var tag = new Tag { Name = $"good_{tid}", IsSystem = true, OwnerId = systemId, CachedWeight = 0 };
        var item = new SRNSMudApp.Data.Item { Content = $"target_{tid}", OwnerId = authorId };

        db.Tags.Add(tag);
        db.Items.Add(item);
        await db.SaveChangesAsync();

        return (db, sut, userId, tag.Id, item.Id, tid);
    }

    [Fact]
    public async Task ToggleItemVote_FirstClick_AddsRelationWithTargetWeight()
    {
        var (db, sut, userId, goodTagId, itemId, tid) = await CreateScopeAsync();
        await using (db)
        {
            ItemVoteResult result = await sut.ToggleItemVoteAsync(itemId, userId, goodTagId, 1);

            Assert.Equal(ItemVoteAction.Added, result.Action);
            Assert.Equal(1, result.Weight);
            TagRelation? relation = await db.TagRelations.SingleAsync(tr => tr.ItemId == itemId && tr.OwnerId == userId);
            Assert.Equal(goodTagId, relation.TagId);
            Assert.Equal(1, relation.Weight);
        }
    }

    [Fact]
    public async Task ToggleItemVote_SecondDifferentWeight_UpdatesAndWritesLedger()
    {
        var (db, sut, userId, goodTagId, itemId, tid) = await CreateScopeAsync();
        await using (db)
        {
            _ = await sut.ToggleItemVoteAsync(itemId, userId, goodTagId, 1);

            ItemVoteResult result = await sut.ToggleItemVoteAsync(itemId, userId, goodTagId, -1);

            Assert.Equal(ItemVoteAction.Updated, result.Action);
            TagRelation relation = await db.TagRelations.SingleAsync(tr => tr.Id == result.RelationId);
            Assert.Equal(-1, relation.Weight);
            TagWeightLedger ledger = await db.TagWeightLedgers.SingleAsync(l => l.SourceId == relation.Id && l.SourceType == "TagRelationUpdate");
            Assert.Equal(-2, ledger.Delta);
            Assert.Equal(-2, ledger.NewWeight - ledger.PreviousWeight);
            TimelineEvent timeline = await db.TimelineEvents!.SingleAsync(e => e.FollowedTagId == goodTagId && e.EventType == "Update");
            Assert.Equal(1, timeline.PreviousWeight);
            Assert.Equal(-1, timeline.NewWeight);
        }
    }

    [Fact]
    public async Task ToggleItemVote_SameWeightTwice_CancelsVoteAndRemovesRelation()
    {
        var (db, sut, userId, goodTagId, itemId, tid) = await CreateScopeAsync();
        await using (db)
        {
            _ = await sut.ToggleItemVoteAsync(itemId, userId, goodTagId, 1);
            var cachedAfterAdd = (await db.Tags.FindAsync(goodTagId))!.CachedWeight;

            ItemVoteResult result = await sut.ToggleItemVoteAsync(itemId, userId, goodTagId, 1);

            Assert.Equal(ItemVoteAction.Removed, result.Action);
            Assert.False(await db.TagRelations.AnyAsync(tr => tr.ItemId == itemId && tr.OwnerId == userId));
            Assert.Equal(cachedAfterAdd, (await db.Tags.FindAsync(goodTagId))!.CachedWeight);
            TimelineEvent timeline = await db.TimelineEvents!.SingleAsync(e => e.FollowedTagId == goodTagId && e.EventType == "Delete");
            Assert.Equal(1, timeline.PreviousWeight);
        }
    }

    [Fact]
    public async Task CreateItemAsync_WithExistingOwner_DoesNotDuplicateUserAndStoresItem()
    {
        var (db, sut, userId, goodTagId, itemId, tid) = await CreateScopeAsync();
        await using (db)
        {
            var authorId = $"author_{tid}";
            var newItem = new SRNSMudApp.Data.Item
            {
                Content = $"New item created by existing author_{tid}",
                OwnerId = authorId
            };

            await sut.CreateItemAsync(newItem, [goodTagId]);

            Assert.Equal(1, await db.Users.CountAsync(u => u.Id == authorId));
            var saved = await db.Items.FirstOrDefaultAsync(i => i.Content == $"New item created by existing author_{tid}");
            Assert.NotNull(saved);
            Assert.Equal(authorId, saved.OwnerId);
            Assert.True(await db.TagRelations.AnyAsync(tr => tr.ItemId == saved.Id && tr.TagId == goodTagId));
        }
    }

    /// <summary>テスト用のシンプルなファクトリ。</summary>
    private sealed class DbContextFactoryStub(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);

        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }
}