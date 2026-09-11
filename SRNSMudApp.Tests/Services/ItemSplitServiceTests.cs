using Microsoft.EntityFrameworkCore;

using Moq;

using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;
using SRNSMudApp.Tests.TestSupport;

namespace SRNSMudApp.Tests.Services;

public class ItemSplitServiceTests : IAsyncLifetime
{
    private MsSqlTestDatabase _sharedDb = null!;

    public async Task InitializeAsync()
    {
        _sharedDb = await SharedMsSqlTestDatabase.GetInstanceAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private (ApplicationDbContext dbContext, ItemSplitService service, Mock<INotificationService> mockNotificationService, string tid) CreateScope()
    {
        var tid = Guid.NewGuid().ToString("N")[..8];
        var dbContext = new ApplicationDbContext(_sharedDb.Options);
        var mockDbFactory = new Mock<IDbContextFactory<ApplicationDbContext>>();
        mockDbFactory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new ApplicationDbContext(_sharedDb.Options));

        var mockNotificationService = new Mock<INotificationService>();
        var service = new ItemSplitService(mockDbFactory.Object, mockNotificationService.Object);
        return (dbContext, service, mockNotificationService, tid);
    }

    [Fact]
    public async Task RequestSplitAsync_Success_CreatesItemSplitRequest()
    {
        var (dbContext, service, mockNotification, tid) = CreateScope();
        await using (dbContext)
        {
            var ownerId = $"owner_{tid}";
            var requesterId = $"requester_{tid}";
            await dbContext.SeedUsersAsync(ownerId, requesterId);

            var originalItem = new Item
            {
                OwnerId = ownerId,
                Content = $"こんにちは、世界_{tid}！これはテスト本文です。"
            };
            dbContext.Items.Add(originalItem);
            await dbContext.SaveChangesAsync();

            // Act
            Result<ItemSplitRequest> result = await service.RequestSplitAsync(originalItem.Id, $"世界_{tid}！", requesterId);

            // Assert
            Assert.True(result is Success<ItemSplitRequest>);
            switch (result)
            {
                case Success<ItemSplitRequest> success:
                    Assert.Equal(originalItem.Id, success.Value.OriginalItemId);
                    Assert.Equal(requesterId, success.Value.RequesterUserId);
                    Assert.Equal(ownerId, success.Value.OwnerUserId);
                    Assert.Equal($"世界_{tid}！", success.Value.SelectedText);
                    Assert.Equal(TradeStatus.Proposed, success.Value.Status);
                    break;
            }

            mockNotification.Verify(n => n.NotifyNotificationsChanged(), Times.Once);
        }
    }

    [Fact]
    public async Task RequestSplitAsync_Fails_WhenTextNotInItem()
    {
        var (dbContext, service, _, tid) = CreateScope();
        await using (dbContext)
        {
            var ownerId = $"owner_{tid}";
            var requesterId = $"requester_{tid}";
            await dbContext.SeedUsersAsync(ownerId, requesterId);

            var originalItem = new Item
            {
                OwnerId = ownerId,
                Content = $"短文_{tid}"
            };
            dbContext.Items.Add(originalItem);
            await dbContext.SaveChangesAsync();

            // Act
            Result<ItemSplitRequest> result = await service.RequestSplitAsync(originalItem.Id, "存在しないテキスト", requesterId);

            // Assert
            Assert.True(result is Failure);
            switch (result)
            {
                case Failure failure:
                    Assert.Contains("本文に含まれていません", failure.ErrorMessage);
                    break;
            }
        }
    }

    [Fact]
    public async Task RequestSplitAsync_Fails_WhenRequestedByOwner()
    {
        var (dbContext, service, _, tid) = CreateScope();
        await using (dbContext)
        {
            var ownerId = $"owner_{tid}";
            await dbContext.SeedUsersAsync(ownerId);

            var originalItem = new Item
            {
                OwnerId = ownerId,
                Content = $"自分のアイテム_{tid}"
            };
            dbContext.Items.Add(originalItem);
            await dbContext.SaveChangesAsync();

            // Act
            Result<ItemSplitRequest> result = await service.RequestSplitAsync(originalItem.Id, "アイテム", ownerId);

            // Assert
            Assert.True(result is Failure);
            switch (result)
            {
                case Failure failure:
                    Assert.Contains("自分自身のアイテム", failure.ErrorMessage);
                    break;
            }
        }
    }

