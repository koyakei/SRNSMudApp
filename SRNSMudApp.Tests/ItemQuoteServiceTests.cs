using Microsoft.EntityFrameworkCore;

using Moq;

using SRNSMudApp.Data;
using SRNSMudApp.Services;

namespace SRNSMudApp.Tests;

public class ItemQuoteServiceTests : IAsyncLifetime
{
    private MsSqlTestDatabase _sharedDb = null!;

    public async Task InitializeAsync()
    {
        _sharedDb = await SharedMsSqlTestDatabase.GetInstanceAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private (ApplicationDbContext dbContext, ItemQuoteService service, string tid) CreateScope()
    {
        var tid = Guid.NewGuid().ToString("N")[..8];
        var dbContext = new ApplicationDbContext(_sharedDb.Options);
        var mockDbFactory = new Mock<IDbContextFactory<ApplicationDbContext>>();
        mockDbFactory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new ApplicationDbContext(_sharedDb.Options));

        var service = new ItemQuoteService(mockDbFactory.Object);
        return (dbContext, service, tid);
    }

    [Fact]
    public async Task CreateQuoteItemAsync_ShouldCreateQuoteItemWithQuotedItemIdAndRelations()
    {
        var (dbContext, service, tid) = CreateScope();
        await using (dbContext)
        {
            var userId = $"user_{tid}";
            await dbContext.SeedUsersAsync(userId);

            var targetItem = new Item { Content = $"TargetItem_{tid}", OwnerId = userId };
            dbContext.Items.Add(targetItem);
            await dbContext.SaveChangesAsync();

            var quoteContent = $"Quote Content_{tid}";
            Item? quoteItem = await service.CreateQuoteItemAsync(targetItem.Id, quoteContent, userId);

            Assert.NotNull(quoteItem);
            Assert.Equal(targetItem.Id, quoteItem.QuotedItemId);
            Assert.Equal(userId, quoteItem.OwnerId);
            Assert.Equal(quoteContent, quoteItem.Content);
            Assert.NotNull(quoteItem.Owner);
            Assert.Equal(userId, quoteItem.Owner.Id);
            Assert.NotNull(quoteItem.QuotedItem);
            Assert.Equal(targetItem.Id, quoteItem.QuotedItem.Id);

            Item? saved = await dbContext.Items.FirstOrDefaultAsync(i => i.Id == quoteItem.Id);
            Assert.NotNull(saved);
            Assert.Equal(targetItem.Id, saved.QuotedItemId);
        }
    }

    [Fact]
    public async Task CreateQuoteItemAsync_WhenTargetItemDoesNotExist_ShouldReturnNull()
    {
        var (dbContext, service, tid) = CreateScope();
        await using (dbContext)
        {
            var userId = $"user_{tid}";
            await dbContext.SeedUsersAsync(userId);

            Item? quoteItem = await service.CreateQuoteItemAsync(999999, "Content", userId);
            Assert.Null(quoteItem);
        }
    }

    [Fact]
    public async Task CreateQuoteItemAsync_WithInitialTags_ShouldAddTagRelations()
    {
        var (dbContext, service, tid) = CreateScope();
        await using (dbContext)
        {
            var userId = $"user_{tid}";
            await dbContext.SeedUsersAsync(userId);

            var targetItem = new Item { Content = $"Target_{tid}", OwnerId = userId };
            var tag = new Tag { Name = $"Tag_{tid}", OwnerId = userId };
            dbContext.Items.Add(targetItem);
            dbContext.Tags.Add(tag);
            await dbContext.SaveChangesAsync();

            Item? quoteItem = await service.CreateQuoteItemAsync(targetItem.Id, "Quote with tags", userId, [tag.Id]);

            Assert.NotNull(quoteItem);
            Assert.Single(quoteItem.TagRelations);
            Assert.Equal(tag.Id, quoteItem.TagRelations.First().TagId);
        }
    }

    [Fact]
    public async Task GetQuotedByItemsAsync_ShouldReturnItemsQuotingTargetItem()
    {
        var (dbContext, service, tid) = CreateScope();
        await using (dbContext)
        {
            var userId = $"user_{tid}";
            await dbContext.SeedUsersAsync(userId);

            var targetItem = new Item { Content = $"Target_{tid}", OwnerId = userId };
            dbContext.Items.Add(targetItem);
            await dbContext.SaveChangesAsync();

            _ = await service.CreateQuoteItemAsync(targetItem.Id, $"Quote 1_{tid}", userId);
            _ = await service.CreateQuoteItemAsync(targetItem.Id, $"Quote 2_{tid}", userId);

            IReadOnlyList<Item> quotedByItems = await service.GetQuotedByItemsAsync(targetItem.Id);

            Assert.Equal(2, quotedByItems.Count);
            Assert.All(quotedByItems, item => Assert.Equal(targetItem.Id, item.QuotedItemId));
        }
    }

    [Fact]
    public async Task GetQuoteCountAsync_ShouldReturnCorrectCount()
    {
        var (dbContext, service, tid) = CreateScope();
        await using (dbContext)
        {
            var userId = $"user_{tid}";
            await dbContext.SeedUsersAsync(userId);

            var targetItem = new Item { Content = $"Target_{tid}", OwnerId = userId };
            dbContext.Items.Add(targetItem);
            await dbContext.SaveChangesAsync();

            Assert.Equal(0, await service.GetQuoteCountAsync(targetItem.Id));

            _ = await service.CreateQuoteItemAsync(targetItem.Id, $"Quote 1_{tid}", userId);
            Assert.Equal(1, await service.GetQuoteCountAsync(targetItem.Id));

            _ = await service.CreateQuoteItemAsync(targetItem.Id, $"Quote 2_{tid}", userId);
            Assert.Equal(2, await service.GetQuoteCountAsync(targetItem.Id));
        }
    }

    [Fact]
    public async Task GetQuotedItemAsync_ShouldReturnTargetItem()
    {
        var (dbContext, service, tid) = CreateScope();
        await using (dbContext)
        {
            var userId = $"user_{tid}";
            await dbContext.SeedUsersAsync(userId);

            var targetItem = new Item { Content = $"Target_{tid}", OwnerId = userId };
            dbContext.Items.Add(targetItem);
            await dbContext.SaveChangesAsync();

            Item? retrieved = await service.GetQuotedItemAsync(targetItem.Id);
            Assert.NotNull(retrieved);
            Assert.Equal(targetItem.Id, retrieved.Id);
            Assert.Equal(targetItem.Content, retrieved.Content);
        }
    }
}

