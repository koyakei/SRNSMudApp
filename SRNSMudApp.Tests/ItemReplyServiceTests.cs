using Microsoft.EntityFrameworkCore;
using Moq;
using SRNSMudApp.Data;
using SRNSMudApp.Services;

namespace SRNSMudApp.Tests;

public class ItemReplyServiceTests : IAsyncLifetime
{
    private MsSqlTestDatabase _sharedDb = null!;

    public async Task InitializeAsync()
    {
        _sharedDb = await SharedMsSqlTestDatabase.GetInstanceAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private (ApplicationDbContext dbContext, ItemReplyService service, string tid) CreateScope()
    {
        var tid = Guid.NewGuid().ToString("N")[..8];
        var dbContext = new ApplicationDbContext(_sharedDb.Options);
        var mockDbFactory = new Mock<IDbContextFactory<ApplicationDbContext>>();
        mockDbFactory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new ApplicationDbContext(_sharedDb.Options));

        var service = new ItemReplyService(mockDbFactory.Object);
        return (dbContext, service, tid);
    }

    [Fact]
    public async Task AddReplyToRequestAsync_ShouldCreateItemAndReturnItWithRelations()
    {
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

            Item? replyItem = await service.AddReplyToRequestAsync(request.Id, userId, message);

            Assert.NotNull(replyItem);
            Assert.Equal(request.Id, replyItem.TaggingRequestEntityId);
            Assert.Equal(userId, replyItem.OwnerId);
            Assert.Equal(message, replyItem.Content);
            Assert.NotNull(replyItem.Owner);
            Assert.Equal(userId, replyItem.Owner.Id);

            Item? savedItem = await dbContext.Items.FirstOrDefaultAsync(i => i.Id == replyItem.Id);
            Assert.NotNull(savedItem);
            Assert.Equal(request.Id, savedItem.TaggingRequestEntityId);
            Assert.Equal(message, savedItem.Content);
        }
    }

    [Fact]
    public async Task AddItemReplyAsync_ShouldCopyParentTagsAsDefault()
    {
        var (dbContext, service, tid) = CreateScope();
        await using (dbContext)
        {
            var userId = $"user_{tid}";
            await dbContext.SeedUsersAsync(userId);

            var parentItem = new Item { Content = $"ParentItem_{tid}", OwnerId = userId };
            var tag1 = new Tag { Name = $"ParentTagA_{tid}", OwnerId = userId };
            var tag2 = new Tag { Name = $"ParentTagB_{tid}", OwnerId = userId };
            dbContext.Items.Add(parentItem);
            dbContext.Tags.AddRange(tag1, tag2);
            await dbContext.SaveChangesAsync();

            dbContext.TagRelations.AddRange(
                new TagRelation { ItemId = parentItem.Id, TagId = tag1.Id, Weight = 1, OwnerId = userId },
                new TagRelation { ItemId = parentItem.Id, TagId = tag2.Id, Weight = 1, OwnerId = userId });
            await dbContext.SaveChangesAsync();

            Item? reply = await service.AddItemReplyAsync(parentItem.Id, $"This is a reply_{tid}", userId);

            Assert.NotNull(reply);
            List<TagRelation> replyRelations = await dbContext.TagRelations
                .Where(tr => tr.ItemId == reply!.Id)
                .OrderBy(tr => tr.TagId)
                .ToListAsync();

            Assert.Equal([tag1.Id, tag2.Id], replyRelations.Select(tr => tr.TagId));
            Assert.All(replyRelations, tr => Assert.Equal(userId, tr.OwnerId));
        }
    }

    [Fact]
    public async Task AddItemReplyAsync_ShouldSaveExplicitNotificationRecipients_WhenTargetUserIdsProvided()
    {
        var (dbContext, service, tid) = CreateScope();
        await using (dbContext)
        {
            var userA = $"userA_{tid}";
            var userB = $"userB_{tid}";
            var userC = $"userC_{tid}";
            await dbContext.SeedUsersAsync(userA, userB, userC);

            var parentItem = new Item { Content = $"ParentItem_{tid}", OwnerId = userA };
            dbContext.Items.Add(parentItem);
            await dbContext.SaveChangesAsync();

            Item? reply = await service.AddItemReplyAsync(parentItem.Id, $"Reply_{tid}", userB, [userA]);

            Assert.NotNull(reply);
            List<ItemReplyNotificationRecipient> recipients = await dbContext.ItemReplyNotificationRecipients
                .Where(r => r.ReplyItemId == reply!.Id)
                .ToListAsync();

            Assert.Single(recipients);
            Assert.Equal(userA, recipients[0].RecipientUserId);
        }
    }

    [Fact]
    public async Task AddItemReplyAsync_ShouldSaveDefaultRecipients_WhenTargetUserIdsNull()
    {
        var (dbContext, service, tid) = CreateScope();
        await using (dbContext)
        {
            var userA = $"userA_{tid}";
            var userB = $"userB_{tid}";
            var userC = $"userC_{tid}";
            await dbContext.SeedUsersAsync(userA, userB, userC);

            var parentItem = new Item { Content = $"ParentItem_{tid}", OwnerId = userA };
            dbContext.Items.Add(parentItem);
            await dbContext.SaveChangesAsync();

            Item? reply1 = await service.AddItemReplyAsync(parentItem.Id, $"Reply1_{tid}", userB, [userA]);
            Assert.NotNull(reply1);

            Item? reply2 = await service.AddItemReplyAsync(parentItem.Id, $"Reply2_{tid}", userC);
            Assert.NotNull(reply2);

            List<string> recipientUserIds = await dbContext.ItemReplyNotificationRecipients
                .Where(r => r.ReplyItemId == reply2!.Id)
                .Select(r => r.RecipientUserId)
                .OrderBy(id => id)
                .ToListAsync();

            List<string> expected = [userA, userB];
            expected.Sort();
            Assert.Equal(expected, recipientUserIds);
        }
    }

    [Fact]
    public async Task GetItemReplyCountAsync_ShouldReturnCorrectCount()
    {
        var (dbContext, service, tid) = CreateScope();
        await using (dbContext)
        {
            var userId = $"user_{tid}";
            await dbContext.SeedUsersAsync(userId);

            var parentItem = new Item { Content = $"Parent Item_{tid}", OwnerId = userId };
            dbContext.Items.Add(parentItem);
            await dbContext.SaveChangesAsync();

            var countBefore = await service.GetItemReplyCountAsync(parentItem.Id);
            Assert.Equal(0, countBefore);

            var reply1 = new Item { Content = $"Reply 1_{tid}", OwnerId = userId, ParentItemId = parentItem.Id };
            var reply2 = new Item { Content = $"Reply 2_{tid}", OwnerId = userId, ParentItemId = parentItem.Id };
            dbContext.Items.AddRange(reply1, reply2);
            await dbContext.SaveChangesAsync();

            var countAfter = await service.GetItemReplyCountAsync(parentItem.Id);
            Assert.Equal(2, countAfter);

            var nonExistentCount = await service.GetItemReplyCountAsync(-1);
            Assert.Equal(0, nonExistentCount);
        }
    }
}