    [Fact]
    public async Task RequestSplitAsync_Fails_WhenDuplicateProposedRequestExists()
    {
        var (dbContext, service, _, tid) = CreateScope();
        await using (dbContext)
        {
            var ownerId = $"owner_{tid}";
            var requesterId = $"requester_{tid}";
            await dbContext.SeedUsersAsync(ownerId, requesterId);

            var originalItem = new Item
            {
                OwnerId = ownerId,
                Content = $"重複テスト_{tid}用の本文です"
            };
            dbContext.Items.Add(originalItem);

            var existingRequest = new ItemSplitRequest
            {
                OriginalItem = originalItem,
                RequesterUserId = requesterId,
                OwnerUserId = ownerId,
                SelectedText = $"重複テスト_{tid}",
                Status = TradeStatus.Proposed,
                OwnerId = requesterId
            };
            dbContext.ItemSplitRequests.Add(existingRequest);
            await dbContext.SaveChangesAsync();

            // Act
            Result<ItemSplitRequest> result = await service.RequestSplitAsync(originalItem.Id, $"重複テスト_{tid}", requesterId);

            // Assert
            Assert.True(result is Failure);
            switch (result)
            {
                case Failure failure:
                    Assert.Contains("既に申請中", failure.ErrorMessage);
                    break;
            }
        }
    }

    [Fact]
    public async Task ApproveSplitAsync_Success_CreatesNewItemAndReplacesContent()
    {
        var (dbContext, service, mockNotification, tid) = CreateScope();
        await using (dbContext)
        {
            var ownerId = $"owner_{tid}";
            var requesterId = $"requester_{tid}";
            await dbContext.SeedUsersAsync(ownerId, requesterId);

            var originalItem = new Item
            {
                OwnerId = ownerId,
                Content = $"開始 [切り抜く部分_{tid}] 終了",
                IsPrivate = true
            };
            dbContext.Items.Add(originalItem);

            var request = new ItemSplitRequest
            {
                OriginalItem = originalItem,
                RequesterUserId = requesterId,
                OwnerUserId = ownerId,
                SelectedText = $"[切り抜く部分_{tid}]",
                Status = TradeStatus.Proposed,
                OwnerId = requesterId
            };
            dbContext.ItemSplitRequests.Add(request);
            await dbContext.SaveChangesAsync();

            // Act
            Result<Item> result = await service.ApproveSplitAsync(request.Id, ownerId);

            // Assert
            Assert.True(result is Success<Item>);
            int createdId = 0;
            switch (result)
            {
                case Success<Item> success:
                    Assert.Equal($"[切り抜く部分_{tid}]", success.Value.Content);
                    Assert.Equal(ownerId, success.Value.OwnerId);
                    Assert.True(success.Value.IsPrivate);
                    createdId = success.Value.Id;
                    break;
            }

            // 元アイテムの本文確認
            dbContext.ChangeTracker.Clear();
            Item? updatedOriginal = await dbContext.Items.FindAsync(originalItem.Id);
            Assert.NotNull(updatedOriginal);
            Assert.Equal($"開始 /ItemDetail/{createdId} 終了", updatedOriginal.Content);

            // リクエストの状態確認
            ItemSplitRequest? updatedReq = await dbContext.ItemSplitRequests.FindAsync(request.Id);
            Assert.NotNull(updatedReq);
            Assert.Equal(TradeStatus.Executed, updatedReq.Status);
            Assert.Equal(createdId, updatedReq.CreatedItemId);

            mockNotification.Verify(n => n.NotifyNotificationsChanged(), Times.Once);
        }
    }

    [Fact]
    public async Task ApproveSplitAsync_Fails_WhenNonOwnerAttemptsApproval()
    {
        var (dbContext, service, _, tid) = CreateScope();
        await using (dbContext)
        {
            var ownerId = $"owner_{tid}";
            var requesterId = $"requester_{tid}";
            var hackerId = $"hacker_{tid}";
            await dbContext.SeedUsersAsync(ownerId, requesterId, hackerId);

            var originalItem = new Item
            {
                OwnerId = ownerId,
                Content = $"権限テスト_{tid}"
            };
            dbContext.Items.Add(originalItem);

            var request = new ItemSplitRequest
            {
                OriginalItem = originalItem,
                RequesterUserId = requesterId,
                OwnerUserId = ownerId,
                SelectedText = $"権限_{tid}",
                Status = TradeStatus.Proposed,
                OwnerId = requesterId
            };
            dbContext.ItemSplitRequests.Add(request);
            await dbContext.SaveChangesAsync();

            // Act
            Result<Item> result = await service.ApproveSplitAsync(request.Id, hackerId);

            // Assert
            Assert.True(result is Failure);
            switch (result)
            {
                case Failure failure:
                    Assert.Contains("権限がありません", failure.ErrorMessage);
                    break;
            }
        }
    }

