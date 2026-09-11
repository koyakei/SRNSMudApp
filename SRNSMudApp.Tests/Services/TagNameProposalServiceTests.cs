using Microsoft.EntityFrameworkCore;

using Moq;

using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;
using SRNSMudApp.Tests.TestSupport;

namespace SRNSMudApp.Tests.Services;

public class TagNameProposalServiceTests : IAsyncLifetime
{
    private MsSqlTestDatabase _sharedDb = null!;

    public async Task InitializeAsync()
    {
        _sharedDb = await SharedMsSqlTestDatabase.GetInstanceAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private (ApplicationDbContext dbContext, TagNameProposalService service, Mock<INotificationService> mockNotificationService, Mock<ITagEmbeddingService> mockEmbeddingService, string tid) CreateScope()
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

        var service = new TagNameProposalService(
            mockDbFactory.Object,
            mockNotificationService.Object,
            mockEmbeddingService.Object);

        return (dbContext, service, mockNotificationService, mockEmbeddingService, tid);
    }

    [Fact]
    public async Task ProposeNameAsync_Success_CreatesProposal()
    {
        var (dbContext, service, mockNotification, _, tid) = CreateScope();
        await using (dbContext)
        {
            var ownerId = $"owner_{tid}";
            var requesterId = $"requester_{tid}";
            await dbContext.SeedUsersAsync(ownerId, requesterId);

            var tag = new Tag
            {
                Name = $"OldName_{tid}",
                OwnerId = ownerId
            };
            dbContext.Tags.Add(tag);
            await dbContext.SaveChangesAsync();

            // Act
            Result<TagNameProposal> result = await service.ProposeNameAsync(
                tag.Id,
                $"NewName_{tid}",
                "より適切な名前への変更",
                requesterId);

            // Assert
            Assert.True(result is Success<TagNameProposal>);
            switch (result)
            {
                case Success<TagNameProposal> success:
                    Assert.Equal(tag.Id, success.Value.TagId);
                    Assert.Equal(requesterId, success.Value.RequesterUserId);
                    Assert.Equal(ownerId, success.Value.OwnerUserId);
                    Assert.Equal($"NewName_{tid}", success.Value.ProposedName);
                    Assert.Equal("より適切な名前への変更", success.Value.Reason);
                    Assert.Equal(TradeStatus.Proposed, success.Value.Status);
                    break;
            }

            mockNotification.Verify(n => n.NotifyNotificationsChanged(), Times.Once);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ProposeNameAsync_Fails_WhenNameIsWhitespace(string? proposedName)
    {
        var (dbContext, service, _, _, tid) = CreateScope();
        await using (dbContext)
        {
            Result<TagNameProposal> result = await service.ProposeNameAsync(1, proposedName!, "理由", $"user_{tid}");
            Assert.True(result is Failure);
            switch (result)
            {
                case Failure failure:
                    Assert.Contains("提案するタグ名が指定されていません", failure.ErrorMessage);
                    break;
            }
        }
    }

    [Fact]
    public async Task ProposeNameAsync_Fails_WhenNameExceeds100Characters()
    {
        var (dbContext, service, _, _, tid) = CreateScope();
        await using (dbContext)
        {
            var tooLongName = new string('A', 101);
            Result<TagNameProposal> result = await service.ProposeNameAsync(1, tooLongName, "理由", $"user_{tid}");
            Assert.True(result is Failure);
            switch (result)
            {
                case Failure failure:
                    Assert.Contains("100文字以内", failure.ErrorMessage);
                    break;
            }
        }
    }

    [Fact]
    public async Task ProposeNameAsync_Fails_WhenNameContainsInvalidCharacters()
    {
        var (dbContext, service, _, _, tid) = CreateScope();
        await using (dbContext)
        {
            // 改行や制御文字は正規表現で不許可
            Result<TagNameProposal> result = await service.ProposeNameAsync(1, "不正な\n名前", "理由", $"user_{tid}");
            Assert.True(result is Failure);
            switch (result)
            {
                case Failure failure:
                    Assert.Contains("のみ使用できます", failure.ErrorMessage);
                    break;
            }
        }
    }

    [Theory]
    [InlineData("good")]
    [InlineData("bad")]
    [InlineData("真実")]
    [InlineData("善")]
    [InlineData("美")]
    [InlineData("全て∀")]
    public async Task ProposeNameAsync_Fails_WhenNameIsReserved(string reservedName)
    {
        var (dbContext, service, _, _, tid) = CreateScope();
        await using (dbContext)
        {
            Result<TagNameProposal> result = await service.ProposeNameAsync(1, reservedName, "理由", $"user_{tid}");
            Assert.True(result is Failure);
            switch (result)
            {
                case Failure failure:
                    Assert.Contains("予約されているシステムタグ名", failure.ErrorMessage);
                    break;
            }
        }
    }

    [Fact]
    public async Task ProposeNameAsync_Fails_WhenTagNotFound()
    {
        var (dbContext, service, _, _, tid) = CreateScope();
        await using (dbContext)
        {
            Result<TagNameProposal> result = await service.ProposeNameAsync(999999, $"NewTag_{tid}", "理由", $"user_{tid}");
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
    public async Task ProposeNameAsync_Fails_WhenSystemTag()
    {
        var (dbContext, service, _, _, tid) = CreateScope();
        await using (dbContext)
        {
            var ownerId = $"owner_{tid}";
            await dbContext.SeedUsersAsync(ownerId);

            var systemTag = new Tag
            {
                Name = $"SysTag_{tid}",
                OwnerId = ownerId,
                IsSystem = true
            };
            dbContext.Tags.Add(systemTag);
            await dbContext.SaveChangesAsync();

            Result<TagNameProposal> result = await service.ProposeNameAsync(systemTag.Id, $"NewSysTag_{tid}", "理由", $"user_{tid}");
            Assert.True(result is Failure);
            switch (result)
            {
                case Failure failure:
                    Assert.Contains("ルートタグやシステムタグの名前変更提案はできません", failure.ErrorMessage);
                    break;
            }
        }
    }

    [Fact]
    public async Task ProposeNameAsync_Fails_WhenOwnTag()
    {
        var (dbContext, service, _, _, tid) = CreateScope();
        await using (dbContext)
        {
            var ownerId = $"owner_{tid}";
            await dbContext.SeedUsersAsync(ownerId);

            var tag = new Tag
            {
                Name = $"OwnTag_{tid}",
                OwnerId = ownerId
            };
            dbContext.Tags.Add(tag);
            await dbContext.SaveChangesAsync();

            Result<TagNameProposal> result = await service.ProposeNameAsync(tag.Id, $"NewName_{tid}", "理由", ownerId);
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
    public async Task ProposeNameAsync_Fails_WhenNameIsSame()
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
                OwnerId = ownerId
            };
            dbContext.Tags.Add(tag);
            await dbContext.SaveChangesAsync();

            Result<TagNameProposal> result = await service.ProposeNameAsync(tag.Id, $"SameTag_{tid}", "理由", requesterId);
            Assert.True(result is Failure);
            switch (result)
            {
                case Failure failure:
                    Assert.Contains("提案された名前が現在のタグ名と同じです", failure.ErrorMessage);
                    break;
            }
        }
    }

    [Fact]
    public async Task ProposeNameAsync_Fails_WhenOwnerAlreadyHasTagWithProposedName()
    {
        var (dbContext, service, _, _, tid) = CreateScope();
        await using (dbContext)
        {
            var ownerId = $"owner_{tid}";
            var requesterId = $"requester_{tid}";
            await dbContext.SeedUsersAsync(ownerId, requesterId);

            var tag1 = new Tag { Name = $"TagOne_{tid}", OwnerId = ownerId };
            var tag2 = new Tag { Name = $"TagTwo_{tid}", OwnerId = ownerId };
            dbContext.Tags.AddRange(tag1, tag2);
            await dbContext.SaveChangesAsync();

            Result<TagNameProposal> result = await service.ProposeNameAsync(tag1.Id, $"TagTwo_{tid}", "理由", requesterId);
            Assert.True(result is Failure);
            switch (result)
            {
                case Failure failure:
                    Assert.Contains("タグの所有者は既に同名のタグを所持しています", failure.ErrorMessage);
                    break;
            }
        }
    }

    [Fact]
    public async Task ProposeNameAsync_Fails_WhenDuplicateProposalExists()
    {
        var (dbContext, service, _, _, tid) = CreateScope();
        await using (dbContext)
        {
            var ownerId = $"owner_{tid}";
            var requesterId = $"requester_{tid}";
            await dbContext.SeedUsersAsync(ownerId, requesterId);

            var tag = new Tag { Name = $"TagBase_{tid}", OwnerId = ownerId };
            dbContext.Tags.Add(tag);
            await dbContext.SaveChangesAsync();

            _ = await service.ProposeNameAsync(tag.Id, $"NewName_{tid}", "理由1", requesterId);
            Result<TagNameProposal> result2 = await service.ProposeNameAsync(tag.Id, $"NewName_{tid}", "理由2", requesterId);

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
    public async Task ApproveProposalAsync_Success_UpdatesTagNameAndStatus()
    {
        var (dbContext, service, mockNotification, mockEmbedding, tid) = CreateScope();
        await using (dbContext)
        {
            var ownerId = $"owner_{tid}";
            var requesterId = $"requester_{tid}";
            await dbContext.SeedUsersAsync(ownerId, requesterId);

            var tag = new Tag
            {
                Name = $"BeforeApprove_{tid}",
                OwnerId = ownerId
            };
            dbContext.Tags.Add(tag);
            await dbContext.SaveChangesAsync();

            Result<TagNameProposal> proposeResult = await service.ProposeNameAsync(
                tag.Id,
                $"AfterApprove_{tid}",
                "承認希望",
                requesterId);

            Assert.True(proposeResult is Success<TagNameProposal>);
            var proposalId = proposeResult is Success<TagNameProposal> s ? s.Value.Id : 0;

            // Act: オーナーが承認
            Result<Tag> approveResult = await service.ApproveProposalAsync(proposalId, ownerId);

            // Assert
            Assert.True(approveResult is Success<Tag>);
            switch (approveResult)
            {
                case Success<Tag> success:
                    Assert.Equal($"AfterApprove_{tid}", success.Value.Name);
                    break;
            }

            var updatedProposal = await dbContext.TagNameProposals.FindAsync(proposalId);
            Assert.NotNull(updatedProposal);
            Assert.Equal(TradeStatus.Executed, updatedProposal.Status);

            mockEmbedding.Verify(e => e.GenerateEmbeddingAsync($"AfterApprove_{tid}"), Times.Once);
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
                OwnerId = ownerId
            };
            dbContext.Tags.Add(tag);
            await dbContext.SaveChangesAsync();

            var proposeResult = await service.ProposeNameAsync(tag.Id, $"NewName_{tid}", null, requesterId);
            var proposalId = proposeResult is Success<TagNameProposal> s ? s.Value.Id : 0;

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
    public async Task ApproveProposalAsync_Fails_WhenNameConflictOccursAtApproval()
    {
        var (dbContext, service, _, _, tid) = CreateScope();
        await using (dbContext)
        {
            var ownerId = $"owner_{tid}";
            var requesterId = $"requester_{tid}";
            await dbContext.SeedUsersAsync(ownerId, requesterId);

            var tag = new Tag
            {
                Name = $"ConflictBefore_{tid}",
                OwnerId = ownerId
            };
            dbContext.Tags.Add(tag);
            await dbContext.SaveChangesAsync();

            var proposeResult = await service.ProposeNameAsync(tag.Id, $"ConflictTarget_{tid}", null, requesterId);
            var proposalId = proposeResult is Success<TagNameProposal> s ? s.Value.Id : 0;

            // 提案後にオーナーが別のタグで ConflictTarget_tid という名前を作成した状況を再現
            var anotherTag = new Tag
            {
                Name = $"ConflictTarget_{tid}",
                OwnerId = ownerId
            };
            dbContext.Tags.Add(anotherTag);
            await dbContext.SaveChangesAsync();

            // Act: 承認を試みる
            Result<Tag> result = await service.ApproveProposalAsync(proposalId, ownerId);

            // Assert: 衝突ガードにより失敗
            Assert.True(result is Failure);
            switch (result)
            {
                case Failure failure:
                    Assert.Contains("同名のタグが既に存在するため承認できません", failure.ErrorMessage);
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
                OwnerId = ownerId
            };
            dbContext.Tags.Add(tag);
            await dbContext.SaveChangesAsync();

            var proposeResult = await service.ProposeNameAsync(tag.Id, $"NewName_{tid}", null, requesterId);
            var proposalId = proposeResult is Success<TagNameProposal> s ? s.Value.Id : 0;

            // Act: オーナーが却下
            Result<bool> rejectResult = await service.RejectProposalAsync(proposalId, ownerId, "現状の名前が最適です");

            Assert.True(rejectResult is Success<bool>);

            var updatedProposal = await dbContext.TagNameProposals.FindAsync(proposalId);
            Assert.NotNull(updatedProposal);
            Assert.Equal(TradeStatus.Rejected, updatedProposal.Status);
            Assert.Equal("現状の名前が最適です", updatedProposal.RejectReason);

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
                OwnerId = ownerId
            };
            dbContext.Tags.Add(tag);
            await dbContext.SaveChangesAsync();

            var proposeResult = await service.ProposeNameAsync(tag.Id, $"NewName_{tid}", null, requesterId);
            var proposalId = proposeResult is Success<TagNameProposal> s ? s.Value.Id : 0;

            // Act: 提案者が取り下げ
            Result<bool> cancelResult = await service.CancelProposalAsync(proposalId, requesterId);

            Assert.True(cancelResult is Success<bool>);

            var updatedProposal = await dbContext.TagNameProposals.FindAsync(proposalId);
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
                OwnerId = ownerId
            };
            dbContext.Tags.Add(tag);
            await dbContext.SaveChangesAsync();

            // 1つ目: Proposed
            _ = await service.ProposeNameAsync(tag.Id, $"Name1_{tid}", null, requesterId);

            // 2つ目: Reject
            var propose2 = await service.ProposeNameAsync(tag.Id, $"Name2_{tid}", null, requesterId);
            var id2 = propose2 is Success<TagNameProposal> s ? s.Value.Id : 0;
            _ = await service.RejectProposalAsync(id2, ownerId, null);

            // Act
            IReadOnlyList<TagNameProposal> pending = await service.GetPendingProposalsForTagAsync(tag.Id);

            // Assert
            Assert.Single(pending);
            Assert.Equal($"Name1_{tid}", pending[0].ProposedName);
        }
    }
}