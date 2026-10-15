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

    [Fact]
    public async Task AddItemReplyAsync_ShouldSavePrivateModeAndGroup_WhenSpecified()
    {
        var (dbContext, service, tid) = CreateScope();
        await using (dbContext)
        {
            var userId = $"user_{tid}";
            await dbContext.SeedUsersAsync(userId);

            var group = new UserGroup { Name = $"Group_{tid}", OwnerId = userId };
            dbContext.UserGroups.Add(group);
            await dbContext.SaveChangesAsync();

            var parentItem = new Item
            {
                Content = $"Parent Item_{tid}",
                OwnerId = userId,
                IsPrivate = true,
                TargetUserGroupId = group.Id
            };
            dbContext.Items.Add(parentItem);
            await dbContext.SaveChangesAsync();

            // Act: 同一の user group を指定してプライベートモードでリプライ
            Item? reply = await service.AddItemReplyAsync(
                parentItem.Id,
                $"Private Reply_{tid}",
                userId,
                isPrivate: true,
                targetUserGroupId: group.Id);

            // Assert
            Assert.NotNull(reply);
            Assert.True(reply.IsPrivate);
            Assert.Equal(group.Id, reply.TargetUserGroupId);

            var saved = await dbContext.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == reply.Id);
            Assert.NotNull(saved);
            Assert.True(saved.IsPrivate);
            Assert.Equal(group.Id, saved.TargetUserGroupId);

            // Act 2: オフにして公開でリプライ
            Item? publicReply = await service.AddItemReplyAsync(
                parentItem.Id,
                $"Public Reply_{tid}",
                userId,
                isPrivate: false,
                targetUserGroupId: null);

            // Assert 2
            Assert.NotNull(publicReply);
            Assert.False(publicReply.IsPrivate);
            Assert.Null(publicReply.TargetUserGroupId);

            var savedPublic = await dbContext.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == publicReply.Id);
            Assert.NotNull(savedPublic);
            Assert.False(savedPublic.IsPrivate);
            Assert.Null(savedPublic.TargetUserGroupId);

            // Act 3: GetItemRepliesAsync で TargetUserGroup が Include されること
            IReadOnlyList<Item> replies = await service.GetItemRepliesAsync(parentItem.Id);
            var privateReplyInList = replies.FirstOrDefault(r => r.Id == reply.Id);
            Assert.NotNull(privateReplyInList);
            Assert.NotNull(privateReplyInList.TargetUserGroup);
            Assert.Equal($"Group_{tid}", privateReplyInList.TargetUserGroup.Name);
        }
    }

    [Fact]
    public async Task GetItemRepliesAsync_ShouldIncludeTagHistoryComments_WithTagActionMetadata()
    {
        var (dbContext, service, tid) = CreateScope();
        await using (dbContext)
        {
            var userOwner = $"user_owner_{tid}";
            var userTagger = $"user_tagger_{tid}";
            await dbContext.SeedUsersAsync(userOwner, userTagger);

            var parentItem = new Item { Content = $"Parent Item_{tid}", OwnerId = userOwner };
            var tag = new Tag { Name = $"Csharp_{tid}", OwnerId = userOwner };
            dbContext.Items.Add(parentItem);
            dbContext.Tags.Add(tag);
            await dbContext.SaveChangesAsync();

            // 通常のリプライ
            var normalReply = new Item
            {
                Content = $"Normal reply_{tid}",
                OwnerId = userOwner,
                ParentItemId = parentItem.Id,
                CreatedDate = DateTime.UtcNow.AddMinutes(1)
            };
            dbContext.Items.Add(normalReply);

            // タグ履歴コメントアイテム
            var commentItem = new Item
            {
                Content = $"Tag history comment_{tid}",
                OwnerId = userTagger,
                CreatedDate = DateTime.UtcNow.AddMinutes(2)
            };
            dbContext.Items.Add(commentItem);
            await dbContext.SaveChangesAsync();

            var relation = new TagRelation
            {
                ItemId = parentItem.Id,
                TagId = tag.Id,
                OwnerId = userTagger,
                Weight = 3,
                CommentItemId = commentItem.Id
            };
            dbContext.TagRelations.Add(relation);
            await dbContext.SaveChangesAsync();

            var ledger = new TagWeightLedger
            {
                TagId = tag.Id,
                ItemId = parentItem.Id,
                SourceType = "TagRelationInsert",
                SourceId = relation.Id,
                TagRelation = relation,
                CommentItemId = commentItem.Id,
                Delta = 3,
                OwnerId = userTagger,
                CreatedDate = DateTime.UtcNow.AddMinutes(2)
            };
            dbContext.TagWeightLedgers.Add(ledger);
            await dbContext.SaveChangesAsync();

            // Act 1: GetItemRepliesAsync
            IReadOnlyList<Item> replies = await service.GetItemRepliesAsync(parentItem.Id);

            // Assert 1: 通常リプライとタグ履歴コメントの両方が返る
            Assert.Equal(2, replies.Count);
            var retrievedNormal = replies.FirstOrDefault(r => r.Id == normalReply.Id);
            Assert.NotNull(retrievedNormal);
            Assert.Null(retrievedNormal.TagActionInfo);

            var retrievedTagComment = replies.FirstOrDefault(r => r.Id == commentItem.Id);
            Assert.NotNull(retrievedTagComment);
            Assert.NotNull(retrievedTagComment.TagActionInfo);
            Assert.Equal($"Csharp_{tid}", retrievedTagComment.TagActionInfo.TagName);
            Assert.Equal(userOwner, retrievedTagComment.TagActionInfo.TagOwnerUserName);
            Assert.Equal(3, retrievedTagComment.TagActionInfo.Weight);

            // Act 2: GetItemReplyCountAsync
            int count = await service.GetItemReplyCountAsync(parentItem.Id);
            Assert.Equal(2, count);
        }
    }
}