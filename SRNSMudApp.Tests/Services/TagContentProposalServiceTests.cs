using Microsoft.EntityFrameworkCore;

using Moq;

using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;
using SRNSMudApp.Tests.TestSupport;

namespace SRNSMudApp.Tests.Services;

public class TagContentProposalServiceTests : IAsyncLifetime
{
    private MsSqlTestDatabase _sharedDb = null!;

    public async Task InitializeAsync()
    {
        _sharedDb = await SharedMsSqlTestDatabase.GetInstanceAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private (ApplicationDbContext dbContext, TagContentProposalService service, Mock<INotificationService> mockNotificationService, Mock<ITagEmbeddingService> mockEmbeddingService, string tid) CreateScope()
    {
        var tid = Guid.NewGuid().ToString("N")[..8];
        var dbContext = new ApplicationDbContext(_sharedDb.Options);
        var mockDbFactory = new Mock<IDbContextFactory<ApplicationDbContext>>();
        mockDbFactory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new ApplicationDbContext(_sharedDb.Options));

        var mockNotificationService = new Mock<INotificationService>();
        var mockEmbeddingService = new Mock<ITagEmbeddingService>();
        mockEmbeddingService.Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>()))
            .ReturnsAsync(new ReadOnlyMemory<float>([0.1f, 0.2f]));

        var service = new TagContentProposalService(
            mockDbFactory.Object,
            mockNotificationService.Object,
            mockEmbeddingService.Object);

        return (dbContext, service, mockNotificationService, mockEmbeddingService, tid);
    }

    [Fact]
    public async Task ProposeContentAsync_Success_CreatesProposal()
    {
        var (dbContext, service, mockNotification, _, tid) = CreateScope();
        await using (dbContext)
        {
            var ownerId = $"owner_{tid}";
            var requesterId = $"requester_{tid}";
            await dbContext.SeedUsersAsync(ownerId, requesterId);

            var tag = new Tag
            {
                Name = $"TestTag_{tid}",
                Content = "元々の説明文",
                OwnerId = ownerId
            };
            dbContext.Tags.Add(tag);
            await dbContext.SaveChangesAsync();

            // Act
            Result<TagContentProposal> result = await service.ProposeContentAsync(
                tag.Id,
                "提案された新しい説明文",
                "誤字の修正と補足",
                requesterId);

            // Assert
            Assert.True(result is Success<TagContentProposal>);
            switch (result)
            {
                case Success<TagContentProposal> success:
                    Assert.Equal(tag.Id, success.Value.TagId);
                    Assert.Equal(requesterId, success.Value.RequesterUserId);
                    Assert.Equal(ownerId, success.Value.OwnerUserId);
                    Assert.Equal("提案された新しい説明文", success.Value.ProposedContent);
                    Assert.Equal("誤字の修正と補足", success.Value.Reason);
                    Assert.Equal(TradeStatus.Proposed, success.Value.Status);
                    break;
            }

            mockNotification.Verify(n => n.NotifyNotificationsChanged(), Times.Once);
        }
    }

    [Fact]
    public async Task ProposeContentAsync_Fails_WhenContentIsNull()
    {
        var (dbContext, service, _, _, tid) = CreateScope();
        await using (dbContext)
        {
            Result<TagContentProposal> result = await service.ProposeContentAsync(1, null!, "理由", $"user_{tid}");
            Assert.True(result is Failure);
        }
    }

    [Fact]
    public async Task ProposeContentAsync_Fails_WhenTagNotFound()
    {
        var (dbContext, service, _, _, tid) = CreateScope();
        await using (dbContext)
        {
            Result<TagContentProposal> result = await service.ProposeContentAsync(999999, "新しい内容", "理由", $"user_{tid}");
            Assert.True(result is Failure);
            switch (result)
            {
                case Failure failure:
                    Assert.Contains("対象のタグが見つかりません", failure.ErrorMessage);
                    break;
            }
        }
    }

    [Fact]
    public async Task ProposeContentAsync_Fails_WhenOwnTag()
    {
        var (dbContext, service, _, _, tid) = CreateScope();
        await using (dbContext)
        {
            var ownerId = $"owner_{tid}";
            await dbContext.SeedUsersAsync(ownerId);

            var tag = new Tag
            {
                Name = $"OwnTag_{tid}",
                Content = "元々の説明文",
                OwnerId = ownerId
            };
            dbContext.Tags.Add(tag);
            await dbContext.SaveChangesAsync();

            Result<TagContentProposal> result = await service.ProposeContentAsync(tag.Id, "新しい内容", "理由", ownerId);
            Assert.True(result is Failure);
            switch (result)
            {
                case Failure failure:
                    Assert.Contains("自分自身のタグに対しては編集提案ではなく直接編集", failure.ErrorMessage);
                    break;
            }
        }
    }

    [Fact]
    public async Task ProposeContentAsync_Fails_WhenContentIsUnchanged()
    {
        var (dbContext, service, _, _, tid) = CreateScope();
        await using (dbContext)
        {
            var ownerId = $"owner_{tid}";
            var requesterId = $"requester_{tid}";
            await dbContext.SeedUsersAsync(ownerId, requesterId);

            var tag = new Tag
            {
                Name = $"SameTag_{tid}",
                Content = "現在の説明文",
                OwnerId = ownerId
            };
            dbContext.Tags.Add(tag);
            await dbContext.SaveChangesAsync();

            Result<TagContentProposal> result = await service.ProposeContentAsync(tag.Id, "現在の説明文", "理由", requesterId);
            Assert.True(result is Failure);
            switch (result)
            {
                case Failure failure:
                    Assert.Contains("提案内容が現在のタグの内容と同じです", failure.ErrorMessage);
                    break;
            }
        }
    }

    [Fact]
    public async Task ProposeContentAsync_Fails_WhenDuplicateProposedExists()
    {
        var (dbContext, service, _, _, tid) = CreateScope();
        await using (dbContext)
        {
            var ownerId = $"owner_{tid}";
            var requesterId = $"requester_{tid}";
            await dbContext.SeedUsersAsync(ownerId, requesterId);

            var tag = new Tag
            {
                Name = $"DupTag_{tid}",
                Content = "元の内容",
                OwnerId = ownerId
            };
            dbContext.Tags.Add(tag);
            await dbContext.SaveChangesAsync();

            _ = await service.ProposeContentAsync(tag.Id, "新内容", "理由1", requesterId);
            Result<TagContentProposal> result2 = await service.ProposeContentAsync(tag.Id, "新内容", "理由2", requesterId);

            Assert.True(result2 is Failure);
            switch (result2)
            {
                case Failure failure:
                    Assert.Contains("既に申請中です", failure.ErrorMessage);
                    break;
            }
        }
    }

    [Fact]
    public async Task ApproveProposalAsync_Success_UpdatesTagContentAndStatus()
    {
        var (dbContext, service, mockNotification, mockEmbedding, tid) = CreateScope();
        await using (dbContext)
        {
            var ownerId = $"owner_{tid}";
            var requesterId = $"requester_{tid}";
            await dbContext.SeedUsersAsync(ownerId, requesterId);

            var tag = new Tag
            {
                Name = $"ApproveTag_{tid}",
                Content = "変更前の内容",
                OwnerId = ownerId
            };
            dbContext.Tags.Add(tag);
            await dbContext.SaveChangesAsync();

            Result<TagContentProposal> proposeResult = await service.ProposeContentAsync(
                tag.Id,
                "承認されるべき新しい内容",
                "更新の提案",
                requesterId);

            Assert.True(proposeResult is Success<TagContentProposal>);
            var proposalId = proposeResult is Success<TagContentProposal> s ? s.Value.Id : 0;

            // Act: オーナーが承認
            Result<Tag> approveResult = await service.ApproveProposalAsync(proposalId, ownerId);

            // Assert
            Assert.True(approveResult is Success<Tag>);
            switch (approveResult)
            {
                case Success<Tag> success:
                    Assert.Equal("承認されるべき新しい内容", success.Value.Content);
                    break;
            }

            // DB上の提案エンティティを確認
            var updatedProposal = await dbContext.TagContentProposals.FindAsync(proposalId);
            Assert.NotNull(updatedProposal);
            Assert.Equal(TradeStatus.Executed, updatedProposal.Status);

            mockEmbedding.Verify(e => e.GenerateEmbeddingAsync(tag.Name), Times.Once);
            mockNotification.Verify(n => n.NotifyNotificationsChanged(), Times.Exactly(2));
        }
    }

    [Fact]
    public async Task ApproveProposalAsync_Fails_WhenNotOwner()
    {
        var (dbContext, service, _, _, tid) = CreateScope();
        await using (dbContext)
        {
            var ownerId = $"owner_{tid}";
            var requesterId = $"requester_{tid}";
            var otherUserId = $"other_{tid}";
            await dbContext.SeedUsersAsync(ownerId, requesterId, otherUserId);

            var tag = new Tag
            {
                Name = $"NotOwnerTag_{tid}",
                Content = "内容",
                OwnerId = ownerId
            };
            dbContext.Tags.Add(tag);
            await dbContext.SaveChangesAsync();

            var proposeResult = await service.ProposeContentAsync(tag.Id, "新しい内容", null, requesterId);
            var proposalId = proposeResult is Success<TagContentProposal> s ? s.Value.Id : 0;

            // Act: 他ユーザーが承認を試みる
            Result<Tag> result = await service.ApproveProposalAsync(proposalId, otherUserId);

            Assert.True(result is Failure);
            switch (result)
            {
                case Failure failure:
                    Assert.Contains("承認する権限がありません", failure.ErrorMessage);
                    break;
            }
        }
    }

    [Fact]
    public async Task RejectProposalAsync_Success_UpdatesStatusAndReason()
    {
        var (dbContext, service, mockNotification, _, tid) = CreateScope();
        await using (dbContext)
        {
            var ownerId = $"owner_{tid}";
            var requesterId = $"requester_{tid}";
            await dbContext.SeedUsersAsync(ownerId, requesterId);

            var tag = new Tag
            {
                Name = $"RejectTag_{tid}",
                Content = "元の内容",
                OwnerId = ownerId
            };
            dbContext.Tags.Add(tag);
            await dbContext.SaveChangesAsync();

            var proposeResult = await service.ProposeContentAsync(tag.Id, "新しい内容", null, requesterId);
            var proposalId = proposeResult is Success<TagContentProposal> s ? s.Value.Id : 0;

            // Act: オーナーが却下
            Result<bool> rejectResult = await service.RejectProposalAsync(proposalId, ownerId, "現状の内容で十分です");

            Assert.True(rejectResult is Success<bool>);

            var updatedProposal = await dbContext.TagContentProposals.FindAsync(proposalId);
            Assert.NotNull(updatedProposal);
            Assert.Equal(TradeStatus.Rejected, updatedProposal.Status);
            Assert.Equal("現状の内容で十分です", updatedProposal.RejectReason);

            mockNotification.Verify(n => n.NotifyNotificationsChanged(), Times.Exactly(2));
        }
    }

    [Fact]
    public async Task CancelProposalAsync_Success_UpdatesStatus()
    {
        var (dbContext, service, mockNotification, _, tid) = CreateScope();
        await using (dbContext)
        {
            var ownerId = $"owner_{tid}";
            var requesterId = $"requester_{tid}";
            await dbContext.SeedUsersAsync(ownerId, requesterId);

            var tag = new Tag
            {
                Name = $"CancelTag_{tid}",
                Content = "元の内容",
                OwnerId = ownerId
            };
            dbContext.Tags.Add(tag);
            await dbContext.SaveChangesAsync();

            var proposeResult = await service.ProposeContentAsync(tag.Id, "新しい内容", null, requesterId);
            var proposalId = proposeResult is Success<TagContentProposal> s ? s.Value.Id : 0;

            // Act: 提案者が取り下げ
            Result<bool> cancelResult = await service.CancelProposalAsync(proposalId, requesterId);

            Assert.True(cancelResult is Success<bool>);

            var updatedProposal = await dbContext.TagContentProposals.FindAsync(proposalId);
            Assert.NotNull(updatedProposal);
            Assert.Equal(TradeStatus.Canceled, updatedProposal.Status);

            mockNotification.Verify(n => n.NotifyNotificationsChanged(), Times.Exactly(2));
        }
    }

    [Fact]
    public async Task GetPendingProposalsForTagAsync_ReturnsOnlyProposed()
    {
        var (dbContext, service, _, _, tid) = CreateScope();
        await using (dbContext)
        {
            var ownerId = $"owner_{tid}";
            var requesterId = $"requester_{tid}";
            await dbContext.SeedUsersAsync(ownerId, requesterId);

            var tag = new Tag
            {
                Name = $"PendingTag_{tid}",
                Content = "元の内容",
                OwnerId = ownerId
            };
            dbContext.Tags.Add(tag);
            await dbContext.SaveChangesAsync();

            // 1つ目: Proposed
            _ = await service.ProposeContentAsync(tag.Id, "提案1", null, requesterId);

            // 2つ目: Reject
            var propose2 = await service.ProposeContentAsync(tag.Id, "提案2", null, requesterId);
            var id2 = propose2 is Success<TagContentProposal> s ? s.Value.Id : 0;
            _ = await service.RejectProposalAsync(id2, ownerId, null);

            // Act
            IReadOnlyList<TagContentProposal> pending = await service.GetPendingProposalsForTagAsync(tag.Id);

            // Assert
            Assert.Single(pending);
            Assert.Equal("提案1", pending[0].ProposedContent);
        }
    }
}