    [Fact]
    public async Task ApproveSplitAsync_Fails_WhenOriginalContentNoLongerContainsSelectedText()
    {
        var (dbContext, service, _, tid) = CreateScope();
        await using (dbContext)
        {
            var ownerId = $"owner_{tid}";
            var requesterId = $"requester_{tid}";
            await dbContext.SeedUsersAsync(ownerId, requesterId);

            var originalItem = new Item
            {
                OwnerId = ownerId,
                Content = $"既に編集されて変更された本文_{tid}"
            };
            dbContext.Items.Add(originalItem);

            var request = new ItemSplitRequest
            {
                OriginalItem = originalItem,
                RequesterUserId = requesterId,
                OwnerUserId = ownerId,
                SelectedText = "昔のテキスト",
                Status = TradeStatus.Proposed,
                OwnerId = requesterId
            };
            dbContext.ItemSplitRequests.Add(request);
            await dbContext.SaveChangesAsync();

            // Act
            Result<Item> result = await service.ApproveSplitAsync(request.Id, ownerId);

            // Assert
            Assert.True(result is Failure);
            switch (result)
            {
                case Failure failure:
                    Assert.Contains("見つかりません", failure.ErrorMessage);
                    break;
            }
        }
    }

    [Fact]
    public async Task RejectSplitAsync_Success_UpdatesStatusAndSetsReason()
    {
        var (dbContext, service, mockNotification, tid) = CreateScope();
        await using (dbContext)
        {
            var ownerId = $"owner_{tid}";
            var requesterId = $"requester_{tid}";
            await dbContext.SeedUsersAsync(ownerId, requesterId);

            var originalItem = new Item
            {
                OwnerId = ownerId,
                Content = $"却下テスト_{tid}"
            };
            dbContext.Items.Add(originalItem);

            var request = new ItemSplitRequest
            {
                OriginalItem = originalItem,
                RequesterUserId = requesterId,
                OwnerUserId = ownerId,
                SelectedText = $"却下_{tid}",
                Status = TradeStatus.Proposed,
                OwnerId = requesterId
            };
            dbContext.ItemSplitRequests.Add(request);
            await dbContext.SaveChangesAsync();

            // Act
            Result<bool> result = await service.RejectSplitAsync(request.Id, ownerId, "文脈が壊れるため");

            // Assert
            Assert.True(result is Success<bool>);

            dbContext.ChangeTracker.Clear();
            ItemSplitRequest? updated = await dbContext.ItemSplitRequests.FindAsync(request.Id);
            Assert.NotNull(updated);
            Assert.Equal(TradeStatus.Rejected, updated.Status);
            Assert.Equal("文脈が壊れるため", updated.RejectReason);

            mockNotification.Verify(n => n.NotifyNotificationsChanged(), Times.Once);
        }
    }

    [Fact]
    public async Task CancelSplitAsync_Success_ByRequester()
    {
        var (dbContext, service, mockNotification, tid) = CreateScope();
        await using (dbContext)
        {
            var ownerId = $"owner_{tid}";
            var requesterId = $"requester_{tid}";
            await dbContext.SeedUsersAsync(ownerId, requesterId);

            var originalItem = new Item
            {
                OwnerId = ownerId,
                Content = $"取り下げテスト_{tid}"
            };
            dbContext.Items.Add(originalItem);

            var request = new ItemSplitRequest
            {
                OriginalItem = originalItem,
                RequesterUserId = requesterId,
                OwnerUserId = ownerId,
                SelectedText = $"取り下げ_{tid}",
                Status = TradeStatus.Proposed,
                OwnerId = requesterId
            };
            dbContext.ItemSplitRequests.Add(request);
            await dbContext.SaveChangesAsync();

            // Act
            Result<bool> result = await service.CancelSplitAsync(request.Id, requesterId);

            // Assert
            Assert.True(result is Success<bool>);

            dbContext.ChangeTracker.Clear();
            ItemSplitRequest? updated = await dbContext.ItemSplitRequests.FindAsync(request.Id);
            Assert.NotNull(updated);
            Assert.Equal(TradeStatus.Canceled, updated.Status);

            mockNotification.Verify(n => n.NotifyNotificationsChanged(), Times.Once);
        }
    }

    [Fact]
    public async Task CancelSplitAsync_Fails_WhenDifferentUserCancels()
    {
        var (dbContext, service, _, tid) = CreateScope();
        await using (dbContext)
        {
            var ownerId = $"owner_{tid}";
            var requesterId = $"requester_{tid}";
            var otherUserId = $"other_{tid}";
            await dbContext.SeedUsersAsync(ownerId, requesterId, otherUserId);

            var originalItem = new Item
            {
                OwnerId = ownerId,
                Content = $"他人のリクエスト_{tid}"
            };
            dbContext.Items.Add(originalItem);

            var request = new ItemSplitRequest
            {
                OriginalItem = originalItem,
                RequesterUserId = requesterId,
                OwnerUserId = ownerId,
                SelectedText = $"他人のリクエスト_{tid}",
                Status = TradeStatus.Proposed,
                OwnerId = requesterId
            };
            dbContext.ItemSplitRequests.Add(request);
            await dbContext.SaveChangesAsync();

            // Act
            Result<bool> result = await service.CancelSplitAsync(request.Id, otherUserId);

            // Assert
            Assert.True(result is Failure);
            switch (result)
            {
                case Failure failure:
                    Assert.Contains("権限がありません", failure.ErrorMessage);
                    break;
            }
        }
    }
}