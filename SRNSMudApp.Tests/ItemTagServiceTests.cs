using Microsoft.EntityFrameworkCore;

using Moq;

using SRNSMudApp.Data;
using SRNSMudApp.Services;

namespace SRNSMudApp.Tests.Services;

public class ItemTagServiceTests : IAsyncLifetime
{
    private MsSqlTestDatabase _sharedDb = null!;

    public async Task InitializeAsync()
    {
        _sharedDb = await SharedMsSqlTestDatabase.GetInstanceAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private (ApplicationDbContext dbContext, ItemTagService service, string tid) CreateScope()
    {
        var tid = Guid.NewGuid().ToString("N")[..8];
        var dbContext = new ApplicationDbContext(_sharedDb.Options);
        var mockDbFactory = new Mock<IDbContextFactory<ApplicationDbContext>>();
        mockDbFactory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new ApplicationDbContext(_sharedDb.Options));

        var service = new ItemTagService(mockDbFactory.Object);
        return (dbContext, service, tid);
    }

    [Fact]
    public async Task AddReplyToRequestAsync_ShouldCreateItemAndReturnItWithRelations()
    {
        // Arrange
        await using var scope = CreateScope().dbContext;
        var (dbContext, service, tid) = CreateScope();
        await using (dbContext)
        {
            var userId = $"user_{tid}";
            await dbContext.SeedUsersAsync(userId);

            var targetItem = new Item { Content = $"TargetItem_{tid}", OwnerId = userId };
            var tag = new Tag { Name = $"Tag_{tid}", OwnerId = userId };
            dbContext.Items.Add(targetItem);
            dbContext.Tags.Add(tag);
            await dbContext.SaveChangesAsync();

            var request = new TaggingRequestEntity
            {
                ContractType = "Gratis",
                OwnerId = userId,
                TargetItemId = targetItem.Id,
                RequestedTagId = tag.Id,
                RequesterUserId = userId,
                TagOwnerUserId = userId
            };
            dbContext.TaggingRequestEntities.Add(request);
            await dbContext.SaveChangesAsync();

            var message = $"This is a test reply_{tid}";

            // Act
            Item? replyItem = await service.AddReplyToRequestAsync(request.Id, userId, message);

            // Assert
            Assert.NotNull(replyItem);
            Assert.Equal(request.Id, replyItem.TaggingRequestEntityId);
            Assert.Equal(userId, replyItem.OwnerId);
            Assert.Equal(message, replyItem.Content);
            Assert.NotNull(replyItem.Owner);
            Assert.Equal(userId, replyItem.Owner.Id);

            // Ensure it's saved in the DB
            Item? savedItem = await dbContext.Items.FirstOrDefaultAsync(i => i.Id == replyItem.Id);
            Assert.NotNull(savedItem);
            Assert.Equal(request.Id, savedItem.TaggingRequestEntityId);
            Assert.Equal(message, savedItem.Content);
        }
    }

