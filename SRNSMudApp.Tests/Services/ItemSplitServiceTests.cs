using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Moq;

using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Resources;
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
    public void Constructor_NullParameters_ThrowsArgumentNullException()
    {
        var mockDbFactory = new Mock<IDbContextFactory<ApplicationDbContext>>();
        var mockNotificationService = new Mock<INotificationService>();

        Assert.Throws<ArgumentNullException>(() => new ItemSplitService(null!, mockNotificationService.Object));
        Assert.Throws<ArgumentNullException>(() => new ItemSplitService(mockDbFactory.Object, null!));
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

            // 申請者が同時に投稿した引用アイテム（本文は切り抜き対象テキスト）
            var quoteItem = new Item
            {
                OwnerId = requesterId,
                Content = $"[切り抜き対象_{tid}]",
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

            // 申請者の引用コメント（リプライ）および第三者のリプライ
            var requesterCommentReply = new Item
            {
                OwnerId = requesterId,
                Content = $"引用投稿コメント_{tid}",
                ParentItemId = quoteItem.Id,
                RootItemId = quoteItem.Id
            };
            var replyToQuoteItem = new Item
            {
                OwnerId = thirdUserId,
                Content = $"リプライ_{tid}",
                ParentItemId = quoteItem.Id,
                RootItemId = quoteItem.Id
            };
            dbContext.Items.AddRange(requesterCommentReply, replyToQuoteItem);
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

            // 4. 申請者の引用コメントおよび第三者のリプライが消失せず、新アイテムの傘下へ引き継がれていること
            Item? updatedRequesterComment = await dbContext.Items.FindAsync(requesterCommentReply.Id);
            Assert.NotNull(updatedRequesterComment);
            Assert.Equal($"引用投稿コメント_{tid}", updatedRequesterComment.Content);
            Assert.Equal(newItem.Id, updatedRequesterComment.ParentItemId);
            Assert.Equal(newItem.Id, updatedRequesterComment.RootItemId);

            Item? updatedReplyItem = await dbContext.Items.FindAsync(replyToQuoteItem.Id);
            Assert.NotNull(updatedReplyItem);
            Assert.Equal(newItem.Id, updatedReplyItem.ParentItemId);
            Assert.Equal(newItem.Id, updatedReplyItem.RootItemId);
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
    public async Task ApproveSplitAsync_WhenRequesterQuoteItemHasDuplicateTags_CombinesWeightsForSameOwner_AndPreservesDifferentOwners()
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
                Content = $"[切り抜き_{tid}]",
                QuotedItemId = originalItem.Id
            };
            dbContext.Items.Add(quoteItem);
            await dbContext.SaveChangesAsync();

            // 同一ユーザー(requesterId)の重複 TagRelation は合算され、別ユーザー(ownerId)の TagRelation は別レコードとして保持される
            var rel1 = new TagRelation { ItemId = quoteItem.Id, TagId = tag.Id, Weight = 2, OwnerId = requesterId };
            var rel2 = new TagRelation { ItemId = quoteItem.Id, TagId = tag.Id, Weight = 3, OwnerId = requesterId };
            var relOtherOwner = new TagRelation { ItemId = quoteItem.Id, TagId = tag.Id, Weight = 4, OwnerId = ownerId };
            dbContext.TagRelations.AddRange(rel1, rel2, relOtherOwner);

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

                // 同一オーナー(requesterId)は 2 + 3 = 5 にマージされ、別オーナー(ownerId)の Weight=4 と合わせて 2 レコードになること
                Assert.Equal(2, migratedRelations.Count);
                Assert.Contains(migratedRelations, r => r.OwnerId == requesterId && r.Weight == 5);
                Assert.Contains(migratedRelations, r => r.OwnerId == ownerId && r.Weight == 4);
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
    public async Task RejectSplitAsync_WithRequesterQuoteItem_DeletesQuoteItem()
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

            // 案A: 引用アイテムが自動削除されること
            Item? remainingQuote = await dbContext.Items.FindAsync(quoteItem.Id);
            Assert.Null(remainingQuote);

            ItemSplitRequest? updatedReq = await dbContext.ItemSplitRequests.FindAsync(request.Id);
            Assert.NotNull(updatedReq);
            Assert.Equal(TradeStatus.Rejected, updatedReq.Status);
            Assert.Null(updatedReq.RequesterQuoteItemId);
        }
    }

    [Fact]
    public async Task CancelSplitAsync_WithRequesterQuoteItem_DeletesQuoteItem()
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

            // 案A: 引用アイテムが自動削除されること
            Item? remainingQuote = await dbContext.Items.FindAsync(quoteItem.Id);
            Assert.Null(remainingQuote);

            ItemSplitRequest? updatedReq = await dbContext.ItemSplitRequests.FindAsync(request.Id);
            Assert.NotNull(updatedReq);
            Assert.Equal(TradeStatus.Canceled, updatedReq.Status);
            Assert.Null(updatedReq.RequesterQuoteItemId);
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

    [Fact]
    public async Task ApproveSplitAsync_LogsStructuredMessage_OnSuccess()
    {
        var tid = Guid.NewGuid().ToString("N")[..8];
        var dbContext = new ApplicationDbContext(_sharedDb.Options);
        var mockDbFactory = new Mock<IDbContextFactory<ApplicationDbContext>>();
        mockDbFactory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new ApplicationDbContext(_sharedDb.Options));

        var mockNotificationService = new Mock<INotificationService>();
        var mockLogger = new Mock<ILogger<ItemSplitService>>();
        mockLogger.Setup(x => x.IsEnabled(LogLevel.Information)).Returns(true);
        var service = new ItemSplitService(mockDbFactory.Object, mockNotificationService.Object, mockLogger.Object);

        await using (dbContext)
        {
            var ownerId = $"owner_{tid}";
            var requesterId = $"requester_{tid}";
            await dbContext.SeedUsersAsync(ownerId, requesterId);

            var originalItem = new Item
            {
                OwnerId = ownerId,
                Content = $"前半_[ログ対象_{tid}]_後半"
            };
            dbContext.Items.Add(originalItem);
            await dbContext.SaveChangesAsync();

            var request = new ItemSplitRequest
            {
                OriginalItem = originalItem,
                RequesterUserId = requesterId,
                OwnerUserId = ownerId,
                SelectedText = $"ログ対象_{tid}",
                Status = TradeStatus.Proposed,
                OwnerId = requesterId
            };
            dbContext.ItemSplitRequests.Add(request);
            await dbContext.SaveChangesAsync();

            // Act
            Result<Item> result = await service.ApproveSplitAsync(request.Id, ownerId);

            // Assert
            Assert.True(result is Success<Item>);
            mockLogger.Verify(
                x => x.Log(
                    LogLevel.Information,
                    new EventId(3003, It.IsAny<string>()),
                    It.Is<It.IsAnyType>((v, t) => true),
                    It.IsAny<Exception>(),
                    It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
                Times.Once);
        }
    }

    [Fact]
    public async Task ApproveSplitAsync_WithRequesterQuoteItemReplies_MigratesParentAndRootItemIds()
    {
        var (dbContext, service, _, tid) = CreateScope();
        await using (dbContext)
        {
            var ownerId = $"owner_{tid}";
            var requesterId = $"requester_{tid}";
            var replyAuthorId = $"reply_author_{tid}";
            await dbContext.SeedUsersAsync(ownerId, requesterId, replyAuthorId);

            var originalItem = new Item
            {
                OwnerId = ownerId,
                Content = $"本文_[リプライ対象_{tid}]_末尾"
            };
            dbContext.Items.Add(originalItem);
            await dbContext.SaveChangesAsync();

            var quoteItem = new Item
            {
                OwnerId = requesterId,
                Content = $"リプライ対象_{tid}",
                QuotedItemId = originalItem.Id
            };
            dbContext.Items.Add(quoteItem);
            await dbContext.SaveChangesAsync();

            var replyItem = new Item
            {
                OwnerId = replyAuthorId,
                Content = "引用アイテムへのリプライです",
                ParentItemId = quoteItem.Id,
                RootItemId = quoteItem.Id
            };
            dbContext.Items.Add(replyItem);
            await dbContext.SaveChangesAsync();

            var request = new ItemSplitRequest
            {
                OriginalItem = originalItem,
                RequesterUserId = requesterId,
                OwnerUserId = ownerId,
                SelectedText = $"リプライ対象_{tid}",
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
            if (result is not Success<Item> success)
            {
                return;
            }
            Item newItem = success.Value;

            dbContext.ChangeTracker.Clear();

            Item? updatedReply = await dbContext.Items.FindAsync(replyItem.Id);
            Assert.NotNull(updatedReply);
            Assert.Equal(newItem.Id, updatedReply.ParentItemId);
            Assert.Equal(newItem.Id, updatedReply.RootItemId);
        }
    }

    [Fact]
    public async Task ApproveSplitAsync_Fails_WhenRequestNotProposed()
    {
        var (dbContext, service, _, tid) = CreateScope();
        await using (dbContext)
        {
            var ownerId = $"owner_{tid}";
            var requesterId = $"requester_{tid}";
            await dbContext.SeedUsersAsync(ownerId, requesterId);

            var originalItem = new Item { OwnerId = ownerId, Content = "テスト_[選択]_本文" };
            dbContext.Items.Add(originalItem);
            await dbContext.SaveChangesAsync();

            var request = new ItemSplitRequest
            {
                OriginalItem = originalItem,
                RequesterUserId = requesterId,
                OwnerUserId = ownerId,
                SelectedText = "選択",
                Status = TradeStatus.Executed,
                OwnerId = requesterId
            };
            dbContext.ItemSplitRequests.Add(request);
            await dbContext.SaveChangesAsync();

            Result<Item> result = await service.ApproveSplitAsync(request.Id, ownerId);

            Assert.True(result is Failure);
        }
    }

    [Fact]
    public async Task RejectSplitAsync_Fails_WhenRequestNotProposed()
    {
        var (dbContext, service, _, tid) = CreateScope();
        await using (dbContext)
        {
            var ownerId = $"owner_{tid}";
            var requesterId = $"requester_{tid}";
            await dbContext.SeedUsersAsync(ownerId, requesterId);

            var originalItem = new Item { OwnerId = ownerId, Content = "テスト本文" };
            dbContext.Items.Add(originalItem);
            await dbContext.SaveChangesAsync();

            var request = new ItemSplitRequest
            {
                OriginalItem = originalItem,
                RequesterUserId = requesterId,
                OwnerUserId = ownerId,
                SelectedText = "テスト",
                Status = TradeStatus.Rejected,
                OwnerId = requesterId
            };
            dbContext.ItemSplitRequests.Add(request);
            await dbContext.SaveChangesAsync();

            Result<bool> result = await service.RejectSplitAsync(request.Id, ownerId, "却下理由");

            Assert.True(result is Failure);
        }
    }

    [Fact]
    public async Task CancelSplitAsync_Fails_WhenRequestNotProposed()
    {
        var (dbContext, service, _, tid) = CreateScope();
        await using (dbContext)
        {
            var ownerId = $"owner_{tid}";
            var requesterId = $"requester_{tid}";
            await dbContext.SeedUsersAsync(ownerId, requesterId);

            var originalItem = new Item { OwnerId = ownerId, Content = "テスト本文" };
            dbContext.Items.Add(originalItem);
            await dbContext.SaveChangesAsync();

            var request = new ItemSplitRequest
            {
                OriginalItem = originalItem,
                RequesterUserId = requesterId,
                OwnerUserId = ownerId,
                SelectedText = "テスト",
                Status = TradeStatus.Canceled,
                OwnerId = requesterId
            };
            dbContext.ItemSplitRequests.Add(request);
            await dbContext.SaveChangesAsync();

            Result<bool> result = await service.CancelSplitAsync(request.Id, requesterId);

            Assert.True(result is Failure);
        }
    }

    [Fact]
    public async Task GetPendingSplitRequestsForOriginalItemAsync_ReturnsOnlyProposedRequestsInDescendingOrder()
    {
        var (dbContext, service, _, tid) = CreateScope();
        await using (dbContext)
        {
            var ownerId = $"owner_{tid}";
            var requesterId1 = $"requester1_{tid}";
            var requesterId2 = $"requester2_{tid}";
            await dbContext.SeedUsersAsync(ownerId, requesterId1, requesterId2);

            var targetItem = new Item { OwnerId = ownerId, Content = "対象アイテム本文" };
            var otherItem = new Item { OwnerId = ownerId, Content = "別アイテム本文" };
            dbContext.Items.AddRange(targetItem, otherItem);
            await dbContext.SaveChangesAsync();

            var req1 = new ItemSplitRequest
            {
                OriginalItem = targetItem,
                RequesterUserId = requesterId1,
                OwnerUserId = ownerId,
                SelectedText = "対象",
                Status = TradeStatus.Proposed,
                OwnerId = requesterId1,
                CreatedDate = DateTime.UtcNow.AddMinutes(-10)
            };
            var req2 = new ItemSplitRequest
            {
                OriginalItem = targetItem,
                RequesterUserId = requesterId2,
                OwnerUserId = ownerId,
                SelectedText = "アイテム",
                Status = TradeStatus.Proposed,
                OwnerId = requesterId2,
                CreatedDate = DateTime.UtcNow
            };
            var reqExecuted = new ItemSplitRequest
            {
                OriginalItem = targetItem,
                RequesterUserId = requesterId1,
                OwnerUserId = ownerId,
                SelectedText = "本文",
                Status = TradeStatus.Executed,
                OwnerId = requesterId1,
                CreatedDate = DateTime.UtcNow.AddMinutes(-5)
            };
            var reqOtherItem = new ItemSplitRequest
            {
                OriginalItem = otherItem,
                RequesterUserId = requesterId1,
                OwnerUserId = ownerId,
                SelectedText = "別",
                Status = TradeStatus.Proposed,
                OwnerId = requesterId1,
                CreatedDate = DateTime.UtcNow
            };

            dbContext.ItemSplitRequests.AddRange(req1, req2, reqExecuted, reqOtherItem);
            await dbContext.SaveChangesAsync();

            // Interceptor sets CreatedDate on Added, so adjust CreatedDate afterwards
            req1.CreatedDate = DateTime.UtcNow.AddMinutes(-10);
            reqExecuted.CreatedDate = DateTime.UtcNow.AddMinutes(-5);
            await dbContext.SaveChangesAsync();

            // Act
            IReadOnlyList<ItemSplitRequest> results =
                await service.GetPendingSplitRequestsForOriginalItemAsync(targetItem.Id);

            // Assert
            Assert.Equal(2, results.Count);
            Assert.Equal(req2.Id, results[0].Id); // 新しい順
            Assert.Equal(req1.Id, results[1].Id);
            Assert.All(results, r => Assert.Equal(TradeStatus.Proposed, r.Status));
        }
    }

    [Fact]
    public async Task GetSplitRequestByIdAsync_ReturnsRequestWithRelatedEntities()
    {
        var (dbContext, service, _, tid) = CreateScope();
        await using (dbContext)
        {
            var ownerId = $"owner_{tid}";
            var requesterId = $"requester_{tid}";
            await dbContext.SeedUsersAsync(ownerId, requesterId);

            var originalItem = new Item { OwnerId = ownerId, Content = "テスト本文" };
            dbContext.Items.Add(originalItem);
            await dbContext.SaveChangesAsync();

            var request = new ItemSplitRequest
            {
                OriginalItem = originalItem,
                RequesterUserId = requesterId,
                OwnerUserId = ownerId,
                SelectedText = "テスト",
                Status = TradeStatus.Proposed,
                OwnerId = requesterId
            };
            dbContext.ItemSplitRequests.Add(request);
            await dbContext.SaveChangesAsync();

            // Act
            ItemSplitRequest? found = await service.GetSplitRequestByIdAsync(request.Id);
            ItemSplitRequest? notFound = await service.GetSplitRequestByIdAsync(999999);

            // Assert
            Assert.NotNull(found);
            Assert.Equal(request.Id, found.Id);
            Assert.NotNull(found.OriginalItem);
            Assert.NotNull(found.RequesterUser);
            Assert.NotNull(found.OwnerUser);
            Assert.Null(notFound);
        }
    }

    [Fact]
    public async Task ApproveSplitAsync_WithMultipleDiverseTagsOnQuoteItem_MergesAllTagsCorrectly()
    {
        var (dbContext, service, _, tid) = CreateScope();
        await using (dbContext)
        {
            var ownerId = $"owner_{tid}";
            var requesterId = $"requester_{tid}";
            await dbContext.SeedUsersAsync(ownerId, requesterId);

            var tagShin = new Tag { Name = $"真_{tid}", OwnerId = ownerId };
            var tagZen = new Tag { Name = $"善_{tid}", OwnerId = ownerId };
            var tagCustom = new Tag { Name = $"カスタム_{tid}", OwnerId = ownerId };
            dbContext.Tags.AddRange(tagShin, tagZen, tagCustom);
            await dbContext.SaveChangesAsync();

            var originalItem = new Item
            {
                OwnerId = ownerId,
                Content = $"前半_[複合タグ対象_{tid}]_後半"
            };
            dbContext.Items.Add(originalItem);
            await dbContext.SaveChangesAsync();

            var commentItem = new Item
            {
                OwnerId = requesterId,
                Content = "真タグへのコメント"
            };
            dbContext.Items.Add(commentItem);
            await dbContext.SaveChangesAsync();

            var quoteItem = new Item
            {
                OwnerId = requesterId,
                Content = $"複合タグ対象_{tid}",
                QuotedItemId = originalItem.Id
            };
            dbContext.Items.Add(quoteItem);
            await dbContext.SaveChangesAsync();

            // 引用アイテムに複数のタグを付与
            dbContext.TagRelations.AddRange(
                new TagRelation { TagId = tagShin.Id, ItemId = quoteItem.Id, Weight = 1, OwnerId = requesterId, CommentItemId = commentItem.Id },
                new TagRelation { TagId = tagZen.Id, ItemId = quoteItem.Id, Weight = 1, OwnerId = requesterId },
                new TagRelation { TagId = tagCustom.Id, ItemId = quoteItem.Id, Weight = 5, OwnerId = requesterId }
            );
            await dbContext.SaveChangesAsync();

            var request = new ItemSplitRequest
            {
                OriginalItem = originalItem,
                RequesterUserId = requesterId,
                OwnerUserId = ownerId,
                SelectedText = $"複合タグ対象_{tid}",
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
            if (result is not Success<Item> success)
            {
                return;
            }
            Item newItem = success.Value;

            dbContext.ChangeTracker.Clear();

            var migratedTags = await dbContext.TagRelations
                .Where(r => r.ItemId == newItem.Id)
                .ToListAsync();

            Assert.Equal(3, migratedTags.Count);

            var shinRel = migratedTags.Single(r => r.TagId == tagShin.Id);
            Assert.Equal(1, shinRel.Weight);
            Assert.Equal(commentItem.Id, shinRel.CommentItemId);

            var zenRel = migratedTags.Single(r => r.TagId == tagZen.Id);
            Assert.Equal(1, zenRel.Weight);

            var customRel = migratedTags.Single(r => r.TagId == tagCustom.Id);
            Assert.Equal(5, customRel.Weight);

            // 引用アイテム自体は削除されていること
            Assert.Null(await dbContext.Items.FindAsync(quoteItem.Id));
        }
    }

    [Fact]
    public async Task SplitDirectlyAsync_WhenSelectedTextOccursMultipleTimes_ReplacesFirstOccurrence()
    {
        var (dbContext, service, _, tid) = CreateScope();
        await using (dbContext)
        {
            var ownerId = $"owner_{tid}";
            await dbContext.SeedUsersAsync(ownerId);

            var originalItem = new Item
            {
                OwnerId = ownerId,
                Content = "あいうえお_ターゲット_かきくけこ_ターゲット_さしすせそ"
            };
            dbContext.Items.Add(originalItem);
            await dbContext.SaveChangesAsync();

            // Act
            Result<Item> result = await service.SplitDirectlyAsync(originalItem.Id, "ターゲット", ownerId);

            // Assert
            Assert.True(result is Success<Item>);
            if (result is not Success<Item> success)
            {
                return;
            }
            Item newItem = success.Value;

            dbContext.ChangeTracker.Clear();
            var reloadedOriginal = await dbContext.Items.FindAsync(originalItem.Id);
            Assert.NotNull(reloadedOriginal);

            // 最初の「ターゲット」のみがリンクに置換され、2番目の「ターゲット」はそのまま残る
            string expectedContent = $"あいうえお_/ItemDetail/{newItem.Id}_かきくけこ_ターゲット_さしすせそ";
            Assert.Equal(expectedContent, reloadedOriginal.Content);
        }
    }

    [Fact]
    public async Task ApproveSplitAsync_WhenSelectedTextOccursMultipleTimes_ReplacesFirstOccurrence()
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
                Content = "開始_重複キーワード_中間_重複キーワード_終了"
            };
            dbContext.Items.Add(originalItem);
            await dbContext.SaveChangesAsync();

            var request = new ItemSplitRequest
            {
                OriginalItem = originalItem,
                RequesterUserId = requesterId,
                OwnerUserId = ownerId,
                SelectedText = "重複キーワード",
                Status = TradeStatus.Proposed,
                OwnerId = requesterId
            };
            dbContext.ItemSplitRequests.Add(request);
            await dbContext.SaveChangesAsync();

            // Act
            Result<Item> result = await service.ApproveSplitAsync(request.Id, ownerId);

            // Assert
            Assert.True(result is Success<Item>);
            if (result is not Success<Item> success)
            {
                return;
            }
            Item newItem = success.Value;

            dbContext.ChangeTracker.Clear();
            var reloadedOriginal = await dbContext.Items.FindAsync(originalItem.Id);
            Assert.NotNull(reloadedOriginal);

            string expectedContent = $"開始_/ItemDetail/{newItem.Id}_中間_重複キーワード_終了";
            Assert.Equal(expectedContent, reloadedOriginal.Content);
        }
    }

    [Fact]
    public async Task SplitDirectlyAsync_WhenEntireContentIsSelected_ReplacesWithLinkOnly()
    {
        var (dbContext, service, _, tid) = CreateScope();
        await using (dbContext)
        {
            var ownerId = $"owner_{tid}";
            await dbContext.SeedUsersAsync(ownerId);

            var originalItem = new Item
            {
                OwnerId = ownerId,
                Content = "すべてを選択して分割するテキスト"
            };
            dbContext.Items.Add(originalItem);
            await dbContext.SaveChangesAsync();

            // Act
            Result<Item> result = await service.SplitDirectlyAsync(originalItem.Id, "すべてを選択して分割するテキスト", ownerId);

            // Assert
            Assert.True(result is Success<Item>);
            if (result is not Success<Item> success)
            {
                return;
            }
            Item newItem = success.Value;

            dbContext.ChangeTracker.Clear();
            var reloadedOriginal = await dbContext.Items.FindAsync(originalItem.Id);
            Assert.NotNull(reloadedOriginal);

            Assert.Equal($"/ItemDetail/{newItem.Id}", reloadedOriginal.Content);
            Assert.Equal("すべてを選択して分割するテキスト", newItem.Content);
        }
    }

    [Fact]
    public async Task ApproveSplitAsync_WithNestedReplyChainOnQuoteItem_MigratesAllRepliesHierarchy()
    {
        var (dbContext, service, _, tid) = CreateScope();
        await using (dbContext)
        {
            var ownerId = $"owner_{tid}";
            var requesterId = $"requester_{tid}";
            var user1 = $"user1_{tid}";
            var user2 = $"user2_{tid}";
            await dbContext.SeedUsersAsync(ownerId, requesterId, user1, user2);

            var originalItem = new Item
            {
                OwnerId = ownerId,
                Content = $"親アイテムの本文_{tid}。分割ターゲットテキスト_{tid}。末尾"
            };
            dbContext.Items.Add(originalItem);
            await dbContext.SaveChangesAsync();

            var quoteItem = new Item
            {
                OwnerId = requesterId,
                Content = $"分割ターゲットテキスト_{tid}",
                QuotedItemId = originalItem.Id
            };
            dbContext.Items.Add(quoteItem);
            await dbContext.SaveChangesAsync();

            // 直下のリプライ
            var reply1 = new Item
            {
                OwnerId = user1,
                Content = $"直下のリプライ_{tid}",
                ParentItemId = quoteItem.Id,
                RootItemId = quoteItem.Id
            };
            dbContext.Items.Add(reply1);
            await dbContext.SaveChangesAsync();

            // ネストした子リプライ (Parent=reply1, Root=quoteItem)
            var reply2 = new Item
            {
                OwnerId = user2,
                Content = $"ネストしたリプライ_{tid}",
                ParentItemId = reply1.Id,
                RootItemId = quoteItem.Id
            };
            dbContext.Items.Add(reply2);
            await dbContext.SaveChangesAsync();

            var request = new ItemSplitRequest
            {
                OriginalItemId = originalItem.Id,
                RequesterUserId = requesterId,
                OwnerUserId = ownerId,
                SelectedText = $"分割ターゲットテキスト_{tid}",
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
            if (result is not Success<Item> success)
            {
                return;
            }
            Item newItem = success.Value;

            dbContext.ChangeTracker.Clear();
            var reloadedReply1 = await dbContext.Items.FindAsync(reply1.Id);
            var reloadedReply2 = await dbContext.Items.FindAsync(reply2.Id);
            var reloadedQuoteItem = await dbContext.Items.FindAsync(quoteItem.Id);

            Assert.NotNull(reloadedReply1);
            Assert.NotNull(reloadedReply2);
            Assert.Null(reloadedQuoteItem); // 引用アイテム自体は削除

            // reply1 の親とルートが新アイテムへ更新されていること
            Assert.Equal(newItem.Id, reloadedReply1.ParentItemId);
            Assert.Equal(newItem.Id, reloadedReply1.RootItemId);

            // reply2 の親は reply1 のままで、ルートが新アイテムへ更新されていること
            Assert.Equal(reply1.Id, reloadedReply2.ParentItemId);
            Assert.Equal(newItem.Id, reloadedReply2.RootItemId);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public async Task SplitDirectlyAsync_WhenOriginalItemIdZeroOrNegative_ReturnsFailure(int invalidItemId)
    {
        var (dbContext, service, _, _) = CreateScope();
        await using (dbContext)
        {
            var result = await service.SplitDirectlyAsync(invalidItemId, "テキスト", "owner");

            Assert.True(result is Failure);
            if (result is Failure failure)
            {
                Assert.Equal(SRNSMudApp.Resources.ErrorMessages.SplitTargetItemRequired, failure.ErrorMessage);
            }
        }
    }

    [Fact]
    public async Task ApproveSplitAsync_WithEmojiAndSpecialCharactersInSelectedText_Succeeds()
    {
        var (dbContext, service, _, tid) = CreateScope();
        await using (dbContext)
        {
            var ownerId = $"owner_{tid}";
            var requesterId = $"requester_{tid}";
            await dbContext.SeedUsersAsync(ownerId, requesterId);

            const string specialText = "✨【重要な概念 #1】🔥";
            var originalItem = new Item
            {
                OwnerId = ownerId,
                Content = $"導入部_{tid}。" + specialText + $"。まとめ_{tid}"
            };
            dbContext.Items.Add(originalItem);
            await dbContext.SaveChangesAsync();

            var quoteItem = new Item
            {
                OwnerId = requesterId,
                Content = specialText,
                QuotedItemId = originalItem.Id
            };
            dbContext.Items.Add(quoteItem);
            await dbContext.SaveChangesAsync();

            var request = new ItemSplitRequest
            {
                OriginalItemId = originalItem.Id,
                RequesterUserId = requesterId,
                OwnerUserId = ownerId,
                SelectedText = specialText,
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
            if (result is not Success<Item> success)
            {
                return;
            }
            Item newItem = success.Value;
            Assert.Equal(specialText, newItem.Content);

            dbContext.ChangeTracker.Clear();
            var reloadedOriginal = await dbContext.Items.FindAsync(originalItem.Id);
            Assert.NotNull(reloadedOriginal);
            Assert.Equal($"導入部_{tid}。/ItemDetail/{newItem.Id}。まとめ_{tid}", reloadedOriginal.Content);
        }
    }

    [Fact]
    public async Task GetPendingSplitRequestsForOriginalItemAsync_FiltersStrictlyByOriginalItemId()
    {
        var (dbContext, service, _, tid) = CreateScope();
        await using (dbContext)
        {
            var ownerId = $"owner_{tid}";
            var reqUser = $"requester_{tid}";
            await dbContext.SeedUsersAsync(ownerId, reqUser);

            var item1 = new Item { OwnerId = ownerId, Content = $"アイテム1_{tid}" };
            var item2 = new Item { OwnerId = ownerId, Content = $"アイテム2_{tid}" };
            dbContext.Items.AddRange(item1, item2);
            await dbContext.SaveChangesAsync();

            var req1 = new ItemSplitRequest
            {
                OriginalItemId = item1.Id,
                RequesterUserId = reqUser,
                OwnerUserId = ownerId,
                SelectedText = "アイテム1",
                Status = TradeStatus.Proposed,
                OwnerId = reqUser
            };
            var req2 = new ItemSplitRequest
            {
                OriginalItemId = item2.Id,
                RequesterUserId = reqUser,
                OwnerUserId = ownerId,
                SelectedText = "アイテム2",
                Status = TradeStatus.Proposed,
                OwnerId = reqUser
            };
            var req3 = new ItemSplitRequest
            {
                OriginalItemId = item1.Id,
                RequesterUserId = reqUser,
                OwnerUserId = ownerId,
                SelectedText = "アイテム1完了済",
                Status = TradeStatus.Executed,
                OwnerId = reqUser
            };
            dbContext.ItemSplitRequests.AddRange(req1, req2, req3);
            await dbContext.SaveChangesAsync();

            // Act: item1 に対する保留中リクエストのみ取得
            var results = await service.GetPendingSplitRequestsForOriginalItemAsync(item1.Id);

            // Assert: item1 の Proposed のみ返却される（req1 のみ）
            Assert.Single(results);
            Assert.Equal(req1.Id, results[0].Id);
        }
    }

    [Fact]
    public async Task GetSplitRequestByIdAsync_WhenNotFound_ReturnsNull()
    {
        var (dbContext, service, _, _) = CreateScope();
        await using (dbContext)
        {
            var result = await service.GetSplitRequestByIdAsync(999999);

            Assert.Null(result);
        }
    }

    [Fact]
    public async Task RequestSplitAsync_WhenCancellationTokenCanceled_ThrowsOperationCanceledException()
    {
        var (dbContext, service, _, _) = CreateScope();
        await using (dbContext)
        {
            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                service.RequestSplitAsync(1, "テキスト", "user", null, cts.Token));
        }
    }

    [Fact]
    public async Task ApproveSplitAsync_WithTagWeightLedgerOnQuoteItem_MigratesLedgerAndCommentItemToNewItem()
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
                Content = $"本文先頭_{tid} 分割対象_{tid} 本文末尾_{tid}"
            };
            var quoteItem = new Item
            {
                OwnerId = requesterId,
                Content = $"分割対象_{tid}"
            };
            dbContext.Items.AddRange(originalItem, quoteItem);

            var tag = new Tag { Name = $"Tag_{tid}", OwnerId = requesterId };
            dbContext.Tags.Add(tag);
            await dbContext.SaveChangesAsync();

            // リアクション・タグ履歴コメント用の TagCommentItem を作成（仕様どおり quoteItem とは別の CommentItem エンティティ）
            var reactionCommentItem = new Item
            {
                OwnerId = requesterId,
                Content = $"リアクション理由コメント_{tid}",
                ItemKindJson = System.Text.Json.JsonSerializer.Serialize(new TagCommentItem(quoteItem.Id, tag.Id))
            };
            dbContext.Items.Add(reactionCommentItem);
            await dbContext.SaveChangesAsync();

            // quoteItem に対する TagRelation と TagWeightLedger を作成
            var tagRelation = new TagRelation
            {
                ItemId = quoteItem.Id,
                TagId = tag.Id,
                OwnerId = requesterId,
                Weight = 2,
                CommentItemId = reactionCommentItem.Id
            };
            dbContext.TagRelations.Add(tagRelation);
            await dbContext.SaveChangesAsync();

            var ledger = new TagWeightLedger
            {
                TagId = tag.Id,
                ItemId = quoteItem.Id,
                SourceType = "TagRelationInsert",
                SourceId = tagRelation.Id,
                CommentItemId = reactionCommentItem.Id,
                Delta = 2,
                PreviousWeight = 0,
                NewWeight = 2,
                OwnerId = requesterId
            };
            dbContext.TagWeightLedgers.Add(ledger);

            var request = new ItemSplitRequest
            {
                OriginalItemId = originalItem.Id,
                RequesterUserId = requesterId,
                OwnerUserId = ownerId,
                SelectedText = $"分割対象_{tid}",
                RequesterQuoteItemId = quoteItem.Id,
                Status = TradeStatus.Proposed,
                OwnerId = requesterId
            };
            dbContext.ItemSplitRequests.Add(request);
            await dbContext.SaveChangesAsync();

            // Act: 承認
            Result<Item> result = await service.ApproveSplitAsync(request.Id, ownerId);

            // Assert
            var newItem = result switch
            {
                Success<Item> s => s.Value,
                Failure f => throw new Xunit.Sdk.XunitException($"Expected Success<Item> but got Failure: {f.ErrorMessage}"),
                _ => throw new Xunit.Sdk.XunitException("Unknown Result variant")
            };

            dbContext.ChangeTracker.Clear();

            // quoteItem は削除されている
            Assert.Null(await dbContext.Items.FindAsync(quoteItem.Id));

            // 新アイテム上の TagRelation が作成され、CommentItemId を引き継いでいること
            var newRelation = await dbContext.TagRelations
                .FirstOrDefaultAsync(tr => tr.ItemId == newItem.Id && tr.TagId == tag.Id);
            Assert.NotNull(newRelation);
            Assert.Equal(reactionCommentItem.Id, newRelation.CommentItemId);

            // TagWeightLedger の ItemId と SourceId（新しい TagRelation.Id）が新アイテム側へ付け替えられ、CommentItemId が維持されていること
            var updatedLedger = await dbContext.TagWeightLedgers.FindAsync(ledger.Id);
            Assert.NotNull(updatedLedger);
            Assert.Equal(newItem.Id, updatedLedger.ItemId);
            Assert.Equal(newRelation.Id, updatedLedger.SourceId);
            Assert.Equal(reactionCommentItem.Id, updatedLedger.CommentItemId);

            // CommentItem の ItemKindJson (TagCommentItem.TargetItemId) も newItem.Id に更新されていること
            var updatedCommentItem = await dbContext.Items.FindAsync(reactionCommentItem.Id);
            Assert.NotNull(updatedCommentItem);
            var commentMeta = System.Text.Json.JsonSerializer.Deserialize<TagCommentItem>(updatedCommentItem.ItemKindJson);
            Assert.NotNull(commentMeta);
            Assert.Equal(newItem.Id, commentMeta.TargetItemId);
            Assert.Equal(tag.Id, commentMeta.TagId);
        }
    }

    [Fact]
    public async Task ApproveSplitAsync_WithCommentItemAndTaggingRequestAndContentReport_MigratesAllRelations()
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
                Content = $"本文先頭_{tid} 分割対象_{tid} 本文末尾_{tid}"
            };
            var quoteItem = new Item
            {
                OwnerId = requesterId,
                Content = $"分割対象_{tid}"
            };
            var otherItem = new Item
            {
                OwnerId = ownerId,
                Content = $"別アイテム_{tid}"
            };
            dbContext.Items.AddRange(originalItem, quoteItem, otherItem);

            var tag = new Tag { Name = $"Tag_{tid}", OwnerId = requesterId };
            dbContext.Tags.Add(tag);
            await dbContext.SaveChangesAsync();

            // 1. 他の TagRelation の CommentItemId として quoteItem を参照
            var otherRelation = new TagRelation
            {
                ItemId = otherItem.Id,
                TagId = tag.Id,
                OwnerId = ownerId,
                Weight = 1,
                CommentItemId = quoteItem.Id
            };
            dbContext.TagRelations.Add(otherRelation);

            // 2. TaggingRequestEntity (バウンティ) の RequestItemId として quoteItem を参照
            var tagTarget = new TaggableTarget { OwnerId = ownerId };
            dbContext.TaggableTargets.Add(tagTarget);
            await dbContext.SaveChangesAsync();

            var bounty = new TaggingRequestEntity
            {
                TargetId = tagTarget.Id,
                RequestedTagId = tag.Id,
                RequestItemId = quoteItem.Id,
                OwnerId = requesterId
            };
            dbContext.TaggingRequestEntities.Add(bounty);

            // 3. ContentReport の ItemId として quoteItem を参照
            var report = new ContentReport
            {
                ItemId = quoteItem.Id,
                Reason = "テスト通報",
                OwnerId = requesterId
            };
            dbContext.ContentReports.Add(report);

            var request = new ItemSplitRequest
            {
                OriginalItemId = originalItem.Id,
                RequesterUserId = requesterId,
                OwnerUserId = ownerId,
                SelectedText = $"分割対象_{tid}",
                RequesterQuoteItemId = quoteItem.Id,
                Status = TradeStatus.Proposed,
                OwnerId = requesterId
            };
            dbContext.ItemSplitRequests.Add(request);
            await dbContext.SaveChangesAsync();

            // Act: 承認
            Result<Item> result = await service.ApproveSplitAsync(request.Id, ownerId);

            // Assert: FK Restrict 例外を起こさず成功すること
            var newItem = result switch
            {
                Success<Item> s => s.Value,
                Failure f => throw new Xunit.Sdk.XunitException($"Expected Success<Item> but got Failure: {f.ErrorMessage}"),
                _ => throw new Xunit.Sdk.XunitException("Unknown Result variant")
            };

            dbContext.ChangeTracker.Clear();

            // quoteItem は削除されている
            Assert.Null(await dbContext.Items.FindAsync(quoteItem.Id));

            // 各種外部キーが新アイテムへ付け替えられている
            var updatedRelation = await dbContext.TagRelations.FindAsync(otherRelation.Id);
            Assert.NotNull(updatedRelation);
            Assert.Equal(newItem.Id, updatedRelation.CommentItemId);

            var updatedBounty = await dbContext.TaggingRequestEntities.FindAsync(bounty.Id);
            Assert.NotNull(updatedBounty);
            Assert.Equal(newItem.Id, updatedBounty.RequestItemId);

            var updatedReport = await dbContext.ContentReports.FindAsync(report.Id);
            Assert.NotNull(updatedReport);
            Assert.Equal(newItem.Id, updatedReport.ItemId);
        }
    }

    [Fact]
    public async Task ApproveSplitAsync_WithTimelineEventAndConversationOptOut_MigratesToNewItem()
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
                Content = $"本文先頭_{tid} 分割対象_{tid} 本文末尾_{tid}"
            };
            var quoteItem = new Item
            {
                OwnerId = requesterId,
                Content = $"分割対象_{tid}"
            };
            dbContext.Items.AddRange(originalItem, quoteItem);

            var tag = new Tag { Name = $"Tag_{tid}", OwnerId = requesterId };
            dbContext.Tags.Add(tag);
            await dbContext.SaveChangesAsync();

            // TimelineEvent
            var te = new TimelineEvent
            {
                OwnerId = requesterId,
                Target = new ItemTarget(quoteItem.Id),
                FollowedTagId = tag.Id,
                EventType = "Insert"
            };
            dbContext.TimelineEvents.Add(te);

            // ConversationOptOut
            var optOut = new ConversationOptOut
            {
                UserId = requesterId,
                RootItemId = quoteItem.Id
            };
            dbContext.ConversationOptOuts.Add(optOut);

            var request = new ItemSplitRequest
            {
                OriginalItemId = originalItem.Id,
                RequesterUserId = requesterId,
                OwnerUserId = ownerId,
                SelectedText = $"分割対象_{tid}",
                RequesterQuoteItemId = quoteItem.Id,
                Status = TradeStatus.Proposed,
                OwnerId = requesterId
            };
            dbContext.ItemSplitRequests.Add(request);
            await dbContext.SaveChangesAsync();

            // Act: 承認
            Result<Item> result = await service.ApproveSplitAsync(request.Id, ownerId);

            // Assert
            var newItem = result switch
            {
                Success<Item> s => s.Value,
                Failure f => throw new Xunit.Sdk.XunitException($"Expected Success<Item> but got Failure: {f.ErrorMessage}"),
                _ => throw new Xunit.Sdk.XunitException("Unknown Result variant")
            };

            dbContext.ChangeTracker.Clear();

            // TimelineEvent の Target が新アイテムへ更新されている
            var updatedTe = await dbContext.TimelineEvents.FindAsync(te.Id);
            Assert.NotNull(updatedTe);
            Assert.True(updatedTe.Target is ItemTarget it && it.TargetItemId == newItem.Id);

            // ConversationOptOut の RootItemId が新アイテムへ更新されている
            var updatedOptOut = await dbContext.ConversationOptOuts.FindAsync(optOut.Id);
            Assert.NotNull(updatedOptOut);
            Assert.Equal(newItem.Id, updatedOptOut.RootItemId);
        }
    }

    [Fact]
    public async Task ApproveSplitAsync_WithQuotedItemsAndDeadLinksAndBountyTarget_MigratesAllContentAndBounties()
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
                Content = $"本文先頭_{tid} 分割対象_{tid} 本文末尾_{tid}"
            };
            var quoteItem = new Item
            {
                OwnerId = requesterId,
                Content = $"分割対象_{tid}"
            };
            dbContext.Items.AddRange(originalItem, quoteItem);
            await dbContext.SaveChangesAsync();

            // 1. quoteItem を引用したアイテム (QuotedItemId, ItemKindJson, 本文にリンク)
            var quotingItem = new Item
            {
                OwnerId = requesterId,
                QuotedItemId = quoteItem.Id,
                ItemKindJson = System.Text.Json.JsonSerializer.Serialize(new QuoteItem(quoteItem.Id)),
                Content = $"引用コメント_{tid} /ItemDetail/{quoteItem.Id} および /Item/WasmDetail/{quoteItem.Id} 補足"
            };

            // 2. 第三のアイテム本文内のリンク (デッドリンク防止)
            var thirdItem = new Item
            {
                OwnerId = ownerId,
                Content = $"第三者の言及: /ItemDetail/{quoteItem.Id} と /Item/WasmDetail/{quoteItem.Id} を参照"
            };

            // 3. バウンティ（TaggingRequestEntity）が quoteItem の TagTarget を対象にしている
            var tag = new Tag { Name = $"BountyTag_{tid}", OwnerId = requesterId };
            dbContext.Tags.Add(tag);
            await dbContext.SaveChangesAsync();

            var bounty = new TaggingRequestEntity
            {
                TargetId = quoteItem.TagTargetId,
                RequestedTagId = tag.Id,
                OwnerId = requesterId
            };

            var replyToBounty = new TaggingRequestEntity
            {
                TargetId = quoteItem.TagTargetId,
                RequestedTagId = tag.Id,
                OwnerId = ownerId
            };

            dbContext.Items.AddRange(quotingItem, thirdItem);
            dbContext.TaggingRequestEntities.AddRange(bounty, replyToBounty);
            await dbContext.SaveChangesAsync();

            // quoteItem 自体が別のバウンティの成果物リプライだった場合
            quoteItem.TaggingRequestEntityId = replyToBounty.Id;

            var request = new ItemSplitRequest
            {
                OriginalItemId = originalItem.Id,
                RequesterUserId = requesterId,
                OwnerUserId = ownerId,
                SelectedText = $"分割対象_{tid}",
                RequesterQuoteItemId = quoteItem.Id,
                Status = TradeStatus.Proposed,
                OwnerId = requesterId
            };
            dbContext.ItemSplitRequests.Add(request);
            await dbContext.SaveChangesAsync();

            // Act: 承認
            Result<Item> result = await service.ApproveSplitAsync(request.Id, ownerId);

            // Assert
            var newItem = result switch
            {
                Success<Item> s => s.Value,
                Failure f => throw new Xunit.Sdk.XunitException($"Expected Success<Item> but got Failure: {f.ErrorMessage}"),
                _ => throw new Xunit.Sdk.XunitException("Unknown Result variant")
            };

            dbContext.ChangeTracker.Clear();

            // quoteItem は削除されている
            Assert.Null(await dbContext.Items.FindAsync(quoteItem.Id));

            // quotingItem の QuotedItemId, ItemKindJson, 本文リンクが newItem.Id に更新されていること
            var updatedQuoting = await dbContext.Items.FindAsync(quotingItem.Id);
            Assert.NotNull(updatedQuoting);
            Assert.Equal(newItem.Id, updatedQuoting.QuotedItemId);
            Assert.Contains($"/ItemDetail/{newItem.Id}", updatedQuoting.Content);
            Assert.DoesNotContain($"/ItemDetail/{quoteItem.Id}", updatedQuoting.Content);
            Assert.Contains($"/Item/WasmDetail/{newItem.Id}", updatedQuoting.Content);
            Assert.DoesNotContain($"/Item/WasmDetail/{quoteItem.Id}", updatedQuoting.Content);
            var quoteMeta = System.Text.Json.JsonSerializer.Deserialize<QuoteItem>(updatedQuoting.ItemKindJson);
            Assert.NotNull(quoteMeta);
            Assert.Equal(newItem.Id, quoteMeta.QuotedItemId);

            // thirdItem の本文リンクが更新されていること
            var updatedThird = await dbContext.Items.FindAsync(thirdItem.Id);
            Assert.NotNull(updatedThird);
            Assert.Contains($"/ItemDetail/{newItem.Id}", updatedThird.Content);
            Assert.DoesNotContain($"/ItemDetail/{quoteItem.Id}", updatedThird.Content);
            Assert.Contains($"/Item/WasmDetail/{newItem.Id}", updatedThird.Content);
            Assert.DoesNotContain($"/Item/WasmDetail/{quoteItem.Id}", updatedThird.Content);

            // バウンティの TargetId が newItem.TagTargetId に更新されていること
            var updatedBounty = await dbContext.TaggingRequestEntities.FindAsync(bounty.Id);
            Assert.NotNull(updatedBounty);
            Assert.Equal(newItem.TagTargetId, updatedBounty.TargetId);

            // newItem が成果物リプライIDを引き継いでいること
            var updatedNewItem = await dbContext.Items.FindAsync(newItem.Id);
            Assert.NotNull(updatedNewItem);
            Assert.Equal(replyToBounty.Id, updatedNewItem.TaggingRequestEntityId);
        }
    }

    [Fact]
    public async Task RequestSplitAsync_WithMismatchedQuoteItem_ReturnsFailure()
    {
        var (dbContext, service, _, tid) = CreateScope();
        await using (dbContext)
        {
            var ownerId = $"owner_{tid}";
            var requesterId = $"requester_{tid}";
            await dbContext.SeedUsersAsync(ownerId, requesterId);

            var originalItem = new Item { OwnerId = ownerId, Content = $"Original_{tid} Target" };
            var otherItem = new Item { OwnerId = ownerId, Content = $"Other_{tid}" };
            dbContext.Items.AddRange(originalItem, otherItem);
            await dbContext.SaveChangesAsync();

            var quoteItem = new Item
            {
                OwnerId = requesterId,
                Content = $"Quote_{tid}",
                QuotedItemId = otherItem.Id
            };
            dbContext.Items.Add(quoteItem);
            await dbContext.SaveChangesAsync();

            Result<ItemSplitRequest> result = await service.RequestSplitAsync(
                originalItem.Id, "Target", requesterId, requesterQuoteItemId: quoteItem.Id);

            Assert.True(result is Failure);
            var failure = result switch
            {
                Failure f => f,
                _ => throw new Xunit.Sdk.XunitException("Expected Failure")
            };
            Assert.Contains(ErrorMessages.SplitQuoteItemNotFound, failure.ErrorMessage);
        }
    }

    [Fact]
    public async Task ApproveSplitAsync_WithMutualContract_MigratesOfferedTargetItemId()
    {
        var (dbContext, service, _, tid) = CreateScope();
        await using (dbContext)
        {
            var ownerId = $"owner_{tid}";
            var requesterId = $"requester_{tid}";
            await dbContext.SeedUsersAsync(ownerId, requesterId);

            var originalItem = new Item { OwnerId = ownerId, Content = $"Original_{tid} [SplitTarget]" };
            dbContext.Items.Add(originalItem);
            await dbContext.SaveChangesAsync();

            var quoteItem = new Item
            {
                OwnerId = requesterId,
                Content = $"Quote_{tid}",
                QuotedItemId = originalItem.Id
            };
            dbContext.Items.Add(quoteItem);

            var tag = new Tag { Name = $"tag_{tid}", OwnerId = requesterId };
            dbContext.Tags.Add(tag);
            await dbContext.SaveChangesAsync();

            // 相互提案コントラクト（OfferedTargetItemId に quoteItem.Id が指定されている）
            var mutualContract = new TaggingRequestEntity
            {
                TargetId = originalItem.TagTargetId,
                RequestedTagId = tag.Id,
                OwnerId = requesterId,
                ContractType = ContractTypes.Mutual,
                Payload = new MutualPayload(quoteItem.Id, tag.Id)
            };
            dbContext.TaggingRequestEntities.Add(mutualContract);

            var request = new ItemSplitRequest
            {
                OriginalItemId = originalItem.Id,
                RequesterUserId = requesterId,
                OwnerUserId = ownerId,
                SelectedText = "[SplitTarget]",
                RequesterQuoteItemId = quoteItem.Id,
                Status = TradeStatus.Proposed,
                OwnerId = requesterId
            };
            dbContext.ItemSplitRequests.Add(request);
            await dbContext.SaveChangesAsync();

            // Act
            Result<Item> result = await service.ApproveSplitAsync(request.Id, ownerId);

            // Assert
            Assert.True(result is Success<Item>);
            var newItem = result switch
            {
                Success<Item> s => s.Value,
                _ => throw new Xunit.Sdk.XunitException("Expected Success<Item>")
            };

            dbContext.ChangeTracker.Clear();
            var updatedContract = await dbContext.TaggingRequestEntities.FindAsync(mutualContract.Id);
            Assert.NotNull(updatedContract);
            Assert.Equal(ContractTypes.Mutual, updatedContract.ContractType);
            var mutual = updatedContract.Payload switch
            {
                MutualPayload m => m,
                _ => throw new Xunit.Sdk.XunitException("Expected MutualPayload")
            };
            Assert.Equal(newItem.Id, mutual.OfferedTargetItemId);
        }
    }

    [Fact]
    public async Task RequestSplitAsync_WithCrlfDuplicate_ReturnsFailure()
    {
        var (dbContext, service, _, tid) = CreateScope();
        await using (dbContext)
        {
            var ownerId = $"owner_{tid}";
            var requesterId = $"requester_{tid}";
            await dbContext.SeedUsersAsync(ownerId, requesterId);

            var originalItem = new Item { OwnerId = ownerId, Content = $"Line1_{tid}\r\nLine2_{tid}\r\nLine3_{tid}" };
            dbContext.Items.Add(originalItem);
            await dbContext.SaveChangesAsync();

            // 1回目: \r\n 形式でリクエスト
            Result<ItemSplitRequest> res1 = await service.RequestSplitAsync(originalItem.Id, $"Line1_{tid}\r\nLine2_{tid}", requesterId);
            Assert.True(res1 is Success<ItemSplitRequest>);

            // 2回目: \n 形式でリクエスト（改行コード正規化により重複として弾かれる）
            Result<ItemSplitRequest> res2 = await service.RequestSplitAsync(originalItem.Id, $"Line1_{tid}\nLine2_{tid}", requesterId);
            Assert.True(res2 is Failure);
            var failure = res2 switch
            {
                Failure f => f,
                _ => throw new Xunit.Sdk.XunitException("Expected Failure")
            };
            Assert.Contains(ErrorMessages.SplitDuplicateRequestExists, failure.ErrorMessage);
        }
    }

    [Fact]
    public async Task RequestSplitAsync_WhenIntersectsExistingLink_ReturnsFailure()
    {
        var (dbContext, service, _, tid) = CreateScope();
        await using (dbContext)
        {
            var ownerId = $"owner_{tid}";
            var requesterId = $"requester_{tid}";
            await dbContext.SeedUsersAsync(ownerId, requesterId);

            var originalItem = new Item { OwnerId = ownerId, Content = $"Start_{tid} /ItemDetail/12345 End_{tid}" };
            dbContext.Items.Add(originalItem);
            await dbContext.SaveChangesAsync();

            // リンクの途中を横断する選択
            Result<ItemSplitRequest> result = await service.RequestSplitAsync(originalItem.Id, $"_{tid} /ItemDe", requesterId);
            Assert.True(result is Failure);
            var failure = result switch
            {
                Failure f => f,
                _ => throw new Xunit.Sdk.XunitException("Expected Failure")
            };
            Assert.Contains("既存のリンク文字列の途中で分割することはできません。", failure.ErrorMessage);
        }
    }

    [Fact]
    public async Task RequestSplitAsync_WhenExceedsMaxContentLength_ReturnsFailure()
    {
        var (dbContext, service, _, tid) = CreateScope();
        await using (dbContext)
        {
            var ownerId = $"owner_{tid}";
            var requesterId = $"requester_{tid}";
            await dbContext.SeedUsersAsync(ownerId, requesterId);

            // 995文字のアイテムを作成
            string prefix = new('a', 990);
            var originalItem = new Item { OwnerId = ownerId, Content = $"{prefix}12345" };
            dbContext.Items.Add(originalItem);
            await dbContext.SaveChangesAsync();

            // "12345"(5文字)を分割しようとすると、リンク置換 ("/ItemDetail/10000000" 等) によって1000文字を超える
            Result<ItemSplitRequest> result = await service.RequestSplitAsync(originalItem.Id, "12345", requesterId);
            Assert.True(result is Failure);
            var failure = result switch
            {
                Failure f => f,
                _ => throw new Xunit.Sdk.XunitException("Expected Failure")
            };
            Assert.Contains("分割後のアイテム本文が最大文字数（1000文字）を超えるため、分割できません。", failure.ErrorMessage);
        }
    }
}