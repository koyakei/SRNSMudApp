#region

using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Services;

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
        var item = new Item { Content = $"target_{tid}", OwnerId = authorId };

        db.Tags.Add(tag);
        db.Items.Add(item);
        await db.SaveChangesAsync();

        return (db, sut, userId, tag.Id, item.Id, tid);
    }



    [Fact]
    public async Task CreateItemAsync_WithExistingOwner_DoesNotDuplicateUserAndStoresItem()
    {
        var (db, sut, _, goodTagId, _, tid) = await CreateScopeAsync();
        await using (db)
        {
            var authorId = $"author_{tid}";
            var newItem = new Item
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