    [Fact]
    public async Task AddTagToItemAsync_ShouldIncreaseCachedWeightAndAddLedger()
    {
        var (dbContext, service, tid) = CreateScope();
        await using (dbContext)
        {
            var userId = $"user_{tid}";
            await dbContext.SeedUsersAsync(userId);

            var item = new Item { Content = $"TestItem_{tid}", OwnerId = userId };
            dbContext.Items.Add(item);
            var tag = new Tag { Name = $"TestTag_{tid}", OwnerId = userId, CachedWeight = 5 };
            dbContext.Tags.Add(tag);
            await dbContext.SaveChangesAsync();

            var result = await service.AddTagToItemAsync(item.Id, tag.Id, userId);

            Assert.Null(result);
            Tag? updatedTag = await dbContext.Tags.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tag.Id);
            Assert.Equal(6, updatedTag!.CachedWeight);

            TagWeightLedger? ledger =
                await dbContext.TagWeightLedgers!.SingleOrDefaultAsync(l => l.TagId == tag.Id && l.SourceType == "TagRelationInsert");
            Assert.NotNull(ledger);
            Assert.Equal(tag.Id, ledger.TagId);
            Assert.Equal(5, ledger.PreviousWeight);
            Assert.Equal(6, ledger.NewWeight);
            Assert.Equal(1, ledger.Delta);
        }
    }

    [Fact]
    public async Task AddTagToItemAsync_ShouldSucceed_WhenTagIsOwnedBySystem()
    {
        var (dbContext, service, tid) = CreateScope();
        await using (dbContext)
        {
            var systemUserId = $"sys_{tid}";
            var normalUserId = $"user_{tid}";
            await dbContext.SeedUsersAsync(systemUserId, normalUserId);

            var item = new Item { Content = $"UserItem_{tid}", OwnerId = normalUserId };
            dbContext.Items.Add(item);
            var systemTag = new Tag { Name = $"SystemTag_{tid}", OwnerId = systemUserId, CachedWeight = 10, IsSystem = true };
            dbContext.Tags.Add(systemTag);
            await dbContext.SaveChangesAsync();

            var result = await service.AddTagToItemAsync(item.Id, systemTag.Id, normalUserId);

            Assert.Null(result);
            Tag? updatedTag = await dbContext.Tags.AsNoTracking().FirstOrDefaultAsync(t => t.Id == systemTag.Id);
            Assert.Equal(11, updatedTag!.CachedWeight);

            TagRelation? relation = await dbContext.TagRelations.FirstOrDefaultAsync(tr => tr.ItemId == item.Id && tr.TagId == systemTag.Id);
            Assert.NotNull(relation);
            Assert.Equal(normalUserId, relation.OwnerId);
        }
    }

    [Fact]
    public async Task RemoveTagRelationAsync_ShouldDecreaseCachedWeightAndAddLedger()
    {
        var (dbContext, service, tid) = CreateScope();
        await using (dbContext)
        {
            var userId = $"user_{tid}";
            await dbContext.SeedUsersAsync(userId);

            var item = new Item { Content = $"TestItem_{tid}", OwnerId = userId };
            var tag = new Tag { Name = $"TestTag_{tid}", OwnerId = userId, CachedWeight = 5 };
            dbContext.Items.Add(item);
            dbContext.Tags.Add(tag);
            await dbContext.SaveChangesAsync();

            var relation = new TagRelation { ItemId = item.Id, TagId = tag.Id, OwnerId = userId, Weight = 2 };
            dbContext.TagRelations.Add(relation);
            await dbContext.SaveChangesAsync();

            var result = await service.RemoveTagRelationAsync(relation.Id, userId);

            Assert.Null(result);
            Tag? updatedTag = await dbContext.Tags.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tag.Id);
            Assert.Equal(3, updatedTag!.CachedWeight);

            TagWeightLedger? ledger =
                await dbContext.TagWeightLedgers!.SingleOrDefaultAsync(l => l.TagId == tag.Id && l.SourceType == "TagRelationDelete");
            Assert.NotNull(ledger);
            Assert.Equal(tag.Id, ledger.TagId);
            Assert.Equal(5, ledger.PreviousWeight);
            Assert.Equal(3, ledger.NewWeight);
            Assert.Equal(-2, ledger.Delta);
        }
    }

    [Fact]
    public async Task AddTagToTagAsync_ShouldIncreaseCachedWeightAndAddLedger()
    {
        var (dbContext, service, tid) = CreateScope();
        await using (dbContext)
        {
            var userId = $"user_{tid}";
            await dbContext.SeedUsersAsync(userId);

            var targetTag = new Tag { Name = $"TargetTag_{tid}", OwnerId = userId };
            var childTag = new Tag { Name = $"ChildTag_{tid}", OwnerId = userId, CachedWeight = 10 };
            dbContext.Tags.AddRange(targetTag, childTag);
            await dbContext.SaveChangesAsync();

            var result = await service.AddTagToTagAsync(targetTag.Id, childTag.Id, userId);

            Assert.Null(result);
            Tag? updatedTag = await dbContext.Tags.AsNoTracking().FirstOrDefaultAsync(t => t.Id == childTag.Id);
            Assert.Equal(11, updatedTag!.CachedWeight);

            TagWeightLedger? ledger =
                await dbContext.TagWeightLedgers!.SingleOrDefaultAsync(l => l.TagId == childTag.Id && l.SourceType == "TagRelationToTagInsert");
            Assert.NotNull(ledger);
            Assert.Equal(childTag.Id, ledger.TagId);
            Assert.Equal(10, ledger.PreviousWeight);
            Assert.Equal(11, ledger.NewWeight);
            Assert.Equal(1, ledger.Delta);
        }
    }

    [Fact]
    public async Task RemoveTagToTagRelationAsync_ShouldDecreaseCachedWeightAndAddLedger()
    {
        var (dbContext, service, tid) = CreateScope();
        await using (dbContext)
        {
            var userId = $"user_{tid}";
            await dbContext.SeedUsersAsync(userId);

            var targetTag = new Tag { Name = $"TargetTag_{tid}", OwnerId = userId };
            var childTag = new Tag { Name = $"ChildTag_{tid}", OwnerId = userId, CachedWeight = 10 };
            dbContext.Tags.AddRange(targetTag, childTag);
            await dbContext.SaveChangesAsync();

            var relation = new TagRelationToTag
            {
                TargetTagId = targetTag.Id,
                TagId = childTag.Id,
                OwnerId = userId,
                Weight = 3
            };
            dbContext.TagRelationToTags.Add(relation);
            await dbContext.SaveChangesAsync();

            var result = await service.RemoveTagToTagRelationAsync(relation.Id, userId);

            Assert.Null(result);
            Tag? updatedTag = await dbContext.Tags.AsNoTracking().FirstOrDefaultAsync(t => t.Id == childTag.Id);
            Assert.Equal(7, updatedTag!.CachedWeight);

            TagWeightLedger? ledger =
                await dbContext.TagWeightLedgers!.SingleOrDefaultAsync(l => l.TagId == childTag.Id && l.SourceType == "TagRelationToTagDelete");
            Assert.NotNull(ledger);
            Assert.Equal(childTag.Id, ledger.TagId);
            Assert.Equal(10, ledger.PreviousWeight);
            Assert.Equal(7, ledger.NewWeight);
            Assert.Equal(-3, ledger.Delta);
        }
    }

    [Fact]
    public async Task AddTagToItemAsync_ShouldReturnErrorIfTagNotOwnedByUser()
    {
        var (dbContext, service, tid) = CreateScope();
        await using (dbContext)
        {
            var ownerId = $"owner_{tid}";
            var otherUserId = $"other_{tid}";
            await dbContext.SeedUsersAsync(ownerId, otherUserId);

            var item = new Item { Content = $"TestItem_{tid}", OwnerId = ownerId };
            dbContext.Items.Add(item);
            var tag = new Tag { Name = $"TestTag_{tid}", OwnerId = ownerId };
            dbContext.Tags.Add(tag);
            await dbContext.SaveChangesAsync();

            var result = await service.AddTagToItemAsync(item.Id, tag.Id, otherUserId);

            Assert.Equal("タグの作成者ではないため、追加する権限がありません。", result);
        }
    }

    [Fact]
    public async Task AddTagToItemAsync_WithExistingItem_DoesNotDuplicateItemEntity()
    {
        var (dbContext, service, tid) = CreateScope();
        await using (dbContext)
        {
            var userId = $"user_{tid}";
            await dbContext.SeedUsersAsync(userId);

            var item = new Item { Content = $"Existing Item_{tid}", OwnerId = userId };
            dbContext.Items.Add(item);
            await dbContext.SaveChangesAsync();
            var savedItemId = item.Id;

            var tag = new Tag { Name = $"TestTag_{tid}", Content = "Test content", OwnerId = userId, CachedWeight = 0 };
            dbContext.Tags.Add(tag);
            await dbContext.SaveChangesAsync();

            var result = await service.AddTagToItemAsync(savedItemId, tag.Id, userId);

            Assert.Null(result);

            Assert.Equal(1, await dbContext.Items.CountAsync(i => i.Id == savedItemId));

            TagRelation? relation =
                await dbContext.TagRelations.SingleOrDefaultAsync(tr => tr.ItemId == savedItemId && tr.TagId == tag.Id);
            Assert.NotNull(relation);
            Assert.Equal(userId, relation!.OwnerId);
        }
    }
}