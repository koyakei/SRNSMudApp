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
    public async Task SplitDirectlyAsync_Success_CreatesQuotedItemAndReplacesSelectedText()
    {
        var (dbContext, service, mockNotification, tid) = CreateScope();
        await using (dbContext)
        {
            var ownerId = $"owner_{tid}";
            await dbContext.SeedUsersAsync(ownerId);

            var originalItem = new Item
            {
                OwnerId = ownerId,
                Content = "前半_[分割対象]_後半",
                IsPrivate = true,
                TargetUserGroupId = null
            };
            dbContext.Items.Add(originalItem);
            await dbContext.SaveChangesAsync();

            Result<Item> result = await service.SplitDirectlyAsync(
                originalItem.Id,
                "[分割対象]",
                ownerId);

            Assert.True(result is Success<Item>);
            switch (result)
            {
                case Success<Item> success:
                    Assert.Equal("[分割対象]", success.Value.Content);
                    Assert.Equal(ownerId, success.Value.OwnerId);
                    Assert.Equal(originalItem.Id, success.Value.QuotedItemId);

                    dbContext.ChangeTracker.Clear();
                    Item? updatedOriginal = await dbContext.Items.FindAsync(originalItem.Id);
                    Assert.NotNull(updatedOriginal);
                    Assert.Equal($"前半_/ItemDetail/{success.Value.Id}_後半", updatedOriginal.Content);
                    break;
            }

            mockNotification.Verify(n => n.NotifyNotificationsChanged(), Times.Once);
        }
    }

    [Fact]
    public async Task SplitDirectlyAsync_Fails_WhenUserIsNotOwner()
    {
        var (dbContext, service, _, tid) = CreateScope();
        await using (dbContext)
        {
            var ownerId = $"owner_{tid}";
            var otherUserId = $"other_{tid}";
            await dbContext.SeedUsersAsync(ownerId, otherUserId);

            var originalItem = new Item
            {
                OwnerId = ownerId,
                Content = $"分割対象_{tid}"
            };
            dbContext.Items.Add(originalItem);
            await dbContext.SaveChangesAsync();

            Result<Item> result = await service.SplitDirectlyAsync(
                originalItem.Id,
                $"分割対象_{tid}",
                otherUserId);

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
    public async Task SplitDirectlyAsync_Fails_WhenSelectedTextIsNotInOriginalItem()
    {
        var (dbContext, service, _, tid) = CreateScope();
        await using (dbContext)
        {
            var ownerId = $"owner_{tid}";
            await dbContext.SeedUsersAsync(ownerId);

            var originalItem = new Item
            {
                OwnerId = ownerId,
                Content = $"本文_{tid}"
            };
            dbContext.Items.Add(originalItem);
            await dbContext.SaveChangesAsync();

            Result<Item> result = await service.SplitDirectlyAsync(
                originalItem.Id,
                "存在しないテキスト",
                ownerId);

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
                    Assert.Equal(originalItem.Id, success.Value.QuotedItemId);
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

    [Fact]
    public async Task RequestSplitAsync_WithCrlfInItemAndLfInSelectedText_Succeeds()
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
                Content = $"1行目_{tid}\r\n2行目_{tid}\r\n3行目_{tid}"
            };
            dbContext.Items.Add(originalItem);
            await dbContext.SaveChangesAsync();

            // Act: selectedText has \n instead of \r\n
            Result<ItemSplitRequest> result = await service.RequestSplitAsync(
                originalItem.Id,
                $"2行目_{tid}\n3行目_{tid}",
                requesterId);

            // Assert
            Assert.True(result is Success<ItemSplitRequest>);
        }
    }

    [Fact]
    public async Task ApproveSplitAsync_WithCrlfInItemAndLfInSelectedText_ReplacesTextCorrectly()
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
                Content = $"先頭_{tid}\r\n分割対象_{tid}\r\n末尾_{tid}"
            };
            dbContext.Items.Add(originalItem);

            var request = new ItemSplitRequest
            {
                OriginalItem = originalItem,
                RequesterUserId = requesterId,
                OwnerUserId = ownerId,
                SelectedText = $"分割対象_{tid}\n末尾_{tid}", // LF only
                Status = TradeStatus.Proposed,
                OwnerId = requesterId
            };
            dbContext.ItemSplitRequests.Add(request);
            await dbContext.SaveChangesAsync();

            // Act
            Result<Item> result = await service.ApproveSplitAsync(request.Id, ownerId);

            // Assert
            Assert.True(result is Success<Item>);
            dbContext.ChangeTracker.Clear();
            var updatedOriginal = await dbContext.Items.FindAsync(originalItem.Id);
            Assert.NotNull(updatedOriginal);
            Assert.Contains($"/ItemDetail/", updatedOriginal.Content);
            Assert.DoesNotContain($"分割対象_{tid}", updatedOriginal.Content);
        }
    }

    [Fact]
    public async Task RequestSplitAsync_WithRequesterQuoteItemId_StoresQuoteItemId()
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
                Content = $"分割元本文_{tid}"
            };
            dbContext.Items.Add(originalItem);
            await dbContext.SaveChangesAsync();

            var quoteItem = new Item
            {
                OwnerId = requesterId,
                Content = $"引用本文_{tid}",
                QuotedItemId = originalItem.Id
            };
            dbContext.Items.Add(quoteItem);
            await dbContext.SaveChangesAsync();

            // Act
            Result<ItemSplitRequest> result = await service.RequestSplitAsync(
                originalItem.Id,
                $"本文_{tid}",
                requesterId,
                quoteItem.Id);

            // Assert
            Assert.True(result is Success<ItemSplitRequest>);
            switch (result)
            {
                case Success<ItemSplitRequest> success:
                    Assert.Equal(quoteItem.Id, success.Value.RequesterQuoteItemId);
                    break;
            }
        }
    }

    [Fact]
    public async Task ApproveSplitAsync_WithRequesterQuoteItem_MigratesTagsAndQuotedBy_AndDeletesQuoteItem()
    {
        var (dbContext, service, _, tid) = CreateScope();
        await using (dbContext)
        {
            var ownerId = $"owner_{tid}";
            var requesterId = $"requester_{tid}";
            var thirdUserId = $"third_{tid}";
            await dbContext.SeedUsersAsync(ownerId, requesterId, thirdUserId);

            // タグの作成
            var tag1 = new Tag { Name = $"tag1_{tid}", OwnerId = ownerId };
            var tag2 = new Tag { Name = $"tag2_{tid}", OwnerId = ownerId };
            dbContext.Tags.AddRange(tag1, tag2);

            // 元アイテム
            var originalItem = new Item
            {
                OwnerId = ownerId,
                Content = $"冒頭 [切り抜き対象_{tid}] 末尾"
            };
            dbContext.Items.Add(originalItem);
            await dbContext.SaveChangesAsync();

            // 申請者が同時に投稿した引用アイテム
            var quoteItem = new Item
            {
                OwnerId = requesterId,
                Content = $"引用投稿コメント_{tid}",
                QuotedItemId = originalItem.Id
            };
            dbContext.Items.Add(quoteItem);
            await dbContext.SaveChangesAsync();

            // 引用アイテムにタグ（tag1, tag2）を付与
            var rel1 = new TagRelation { ItemId = quoteItem.Id, TagId = tag1.Id, Weight = 3, OwnerId = requesterId };
            var rel2 = new TagRelation { ItemId = quoteItem.Id, TagId = tag2.Id, Weight = 1, OwnerId = requesterId };
            dbContext.TagRelations.AddRange(rel1, rel2);

            // 第三者がこの引用アイテムをさらに引用（被引用アイテム）
            var thirdQuotingItem = new Item
            {
                OwnerId = thirdUserId,
                Content = $"孫引用_{tid}",
                QuotedItemId = quoteItem.Id
            };
            dbContext.Items.Add(thirdQuotingItem);
            await dbContext.SaveChangesAsync();

            // 引用アイテムに対するリプライ
            var replyToQuoteItem = new Item
            {
                OwnerId = thirdUserId,
                Content = $"リプライ_{tid}",
                ParentItemId = quoteItem.Id,
                RootItemId = quoteItem.Id
            };
            dbContext.Items.Add(replyToQuoteItem);
            await dbContext.SaveChangesAsync();

            // 分割リクエスト（RequesterQuoteItemId に quoteItem.Id を指定）
            var request = new ItemSplitRequest
            {
                OriginalItem = originalItem,
                RequesterUserId = requesterId,
                OwnerUserId = ownerId,
                SelectedText = $"[切り抜き対象_{tid}]",
                Status = TradeStatus.Proposed,
                OwnerId = requesterId,
                RequesterQuoteItemId = quoteItem.Id
            };
            dbContext.ItemSplitRequests.Add(request);
            await dbContext.SaveChangesAsync();

            // Act: オーナーが分割リクエストを承認
            Result<Item> result = await service.ApproveSplitAsync(request.Id, ownerId);

            // Assert: 新アイテム作成成功
            if (result is not Success<Item> successResult)
            {
                Assert.Fail(result is Failure f ? f.ErrorMessage : "result is not Success");
                return;
            }
            Item newItem = successResult.Value;
            Assert.Equal($"[切り抜き対象_{tid}]", newItem.Content);

            dbContext.ChangeTracker.Clear();

            // 1. 引用アイテムが削除されていること
            Item? deletedQuote = await dbContext.Items.FindAsync(quoteItem.Id);
            Assert.Null(deletedQuote);

            // 2. 引用アイテムについていたタグが新アイテムへ移行されていること
            var migratedRelations = await dbContext.TagRelations
                .Where(r => r.ItemId == newItem.Id)
                .ToListAsync();
            Assert.Equal(2, migratedRelations.Count);
            Assert.Contains(migratedRelations, r => r.TagId == tag1.Id && r.Weight == 3);
            Assert.Contains(migratedRelations, r => r.TagId == tag2.Id && r.Weight == 1);

            // 3. 被引用アイテムの QuotedItemId が新アイテムの Id に置き換わっていること
            Item? updatedQuotingItem = await dbContext.Items.FindAsync(thirdQuotingItem.Id);
            Assert.NotNull(updatedQuotingItem);
            Assert.Equal(newItem.Id, updatedQuotingItem.QuotedItemId);

            // 4. リプライアイテムの ParentItemId が新アイテムの Id に置き換わっていること
            Item? updatedReplyItem = await dbContext.Items.FindAsync(replyToQuoteItem.Id);
            Assert.NotNull(updatedReplyItem);
            Assert.Equal(newItem.Id, updatedReplyItem.ParentItemId);
        }
    }

    [Fact]
    public async Task RequestSplitAsync_WhenQuoteItemBelongsToDifferentUser_ReturnsFailure()
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
                Content = $"分割元アイテム_{tid}"
            };
            dbContext.Items.Add(originalItem);
            await dbContext.SaveChangesAsync();

            // 他人が作成した引用アイテム
            var quoteItem = new Item
            {
                OwnerId = otherUserId,
                Content = $"他人の引用_{tid}",
                QuotedItemId = originalItem.Id
            };
            dbContext.Items.Add(quoteItem);
            await dbContext.SaveChangesAsync();

            // Act: requesterId が他人の quoteItem.Id を指定してリクエスト
            Result<ItemSplitRequest> result = await service.RequestSplitAsync(
                originalItem.Id,
                $"アイテム_{tid}",
                requesterId,
                quoteItem.Id);

            // Assert
            Assert.True(result is Failure);
            if (result is Failure failure)
            {
                Assert.Contains("指定された引用アイテムが見つかりません", failure.ErrorMessage);
            }
        }
    }

    [Fact]
    public async Task RequestSplitAsync_WhenQuoteItemDoesNotExist_ReturnsFailure()
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
                Content = $"分割元アイテム_{tid}"
            };
            dbContext.Items.Add(originalItem);
            await dbContext.SaveChangesAsync();

            // Act: 存在しない quoteItemId を指定
            Result<ItemSplitRequest> result = await service.RequestSplitAsync(
                originalItem.Id,
                $"アイテム_{tid}",
                requesterId,
                999999);

            // Assert
            Assert.True(result is Failure);
            if (result is Failure failure)
            {
                Assert.Contains("指定された引用アイテムが見つかりません", failure.ErrorMessage);
            }
        }
    }

    [Fact]
    public async Task ApproveSplitAsync_WhenRequesterQuoteItemHasDuplicateTags_CombinesWeights()
    {
        var (dbContext, service, _, tid) = CreateScope();
        await using (dbContext)
        {
            var ownerId = $"owner_{tid}";
            var requesterId = $"requester_{tid}";
            await dbContext.SeedUsersAsync(ownerId, requesterId);

            var tag = new Tag { Name = $"tag_{tid}", OwnerId = ownerId };
            dbContext.Tags.Add(tag);

            var originalItem = new Item
            {
                OwnerId = ownerId,
                Content = $"開始 [切り抜き_{tid}] 終了"
            };
            dbContext.Items.Add(originalItem);
            await dbContext.SaveChangesAsync();

            var quoteItem = new Item
            {
                OwnerId = requesterId,
                Content = $"引用_{tid}",
                QuotedItemId = originalItem.Id
            };
            dbContext.Items.Add(quoteItem);
            await dbContext.SaveChangesAsync();

            // 同一タグに異なる重みで 2 件の TagRelation を付与（別ユーザーまたは別コンテキストの紐付けを想定）
            var rel1 = new TagRelation { ItemId = quoteItem.Id, TagId = tag.Id, Weight = 2, OwnerId = requesterId };
            var rel2 = new TagRelation { ItemId = quoteItem.Id, TagId = tag.Id, Weight = 3, OwnerId = ownerId };
            dbContext.TagRelations.AddRange(rel1, rel2);

            var request = new ItemSplitRequest
            {
                OriginalItem = originalItem,
                RequesterUserId = requesterId,
                OwnerUserId = ownerId,
                SelectedText = $"[切り抜き_{tid}]",
                Status = TradeStatus.Proposed,
                OwnerId = requesterId,
                RequesterQuoteItemId = quoteItem.Id
            };
            dbContext.ItemSplitRequests.Add(request);
            await dbContext.SaveChangesAsync();

            // Act
            Result<Item> result = await service.ApproveSplitAsync(request.Id, ownerId);

            // Assert
            Assert.True(result is Success<Item>);
            if (result is Success<Item> success)
            {
                dbContext.ChangeTracker.Clear();
                var migratedRelations = await dbContext.TagRelations
                    .Where(r => r.ItemId == success.Value.Id && r.TagId == tag.Id)
                    .ToListAsync();

                // 重複がマージされて 1 レコードになり、Weight が 2 + 3 = 5 に合算されていること
                Assert.Single(migratedRelations);
                Assert.Equal(5, migratedRelations[0].Weight);
            }
        }
    }

    [Fact]
    public async Task ApproveSplitAsync_WhenRequesterQuoteItemAlreadyDeleted_StillSucceeds()
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
                Content = $"先頭 [対象_{tid}] 末尾"
            };
            dbContext.Items.Add(originalItem);
            await dbContext.SaveChangesAsync();

            var quoteItem = new Item
            {
                OwnerId = requesterId,
                Content = $"削除予定の引用_{tid}",
                QuotedItemId = originalItem.Id
            };
            dbContext.Items.Add(quoteItem);
            await dbContext.SaveChangesAsync();

            var request = new ItemSplitRequest
            {
                OriginalItem = originalItem,
                RequesterUserId = requesterId,
                OwnerUserId = ownerId,
                SelectedText = $"[対象_{tid}]",
                Status = TradeStatus.Proposed,
                OwnerId = requesterId,
                RequesterQuoteItemId = quoteItem.Id
            };
            dbContext.ItemSplitRequests.Add(request);
            await dbContext.SaveChangesAsync();

            // 承認前に引用アイテムを手動削除
            dbContext.Items.Remove(quoteItem);
            await dbContext.SaveChangesAsync();

            // Act
            Result<Item> result = await service.ApproveSplitAsync(request.Id, ownerId);

            // Assert: エラーにならず新アイテムが作成されること
            Assert.True(result is Success<Item>);
            if (result is Success<Item> success)
            {
                Assert.Equal($"[対象_{tid}]", success.Value.Content);
            }

            dbContext.ChangeTracker.Clear();
            ItemSplitRequest? updatedReq = await dbContext.ItemSplitRequests.FindAsync(request.Id);
            Assert.NotNull(updatedReq);
            Assert.Equal(TradeStatus.Executed, updatedReq.Status);
        }
    }

    [Fact]
    public async Task RejectSplitAsync_WithRequesterQuoteItem_DoesNotDeleteQuoteItem()
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
                Content = $"本文_{tid}"
            };
            dbContext.Items.Add(originalItem);
            await dbContext.SaveChangesAsync();

            var quoteItem = new Item
            {
                OwnerId = requesterId,
                Content = $"引用_{tid}",
                QuotedItemId = originalItem.Id
            };
            dbContext.Items.Add(quoteItem);
            await dbContext.SaveChangesAsync();

            var request = new ItemSplitRequest
            {
                OriginalItem = originalItem,
                RequesterUserId = requesterId,
                OwnerUserId = ownerId,
                SelectedText = $"本文_{tid}",
                Status = TradeStatus.Proposed,
                OwnerId = requesterId,
                RequesterQuoteItemId = quoteItem.Id
            };
            dbContext.ItemSplitRequests.Add(request);
            await dbContext.SaveChangesAsync();

            // Act: 却下実行
            Result<bool> result = await service.RejectSplitAsync(request.Id, ownerId, "却下理由");

            // Assert
            Assert.True(result is Success<bool>);
            dbContext.ChangeTracker.Clear();

            // 引用アイテムが削除されずに残っていること
            Item? remainingQuote = await dbContext.Items.FindAsync(quoteItem.Id);
            Assert.NotNull(remainingQuote);
            Assert.Equal(quoteItem.Id, remainingQuote.Id);

            ItemSplitRequest? updatedReq = await dbContext.ItemSplitRequests.FindAsync(request.Id);
            Assert.NotNull(updatedReq);
            Assert.Equal(TradeStatus.Rejected, updatedReq.Status);
        }
    }

    [Fact]
    public async Task CancelSplitAsync_WithRequesterQuoteItem_DoesNotDeleteQuoteItem()
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
                Content = $"本文_{tid}"
            };
            dbContext.Items.Add(originalItem);
            await dbContext.SaveChangesAsync();

            var quoteItem = new Item
            {
                OwnerId = requesterId,
                Content = $"引用_{tid}",
                QuotedItemId = originalItem.Id
            };
            dbContext.Items.Add(quoteItem);
            await dbContext.SaveChangesAsync();

            var request = new ItemSplitRequest
            {
                OriginalItem = originalItem,
                RequesterUserId = requesterId,
                OwnerUserId = ownerId,
                SelectedText = $"本文_{tid}",
                Status = TradeStatus.Proposed,
                OwnerId = requesterId,
                RequesterQuoteItemId = quoteItem.Id
            };
            dbContext.ItemSplitRequests.Add(request);
            await dbContext.SaveChangesAsync();

            // Act: 申請者による取り下げ
            Result<bool> result = await service.CancelSplitAsync(request.Id, requesterId);

            // Assert
            Assert.True(result is Success<bool>);
            dbContext.ChangeTracker.Clear();

            // 引用アイテムが削除されずに残っていること
            Item? remainingQuote = await dbContext.Items.FindAsync(quoteItem.Id);
            Assert.NotNull(remainingQuote);
            Assert.Equal(quoteItem.Id, remainingQuote.Id);

            ItemSplitRequest? updatedReq = await dbContext.ItemSplitRequests.FindAsync(request.Id);
            Assert.NotNull(updatedReq);
            Assert.Equal(TradeStatus.Canceled, updatedReq.Status);
        }
    }

    [Fact]
    public async Task ApproveSplitAsync_WithReactionTagAndCommentOnQuoteItem_MigratesReactionTagAndCommentToNewItem()
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
                Content = $"前半_[リアクション分割_{tid}]_後半"
            };
            dbContext.Items.Add(originalItem);
            await dbContext.SaveChangesAsync();

            // 引用アイテムを作成
            var quoteItem = new Item
            {
                OwnerId = requesterId,
                Content = $"リアクション分割_{tid}",
                QuotedItemId = originalItem.Id
            };
            dbContext.Items.Add(quoteItem);
            await dbContext.SaveChangesAsync();

            // リアクションタグとコメント用アイテムを作成
            var reactionTag = new Tag
            {
                Name = $"真_{tid}",
                OwnerId = requesterId
            };
            dbContext.Tags.Add(reactionTag);

            var commentItem = new Item
            {
                OwnerId = requesterId,
                Content = $"素晴らしい主張です_{tid}"
            };
            dbContext.Items.Add(commentItem);
            await dbContext.SaveChangesAsync();

            // 引用アイテムにリアクションタグ（コメント付き）を付与
            var reactionRelation = new TagRelation
            {
                TagId = reactionTag.Id,
                ItemId = quoteItem.Id,
                Weight = 1,
                OwnerId = requesterId,
                CommentItemId = commentItem.Id,
                CreatedDate = DateTime.UtcNow,
                UpdatedDate = DateTime.UtcNow
            };
            dbContext.TagRelations.Add(reactionRelation);

            // 分割リクエスト（RequesterQuoteItemId に quoteItem.Id を指定）
            var request = new ItemSplitRequest
            {
                OriginalItem = originalItem,
                RequesterUserId = requesterId,
                OwnerUserId = ownerId,
                SelectedText = $"リアクション分割_{tid}",
                Status = TradeStatus.Proposed,
                OwnerId = requesterId,
                RequesterQuoteItemId = quoteItem.Id
            };
            dbContext.ItemSplitRequests.Add(request);
            await dbContext.SaveChangesAsync();

            // Act: オーナーが分割リクエストを承認
            Result<Item> result = await service.ApproveSplitAsync(request.Id, ownerId);

            // Assert
            if (result is not Success<Item> successResult)
            {
                Assert.Fail(result is Failure f ? f.ErrorMessage : "result is not Success");
                return;
            }
            Item newItem = successResult.Value;

            dbContext.ChangeTracker.Clear();

            // 1. 引用アイテムが削除されていること
            Item? deletedQuote = await dbContext.Items.FindAsync(quoteItem.Id);
            Assert.Null(deletedQuote);

            // 2. コメント用アイテムは残っていること
            Item? remainingComment = await dbContext.Items.FindAsync(commentItem.Id);
            Assert.NotNull(remainingComment);

            // 3. リアクションタグ情報（Weight, CommentItemId）が新アイテムへ移行されていること
            var migratedRelations = await dbContext.TagRelations
                .Where(r => r.ItemId == newItem.Id)
                .ToListAsync();

            TagRelation migratedReaction = Assert.Single(migratedRelations);
            Assert.Equal(reactionTag.Id, migratedReaction.TagId);
            Assert.Equal(1, migratedReaction.Weight);
            Assert.Equal(commentItem.Id, migratedReaction.CommentItemId);
        }
    }
}