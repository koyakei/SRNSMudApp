using Microsoft.EntityFrameworkCore;

using Moq;

using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;

namespace SRNSMudApp.Tests;

public class TaggingServiceTests : IAsyncLifetime
{
    private MsSqlTestDatabase _sharedDb = null!;

    public async Task InitializeAsync()
    {
        _sharedDb = await SharedMsSqlTestDatabase.GetInstanceAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private (ApplicationDbContext dbContext, TaggingService service, string tid) CreateScope()
    {
        var tid = Guid.NewGuid().ToString("N")[..8];
        var ctx = new ApplicationDbContext(_sharedDb.Options);
        var mockDbFactory = new Mock<IDbContextFactory<ApplicationDbContext>>();
        mockDbFactory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new ApplicationDbContext(_sharedDb.Options));
        var svc = new TaggingService(mockDbFactory.Object);
        return (ctx, svc, tid);
    }

    [Fact]
    public async Task RejectRequestAsync_WhenRequestNotFound_ThrowsInvalidOperationException()
    {
        // Arrange
        var (dbContext, service, _) = CreateScope();
        await using (dbContext)
        {
            // Act & Assert
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.RejectRequestAsync(999999, "any_user", "reason"));
            Assert.Equal("リクエストが見つかりません。", ex.Message);
        }
    }

    [Theory]
    [InlineData("Executed")]
    [InlineData("Canceled")]
    [InlineData("Rejected")]
    public async Task RejectRequestAsync_WhenStatusIsNotProposed_ThrowsInvalidOperationException(string initialStatus)
    {
        // Arrange
        var (dbContext, service, tid) = CreateScope();
        await using (dbContext)
        {
            var requesterId = $"req_{tid}";
            var tagOwnerId = $"owner_{tid}";
            await dbContext.SeedUsersAsync(requesterId, tagOwnerId);

            var item = new Item { Content = $"Item_{tid}", OwnerId = requesterId };
            var tag = new Tag { Name = $"Tag_{tid}", OwnerId = tagOwnerId };
            dbContext.Items.Add(item);
            dbContext.Tags.Add(tag);
            await dbContext.SaveChangesAsync();

            var request = new TaggingRequestEntity
            {
                ContractType = ContractTypes.Gratis,
                RequesterUserId = requesterId,
                TagOwnerUserId = tagOwnerId,
                OwnerId = requesterId,
                TargetItemId = item.Id,
                TargetId = item.TagTargetId,
                RequestedTagId = tag.Id,
                Payload = new GratisPayload("test")
            };

            switch (initialStatus)
            {
                case "Executed":
                    request.Execute();
                    break;
                case "Canceled":
                    request.Cancel();
                    break;
                case "Rejected":
                    request.Reject(new RejectionReason("initial reject"));
                    break;
            }

            dbContext.TaggingRequestEntities.Add(request);
            await dbContext.SaveChangesAsync();

            // Act & Assert
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.RejectRequestAsync(request.Id, tagOwnerId, "reject"));
            Assert.Equal("このリクエストは既に処理されています。", ex.Message);
        }
    }

    [Fact]
    public async Task RejectRequestAsync_ByTagOwner_ShouldChangeStatusToRejectedAndPersistReason()
    {
        // Arrange
        var (dbContext, service, tid) = CreateScope();
        await using (dbContext)
        {
            var requesterId = $"req_{tid}";
            var tagOwnerId = $"owner_{tid}";
            await dbContext.SeedUsersAsync(requesterId, tagOwnerId);

            var item = new Item { Content = $"Item_{tid}", OwnerId = requesterId };
            var tag = new Tag { Name = $"Tag_{tid}", OwnerId = tagOwnerId };
            dbContext.Items.Add(item);
            dbContext.Tags.Add(tag);
            await dbContext.SaveChangesAsync();

            var request = new TaggingRequestEntity
            {
                ContractType = ContractTypes.Gratis,
                RequesterUserId = requesterId,
                TagOwnerUserId = tagOwnerId,
                OwnerId = requesterId,
                TargetItemId = item.Id,
                TargetId = item.TagTargetId,
                RequestedTagId = tag.Id,
                Payload = new GratisPayload("test")
            };
            dbContext.TaggingRequestEntities.Add(request);
            await dbContext.SaveChangesAsync();

            var comment = "Not suitable for this item.";

            // Act
            await service.RejectRequestAsync(request.Id, tagOwnerId, comment);

            // Assert
            dbContext.ChangeTracker.Clear();
            TaggingRequestEntity? updated = await dbContext.TaggingRequestEntities.FindAsync(request.Id);
            Assert.NotNull(updated);
            Assert.Equal(TradeStatus.Rejected, updated!.Status);
            Assert.True(updated.Rejection is RejectionReason r && r.Reason == comment);
            Assert.Contains(comment, updated.RejectionInfoJson);
        }
    }

    [Fact]
    public async Task RejectRequestAsync_ByRequester_ShouldChangeStatusToRejectedAndPersistReason()
    {
        // Arrange
        var (dbContext, service, tid) = CreateScope();
        await using (dbContext)
        {
            var requesterId = $"req_{tid}";
            var tagOwnerId = $"owner_{tid}";
            await dbContext.SeedUsersAsync(requesterId, tagOwnerId);

            var item = new Item { Content = $"Item_{tid}", OwnerId = requesterId };
            var tag = new Tag { Name = $"Tag_{tid}", OwnerId = tagOwnerId };
            dbContext.Items.Add(item);
            dbContext.Tags.Add(tag);
            await dbContext.SaveChangesAsync();

            var request = new TaggingRequestEntity
            {
                ContractType = ContractTypes.Gratis,
                RequesterUserId = requesterId,
                TagOwnerUserId = tagOwnerId,
                OwnerId = requesterId,
                TargetItemId = item.Id,
                TargetId = item.TagTargetId,
                RequestedTagId = tag.Id,
                Payload = new GratisPayload("test")
            };
            dbContext.TaggingRequestEntities.Add(request);
            await dbContext.SaveChangesAsync();

            var comment = "Withdrawing my request.";

            // Act: 依頼者自身による却下
            await service.RejectRequestAsync(request.Id, requesterId, comment);

            // Assert
            dbContext.ChangeTracker.Clear();
            TaggingRequestEntity? updated = await dbContext.TaggingRequestEntities.FindAsync(request.Id);
            Assert.NotNull(updated);
            Assert.Equal(TradeStatus.Rejected, updated!.Status);
            Assert.True(updated.Rejection is RejectionReason r && r.Reason == comment);
        }
    }

    [Fact]
    public async Task RejectRequestAsync_WhenTriggerContract_ByThirdParty_ShouldSucceed()
    {
        // Arrange: Trigger 契約は第三者でも却下可能
        var (dbContext, service, tid) = CreateScope();
        await using (dbContext)
        {
            var requesterId = $"req_{tid}";
            var tagOwnerId = $"owner_{tid}";
            var thirdPartyId = $"third_{tid}";
            await dbContext.SeedUsersAsync(requesterId, tagOwnerId, thirdPartyId);

            var item = new Item { Content = $"Item_{tid}", OwnerId = requesterId };
            var tag = new Tag { Name = $"Tag_{tid}", OwnerId = tagOwnerId };
            dbContext.Items.Add(item);
            dbContext.Tags.Add(tag);
            await dbContext.SaveChangesAsync();

            var request = new TaggingRequestEntity
            {
                ContractType = ContractTypes.Trigger,
                RequesterUserId = requesterId,
                TagOwnerUserId = tagOwnerId,
                OwnerId = requesterId,
                TargetItemId = item.Id,
                TargetId = item.TagTargetId,
                RequestedTagId = tag.Id,
                Payload = new PublicOfferPayload(1)
            };
            dbContext.TaggingRequestEntities.Add(request);
            await dbContext.SaveChangesAsync();

            // Act
            await service.RejectRequestAsync(request.Id, thirdPartyId, "Trigger reject by third party");

            // Assert
            dbContext.ChangeTracker.Clear();
            TaggingRequestEntity? updated = await dbContext.TaggingRequestEntities.FindAsync(request.Id);
            Assert.NotNull(updated);
            Assert.Equal(TradeStatus.Rejected, updated!.Status);
        }
    }

    [Fact]
    public async Task RejectRequestAsync_WhenBountyContract_ByThirdParty_ShouldSucceed()
    {
        // Arrange: Bounty 契約は第三者でも却下可能
        var (dbContext, service, tid) = CreateScope();
        await using (dbContext)
        {
            var requesterId = $"req_{tid}";
            var tagOwnerId = $"owner_{tid}";
            var thirdPartyId = $"third_{tid}";
            await dbContext.SeedUsersAsync(requesterId, tagOwnerId, thirdPartyId);

            var item = new Item { Content = $"Item_{tid}", OwnerId = requesterId };
            var tag = new Tag { Name = $"Tag_{tid}", OwnerId = tagOwnerId };
            dbContext.Items.Add(item);
            dbContext.Tags.Add(tag);
            await dbContext.SaveChangesAsync();

            var request = new TaggingRequestEntity
            {
                ContractType = ContractTypes.Bounty,
                RequesterUserId = requesterId,
                TagOwnerUserId = tagOwnerId,
                OwnerId = requesterId,
                TargetItemId = item.Id,
                TargetId = item.TagTargetId,
                RequestedTagId = tag.Id,
                Payload = new BountyPayload(1)
            };
            dbContext.TaggingRequestEntities.Add(request);
            await dbContext.SaveChangesAsync();

            // Act
            await service.RejectRequestAsync(request.Id, thirdPartyId, "Bounty reject by third party");

            // Assert
            dbContext.ChangeTracker.Clear();
            TaggingRequestEntity? updated = await dbContext.TaggingRequestEntities.FindAsync(request.Id);
            Assert.NotNull(updated);
            Assert.Equal(TradeStatus.Rejected, updated!.Status);
        }
    }

    [Fact]
    public async Task RejectRequestAsync_WhenGratisContract_ByThirdParty_ThrowsUnauthorizedAccessException()
    {
        // Arrange: Gratis 契約は第三者による却下は認可エラー
        var (dbContext, service, tid) = CreateScope();
        await using (dbContext)
        {
            var requesterId = $"req_{tid}";
            var tagOwnerId = $"owner_{tid}";
            var thirdPartyId = $"third_{tid}";
            await dbContext.SeedUsersAsync(requesterId, tagOwnerId, thirdPartyId);

            var item = new Item { Content = $"Item_{tid}", OwnerId = requesterId };
            var tag = new Tag { Name = $"Tag_{tid}", OwnerId = tagOwnerId };
            dbContext.Items.Add(item);
            dbContext.Tags.Add(tag);
            await dbContext.SaveChangesAsync();

            var request = new TaggingRequestEntity
            {
                ContractType = ContractTypes.Gratis,
                RequesterUserId = requesterId,
                TagOwnerUserId = tagOwnerId,
                OwnerId = requesterId,
                TargetItemId = item.Id,
                TargetId = item.TagTargetId,
                RequestedTagId = tag.Id,
                Payload = new GratisPayload("test")
            };
            dbContext.TaggingRequestEntities.Add(request);
            await dbContext.SaveChangesAsync();

            // Act & Assert
            var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                service.RejectRequestAsync(request.Id, thirdPartyId, "Unauthorized rejection"));
            Assert.Equal("このリクエストを却下する権限がありません。", ex.Message);
        }
    }

    [Fact]
    public async Task RejectRequestAsync_ConcurrentRejection_OnlyOneSucceeds()
    {
        // Arrange
        var (dbContext, service, tid) = CreateScope();
        await using (dbContext)
        {
            var requesterId = $"req_{tid}";
            var tagOwnerId = $"owner_{tid}";
            await dbContext.SeedUsersAsync(requesterId, tagOwnerId);

            var item = new Item { Content = $"Item_{tid}", OwnerId = requesterId };
            var tag = new Tag { Name = $"Tag_{tid}", OwnerId = tagOwnerId };
            dbContext.Items.Add(item);
            dbContext.Tags.Add(tag);
            await dbContext.SaveChangesAsync();

            var request = new TaggingRequestEntity
            {
                ContractType = ContractTypes.Gratis,
                RequesterUserId = requesterId,
                TagOwnerUserId = tagOwnerId,
                OwnerId = requesterId,
                TargetItemId = item.Id,
                TargetId = item.TagTargetId,
                RequestedTagId = tag.Id,
                Payload = new GratisPayload("test")
            };
            dbContext.TaggingRequestEntities.Add(request);
            await dbContext.SaveChangesAsync();

            // Act: 2つの却下タスクを並行実行
            var task1 = service.RejectRequestAsync(request.Id, tagOwnerId, "reject 1");
            var task2 = service.RejectRequestAsync(request.Id, tagOwnerId, "reject 2");

            // 1つは成功し、もう1つは InvalidOperationException をスローすること
            Task[] tasks = [task1, task2];
            var exceptions = new List<Exception>();
            foreach (var task in tasks)
            {
                try
                {
                    await task;
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }

            Assert.Single(exceptions);
            Assert.IsType<InvalidOperationException>(exceptions[0]);
            Assert.Contains("既に処理されてい", exceptions[0].Message);

            dbContext.ChangeTracker.Clear();
            TaggingRequestEntity? updated = await dbContext.TaggingRequestEntities.FindAsync(request.Id);
            Assert.Equal(TradeStatus.Rejected, updated!.Status);
        }
    }
}