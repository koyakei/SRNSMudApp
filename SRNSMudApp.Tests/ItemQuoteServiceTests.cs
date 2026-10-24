using Microsoft.EntityFrameworkCore;

using Moq;

using SRNSMudApp.Data;
using SRNSMudApp.Services;
using SRNSMudApp.Services.Resolvers;

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

        IItemSourceResolver[] sourceResolvers =
        [
            new QuotedItemIdSourceResolver(mockDbFactory.Object),
            new ItemSplitRequestSourceResolver(mockDbFactory.Object),
            new ItemLinkSourceResolver(mockDbFactory.Object)
        ];
        var service = new ItemQuoteService(mockDbFactory.Object, sourceResolvers);
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

    [Fact]
    public async Task GetSourceItemAsync_WithQuotedItemId_ShouldReturnSourceItem()
    {
        var (dbContext, service, tid) = CreateScope();
        await using (dbContext)
        {
            var userId = $"user_{tid}";
            await dbContext.SeedUsersAsync(userId);

            var originalItem = new Item { Content = $"Original_{tid}", OwnerId = userId };
            dbContext.Items.Add(originalItem);
            await dbContext.SaveChangesAsync();

            var splitItem = new Item { Content = $"Split_{tid}", OwnerId = userId, QuotedItemId = originalItem.Id };
            dbContext.Items.Add(splitItem);
            await dbContext.SaveChangesAsync();

            Item? source = await service.GetSourceItemAsync(splitItem.Id);
            Assert.NotNull(source);
            Assert.Equal(originalItem.Id, source.Id);
            Assert.Equal(originalItem.Content, source.Content);
        }
    }

    [Fact]
    public async Task GetSourceItemAsync_FallbackWithItemSplitRequest_ShouldReturnOriginalItem()
    {
        var (dbContext, service, tid) = CreateScope();
        await using (dbContext)
        {
            var userId = $"user_{tid}";
            await dbContext.SeedUsersAsync(userId);

            var originalItem = new Item { Content = $"Original_{tid}", OwnerId = userId };
            dbContext.Items.Add(originalItem);
            await dbContext.SaveChangesAsync();

            var splitItem = new Item { Content = $"Split_{tid}", OwnerId = userId }; // QuotedItemId は未設定
            dbContext.Items.Add(splitItem);
            await dbContext.SaveChangesAsync();

            var request = new ItemSplitRequest
            {
                OriginalItemId = originalItem.Id,
                CreatedItemId = splitItem.Id,
                RequesterUserId = userId,
                OwnerUserId = userId,
                SelectedText = "Split",
                Status = TradeStatus.Executed,
                OwnerId = userId
            };
            dbContext.ItemSplitRequests.Add(request);
            await dbContext.SaveChangesAsync();

            Item? source = await service.GetSourceItemAsync(splitItem.Id);
            Assert.NotNull(source);
            Assert.Equal(originalItem.Id, source.Id);
        }
    }

    [Fact]
    public async Task GetSourceItemAsync_FallbackWithContentLink_ShouldReturnLinkingItem()
    {
        var (dbContext, service, tid) = CreateScope();
        await using (dbContext)
        {
            var userId = $"user_{tid}";
            await dbContext.SeedUsersAsync(userId);

            var splitItem = new Item { Content = $"Split_{tid}", OwnerId = userId };
            dbContext.Items.Add(splitItem);
            await dbContext.SaveChangesAsync();

            var originalItem = new Item { Content = $"前文 /ItemDetail/{splitItem.Id} 後文", OwnerId = userId };
            dbContext.Items.Add(originalItem);
            await dbContext.SaveChangesAsync();

            Item? source = await service.GetSourceItemAsync(splitItem.Id);
            Assert.NotNull(source);
            Assert.Equal(originalItem.Id, source.Id);
        }
    }

    [Fact]
    public async Task GetSourceItemAsync_WhenNoSource_ShouldReturnNull()
    {
        var (dbContext, service, tid) = CreateScope();
        await using (dbContext)
        {
            var userId = $"user_{tid}";
            await dbContext.SeedUsersAsync(userId);

            var standaloneItem = new Item { Content = $"Standalone_{tid}", OwnerId = userId };
            dbContext.Items.Add(standaloneItem);
            await dbContext.SaveChangesAsync();

            Item? source = await service.GetSourceItemAsync(standaloneItem.Id);
            Assert.Null(source);
        }
    }

    [Fact]
    public async Task CreateQuoteItemAsync_WhenTargetIsPrivateOrGroup_ShouldInheritAttributes()
    {
        var (dbContext, service, tid) = CreateScope();
        await using (dbContext)
        {
            var userId = $"user_{tid}";
            await dbContext.SeedUsersAsync(userId);

            var group = new UserGroup { Name = $"Group_{tid}", OwnerId = userId };
            dbContext.UserGroups.Add(group);
            await dbContext.SaveChangesAsync();

            var privateTargetItem = new Item
            {
                Content = $"PrivateTarget_{tid}",
                OwnerId = userId,
                IsPrivate = true,
                TargetUserGroupId = group.Id
            };
            dbContext.Items.Add(privateTargetItem);
            await dbContext.SaveChangesAsync();

            Item? quoteItem = await service.CreateQuoteItemAsync(privateTargetItem.Id, "Quote Private", userId);

            Assert.NotNull(quoteItem);
            Assert.True(quoteItem.IsPrivate);
            Assert.Equal(group.Id, quoteItem.TargetUserGroupId);

            Item? saved = await dbContext.Items.FirstOrDefaultAsync(i => i.Id == quoteItem.Id);
            Assert.NotNull(saved);
            Assert.True(saved.IsPrivate);
            Assert.Equal(group.Id, saved.TargetUserGroupId);
        }
    }

    [Fact]
    public async Task CreateQuoteItemAsync_WhenTargetIsPublic_CreatesPublicQuoteItem()
    {
        var (dbContext, service, tid) = CreateScope();
        await using (dbContext)
        {
            var userId = $"user_{tid}";
            await dbContext.SeedUsersAsync(userId);

            var publicTargetItem = new Item
            {
                Content = $"PublicTarget_{tid}",
                OwnerId = userId,
                IsPrivate = false,
                TargetUserGroupId = null
            };
            dbContext.Items.Add(publicTargetItem);
            await dbContext.SaveChangesAsync();

            Item? quoteItem = await service.CreateQuoteItemAsync(publicTargetItem.Id, "Quote Public", userId);

            Assert.NotNull(quoteItem);
            Assert.False(quoteItem.IsPrivate);
            Assert.Null(quoteItem.TargetUserGroupId);

            Item? saved = await dbContext.Items.FirstOrDefaultAsync(i => i.Id == quoteItem.Id);
            Assert.NotNull(saved);
            Assert.False(saved.IsPrivate);
            Assert.Null(saved.TargetUserGroupId);
        }
    }

    [Fact]
    public async Task CreateQuoteItemAsync_WhenTargetIsPrivateWithoutGroup_CreatesPrivateQuoteItemWithoutGroup()
    {
        var (dbContext, service, tid) = CreateScope();
        await using (dbContext)
        {
            var userId = $"user_{tid}";
            await dbContext.SeedUsersAsync(userId);

            var privateTargetItem = new Item
            {
                Content = $"PrivateTarget_{tid}",
                OwnerId = userId,
                IsPrivate = true,
                TargetUserGroupId = null
            };
            dbContext.Items.Add(privateTargetItem);
            await dbContext.SaveChangesAsync();

            Item? quoteItem = await service.CreateQuoteItemAsync(privateTargetItem.Id, "Quote Private Solo", userId);

            Assert.NotNull(quoteItem);
            Assert.True(quoteItem.IsPrivate);
            Assert.Null(quoteItem.TargetUserGroupId);

            Item? saved = await dbContext.Items.FirstOrDefaultAsync(i => i.Id == quoteItem.Id);
            Assert.NotNull(saved);
            Assert.True(saved.IsPrivate);
            Assert.Null(saved.TargetUserGroupId);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateQuoteItemAsync_WhenContentIsNullOrWhiteSpace_ThrowsArgumentException(string? invalidContent)
    {
        var (dbContext, service, tid) = CreateScope();
        await using (dbContext)
        {
            var userId = $"user_{tid}";
            await dbContext.SeedUsersAsync(userId);

            var targetItem = new Item { Content = $"Target_{tid}", OwnerId = userId };
            dbContext.Items.Add(targetItem);
            await dbContext.SaveChangesAsync();

            await Assert.ThrowsAsync<ArgumentException>(
                () => service.CreateQuoteItemAsync(targetItem.Id, invalidContent!, userId));
        }
    }

    [Fact]
    public async Task GetQuotedByItemsAsync_WhenQuotedByPrivateItem_FiltersOutForUnauthorizedUser()
    {
        var (dbContext, service, tid) = CreateScope();
        await using (dbContext)
        {
            var userA = $"uA_{tid}";
            var userB = $"uB_{tid}";
            var userC = $"uC_{tid}";
            var adminUser = $"uAdmin_{tid}";
            await dbContext.SeedUsersAsync(userA, userB, userC, adminUser);

            var targetItem = new Item { Content = $"Target_{tid}", OwnerId = userA };
            dbContext.Items.Add(targetItem);
            await dbContext.SaveChangesAsync();

            var privateQuote = new Item
            {
                Content = $"PrivateQuote_{tid}",
                OwnerId = userB,
                QuotedItemId = targetItem.Id,
                IsPrivate = true,
                TargetUserGroupId = null
            };
            dbContext.Items.Add(privateQuote);
            await dbContext.SaveChangesAsync();

            // Act 1: 権限のない第三者 (userC)
            IReadOnlyList<Item> quotesForUserC = await service.GetQuotedByItemsAsync(targetItem.Id, userC, isAdmin: false);
            Assert.Empty(quotesForUserC);

            // Act 2: 未ログインユーザー (null)
            IReadOnlyList<Item> quotesForAnon = await service.GetQuotedByItemsAsync(targetItem.Id, null, isAdmin: false);
            Assert.Empty(quotesForAnon);

            // Act 3: 投稿者本人 (userB)
            IReadOnlyList<Item> quotesForOwner = await service.GetQuotedByItemsAsync(targetItem.Id, userB, isAdmin: false);
            Assert.Single(quotesForOwner);
            Assert.Equal(privateQuote.Id, quotesForOwner[0].Id);

            // Act 4: 管理者 (adminUser)
            IReadOnlyList<Item> quotesForAdmin = await service.GetQuotedByItemsAsync(targetItem.Id, adminUser, isAdmin: true);
            Assert.Single(quotesForAdmin);
            Assert.Equal(privateQuote.Id, quotesForAdmin[0].Id);
        }
    }
}