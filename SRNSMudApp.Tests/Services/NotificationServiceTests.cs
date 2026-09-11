using Moq;

using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;

namespace SRNSMudApp.Tests.Services;

/// <summary>
///     NotificationService の DTO 変換および内部ヘルパーロジックを検証する単体テスト。
///     DB に依存しない純粋なドメイン変換処理を高速かつ網羅的に検証する。
/// </summary>
public class NotificationServiceTests
{
    [Theory]
    [InlineData(TaggingRequestType.Add, "追加")]
    [InlineData(TaggingRequestType.DecreaseWeight, "削除")]
    [InlineData(TaggingRequestType.Move, "移動")]
    [InlineData(null, "不明")]
    public void GetRequestTypeLabel_ReturnsExpectedJapaneseText(TaggingRequestType? type, string expected)
    {
        var result = NotificationService.GetRequestTypeLabel(type);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void BuildTagRequestNotifications_MapsPropertiesCorrectly_WhenMoveRequest()
    {
        // Arrange
        List<TaggingRequestEntity> requests =
        [
            new()
            {
                Id = 20,
                RequestType = TaggingRequestType.Move,
                TargetItemId = 100,
                RequestedTagId = 200,
                CreatedDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                OwnerId = "userA",
                RequestedTag = new Tag { Name = "Rust", OwnerId = "userB" },
                RequesterUserId = "userA",
                TagOwnerUserId = "userB"
            }
        ];

        List<NotificationReadState> readStates = [];

        // Act
        List<NotificationDto> dtos = [.. NotificationService.BuildTagRequestNotifications(requests, readStates)];

        // Assert
        Assert.Single(dtos);
        NotificationDto dto = dtos[0];
        Assert.Equal(20, dto.SourceId);
        Assert.Equal("/tag-tree?tagId=200", dto.TargetUrl.ToHref());
        Assert.Contains("Rustの移動リクエストが届いています。", dto.Message);
        Assert.True(dto.Kind is TagRequestNotification);
    }

    [Fact]
    public void IsRead_ReturnsTrue_WhenMatchingReadStateExists()
    {
        List<NotificationReadState> states =
        [
            new() { SourceId = 1, SourceType = "TagRequest", UserId = "u1" },
            new() { SourceId = 2, SourceType = "RequestRejected", UserId = "u1" }
        ];

        Assert.True(NotificationService.IsRead(states, 1, "TagRequest"));
        Assert.True(NotificationService.IsRead(states, 2, "RequestRejected"));
        Assert.False(NotificationService.IsRead(states, 3, "TagRequest"));
        Assert.False(NotificationService.IsRead(states, 1, "OtherType"));
    }

    [Fact]
    public void BuildTagRequestNotifications_MapsPropertiesCorrectly()
    {
        // Arrange
        List<TaggingRequestEntity> requests =
        [
            new()
            {
                Id = 10,
                RequestType = TaggingRequestType.Add,
                TargetItemId = 100,
                RequestedTagId = 200,
                ProposedWeight = 2,
                CreatedDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                OwnerId = "userA",
                RequestedTag = new Tag { Name = "Rust", OwnerId = "system" },
                RequesterUserId = "userA",
                TagOwnerUserId = "userB"
            }
        ];

        List<NotificationReadState> readStates =
        [
            new() { SourceId = 10, SourceType = "TagRequest", UserId = "userB" }
        ];

        // Act
        List<NotificationDto> dtos = [.. NotificationService.BuildTagRequestNotifications(requests, readStates)];

        // Assert
        Assert.Single(dtos);
        NotificationDto dto = dtos[0];
        Assert.Equal(10, dto.SourceId);
        Assert.True(dto.IsRead);
        Assert.Equal("/ItemDetail/100", dto.TargetUrl.ToHref());
        Assert.Contains("Rustの追加リクエストが届いています。", dto.Message);
        Assert.True(dto.Kind is TagRequestNotification);
    }

    [Fact]
    public void BuildRejectedRequestNotifications_IncludesComment_WhenPresent()
    {
        // Arrange

        var req = new TaggingRequestEntity
        {
            Id = 11,
            RequestType = TaggingRequestType.Add,
            TargetItemId = 101,
            RequestedTagId = 201,
            UpdatedDate = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            OwnerId = "userA",
            RequestedTag = new Tag { Name = "Go", OwnerId = "system" },
            RequesterUserId = "userA",
            TagOwnerUserId = "userB"
        };
        req.Reject(new SRNSMudApp.Models.Unions.RejectionReason("不適切なタグ付けです。"));
        List<TaggingRequestEntity> requests = [req];

        List<NotificationReadState> readStates = [];

        // Act
        List<NotificationDto> dtos = [.. NotificationService.BuildRejectedRequestNotifications(requests, readStates)];

        // Assert
        Assert.Single(dtos);
        NotificationDto dto = dtos[0];
        Assert.Equal(11, dto.SourceId);
        Assert.False(dto.IsRead);
        Assert.Contains("理由: 不適切なタグ付けです。", dto.Message);
        Assert.True(dto.Kind is RequestRejectedNotification);
    }

    [Fact]
    public void BuildApprovedRequestNotifications_MapsPropertiesCorrectly()
    {
        // Arrange

        var req = new TaggingRequestEntity
        {
            Id = 12,
            RequestType = TaggingRequestType.Add,
            TargetItemId = 102,
            RequestedTagId = 202,
            UpdatedDate = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc),
            OwnerId = "userA",
            RequestedTag = new Tag { Name = "C#", OwnerId = "system" },
            RequesterUserId = "userA",
            TagOwnerUserId = "userB"
        };
        req.Execute();
        List<TaggingRequestEntity> requests = [req];

        List<NotificationReadState> readStates = [];

        // Act
        List<NotificationDto> dtos = [.. NotificationService.BuildApprovedRequestNotifications(requests, readStates)];

        // Assert
        Assert.Single(dtos);
        NotificationDto dto = dtos[0];
        Assert.Equal(12, dto.SourceId);
        Assert.Contains("C#の追加リクエストが承認されました。", dto.Message);
        Assert.True(dto.Kind is RequestApprovedNotification);
    }

    [Fact]
    public void BuildReplyNotifications_MapsItemReplyCorrectly()
    {
        // Arrange
        List<Item> replies =
        [
            new()
            {
                Id = 50,
                ParentItemId = 20,
                Content = "返信本文",
                OwnerId = "userC",
                Owner = new ApplicationUser { UserName = "Alice" },
                CreatedDate = new DateTime(2026, 1, 4, 0, 0, 0, DateTimeKind.Utc)
            }
        ];

        List<NotificationReadState> readStates = [];

        // Act
        List<NotificationDto> dtos = [.. NotificationService.BuildReplyNotifications(replies, readStates, "ItemReply")];

        // Assert
        Assert.Single(dtos);
        NotificationDto dto = dtos[0];
        Assert.Equal(50, dto.SourceId);
        Assert.Equal("Alice", dto.ActorName);
        Assert.Equal("/ItemDetail/50", dto.TargetUrl.ToHref());
        Assert.True(dto.Kind is ItemReplyNotification);
    }

    [Fact]
    public void BuildReplyNotifications_WhenUserIsThreadParticipant_UsesParticipatingItemMessage()
    {
        // Arrange
        List<Item> replies =
        [
            new()
            {
                Id = 51,
                ParentItemId = 20,
                ParentItem = new Item { Id = 20, OwnerId = "userOriginalAuthor" },
                Content = "返信本文",
                OwnerId = "userC",
                Owner = new ApplicationUser { UserName = "Charlie" },
                CreatedDate = new DateTime(2026, 1, 4, 0, 0, 0, DateTimeKind.Utc)
            }
        ];

        List<NotificationReadState> readStates = [];

        // Act
        List<NotificationDto> dtos = [.. NotificationService.BuildReplyNotifications(replies, readStates, "ItemReply", "userReplierBob")];

        // Assert
        Assert.Single(dtos);
        NotificationDto dto = dtos[0];
        Assert.Contains("Charlieさんがあなたの参加しているアイテムにリプライしました。", dto.Message);
    }

    [Fact]
    public void BuildReplyNotifications_WhenUserIsParentOwner_UsesItemMessage()
    {
        // Arrange
        List<Item> replies =
        [
            new()
            {
                Id = 52,
                ParentItemId = 20,
                ParentItem = new Item { Id = 20, OwnerId = "userAlice" },
                Content = "返信本文",
                OwnerId = "userB",
                Owner = new ApplicationUser { UserName = "Bob" },
                CreatedDate = new DateTime(2026, 1, 4, 0, 0, 0, DateTimeKind.Utc)
            }
        ];

        List<NotificationReadState> readStates = [];

        // Act
        List<NotificationDto> dtos = [.. NotificationService.BuildReplyNotifications(replies, readStates, "ItemReply", "userAlice")];

        // Assert
        Assert.Single(dtos);
        NotificationDto dto = dtos[0];
        Assert.Contains("Bobさんがあなたのアイテムにリプライしました。", dto.Message);
    }

    [Fact]
    public async Task GetUserNotificationsAsync_CallsDataProvider_AndAggregatesInDescendingOrder()
    {
        // Arrange
        var mockProvider = new Moq.Mock<INotificationsDataProvider>();
        var rawData = new NotificationRawData(
            TagRequests:
            [
                new TaggingRequestEntity
                {
                    Id = 1,
                    RequestType = TaggingRequestType.Add,
                    TargetItemId = 10,
                    RequestedTagId = 20,
                    ProposedWeight = 1,
                    CreatedDate = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc),
                    OwnerId = "user1",
                    RequestedTag = new Tag { Name = "Tag1", OwnerId = "user2" },
                    RequesterUserId = "user1",
                    TagOwnerUserId = "user2"
                }
            ],
            ItemReplies: [],
            RejectedRequests: [],
            ApprovedRequests: [],
            RequestReplies: [],
            ReadStates: []
        );

        mockProvider.Setup(p => p.GetNotificationRawDataAsync("user2", Moq.It.IsAny<CancellationToken>()))
            .ReturnsAsync(rawData);

        var service = new NotificationService(mockProvider.Object);

        // Act
        IReadOnlyList<NotificationDto> results = await service.GetUserNotificationsAsync("user2");

        // Assert
        Assert.Single(results);
        Assert.Equal(1, results[0].SourceId);
        mockProvider.Verify(p => p.GetNotificationRawDataAsync("user2", Moq.It.IsAny<CancellationToken>()), Moq.Times.Once);
    }

    [Fact]
    public async Task MarkAsReadAsync_DelegatesToDataProvider_AndFiresEvent()
    {
        // Arrange
        var mockProvider = new Moq.Mock<INotificationsDataProvider>();
        var service = new NotificationService(mockProvider.Object);
        var eventFired = false;
        service.NotificationsChanged += (_, _) => eventFired = true;

        // Act
        await service.MarkAsReadAsync("user1", 42, "TagRequest");

        // Assert
        mockProvider.Verify(p => p.MarkAsReadAsync("user1", 42, "TagRequest", Moq.It.IsAny<CancellationToken>()), Moq.Times.Once);
        Assert.True(eventFired);
    }

    [Fact]
    public async Task MarkAllAsReadAsync_WithUnreadItems_DelegatesToDataProvider_AndFiresEvent()
    {
        // Arrange
        var mockProvider = new Moq.Mock<INotificationsDataProvider>();
        var rawData = new NotificationRawData(
            TagRequests:
            [
                new TaggingRequestEntity
                {
                    Id = 10,
                    OwnerId = "other",
                    RequestedTagId = 1,
                    TargetItemId = 1,
                    RequesterUserId = "other",
                    ProposedWeight = 1,
                    CreatedDate = DateTime.UtcNow
                }
            ],
            ItemReplies: [],
            RejectedRequests: [],
            ApprovedRequests: [],
            RequestReplies: [],
            ReadStates: [] // 未読
        );
        mockProvider.Setup(p => p.GetNotificationRawDataAsync("user1", Moq.It.IsAny<CancellationToken>()))
            .ReturnsAsync(rawData);

        var service = new NotificationService(mockProvider.Object);
        var eventFired = false;
        service.NotificationsChanged += (_, _) => eventFired = true;

        // Act
        await service.MarkAllAsReadAsync("user1");

        // Assert
        mockProvider.Verify(p => p.MarkAllAsReadAsync(
            "user1",
            Moq.It.Is<IEnumerable<(int SourceId, string SourceType)>>(items => items.Count() == 1 && items.First().SourceId == 10),
            Moq.It.IsAny<CancellationToken>()), Moq.Times.Once);
        Assert.True(eventFired);
    }

    [Fact]
    public async Task MarkAllAsReadAsync_WhenAllRead_DoesNotCallDataProvider()
    {
        // Arrange
        var mockProvider = new Moq.Mock<INotificationsDataProvider>();
        var rawData = new NotificationRawData(
            TagRequests:
            [
                new TaggingRequestEntity
                {
                    Id = 10,
                    OwnerId = "other",
                    RequestedTagId = 1,
                    TargetItemId = 1,
                    RequesterUserId = "other",
                    ProposedWeight = 1,
                    CreatedDate = DateTime.UtcNow
                }
            ],
            ItemReplies: [],
            RejectedRequests: [],
            ApprovedRequests: [],
            RequestReplies: [],
            ReadStates: [new NotificationReadState { SourceId = 10, SourceType = "TagRequest", UserId = "user1" }] // 既読
        );
        mockProvider.Setup(p => p.GetNotificationRawDataAsync("user1", Moq.It.IsAny<CancellationToken>()))
            .ReturnsAsync(rawData);

        var service = new NotificationService(mockProvider.Object);
        var eventFired = false;
        service.NotificationsChanged += (_, _) => eventFired = true;

        // Act
        await service.MarkAllAsReadAsync("user1");

        // Assert
        mockProvider.Verify(p => p.MarkAllAsReadAsync(
            "user1",
            Moq.It.IsAny<IEnumerable<(int SourceId, string SourceType)>>(),
            Moq.It.IsAny<CancellationToken>()), Moq.Times.Never);
        Assert.False(eventFired);
    }

    [Fact]
    public void BuildReportResolvedNotifications_MapsPropertiesCorrectly()
    {
        // Arrange
        var report1 = new ContentReport
        {
            Id = 15,
            TargetType = ReportTargetType.Item,
            ItemId = 101,
            OwnerId = "reporter1",
            Reason = "Spam",
            UpdatedDate = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc)
        };
        report1.TakeAction("admin1", "削除しました");

        List<ContentReport> reports = [report1];

        List<NotificationReadState> readStates =
        [
            new() { SourceId = 15, SourceType = "ReportResolved", UserId = "reporter1" }
        ];

        // Act
        List<NotificationDto> dtos = [.. NotificationService.BuildReportResolvedNotifications(reports, readStates)];

        // Assert
        Assert.Single(dtos);
        NotificationDto dto = dtos[0];
        Assert.Equal(15, dto.SourceId);
        Assert.True(dto.IsRead);
        Assert.Equal("/ItemDetail/101", dto.TargetUrl.ToHref());
        Assert.Contains("アイテム", dto.Message);
        Assert.Contains("処置（削除等）が完了しました", dto.Message);
        Assert.Contains("削除しました", dto.Message);
        Assert.True(dto.Kind is ReportResolvedNotification);
        Assert.Equal("管理者", dto.ActorName);
    }

    [Fact]
    public void NotifyNotificationsChanged_RaisesNotificationsChangedEvent()
    {
        // Arrange
        var mockProvider = new Moq.Mock<INotificationsDataProvider>();
        var service = new NotificationService(mockProvider.Object);
        var eventFired = false;
        service.NotificationsChanged += (_, _) => eventFired = true;

        // Act
        service.NotifyNotificationsChanged();

        // Assert
        Assert.True(eventFired);
    }

    [Fact]
    public void BuildSplitRequestNotifications_BuildsExpectedDto()
    {
        // Arrange
        var request = new ItemSplitRequest
        {
            Id = 42,
            OriginalItemId = 100,
            RequesterUserId = "reqUser",
            OwnerUserId = "ownerUser",
            SelectedText = "分割されるテキスト内容",
            Status = TradeStatus.Proposed,
            CreatedDate = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc),
            RequesterUser = new ApplicationUser { UserName = "Alice" },
            OwnerId = "reqUser"
        };
        List<NotificationReadState> readStates = [new() { SourceId = 42, SourceType = "ItemSplitRequest", UserId = "ownerUser" }];

        // Act
        List<NotificationDto> dtos = [.. NotificationService.BuildSplitRequestNotifications([request], readStates)];

        // Assert
        Assert.Single(dtos);
        NotificationDto dto = dtos[0];
        Assert.Equal(42, dto.SourceId);
        Assert.True(dto.IsRead);
        Assert.Equal("Alice", dto.ActorName);
        Assert.Equal(100, dto.AssociatedItemId);
        Assert.Equal("/ItemDetail/100", dto.TargetUrl.ToHref());
        Assert.Contains("Alice さんからアイテムのテキスト分割リクエスト", dto.Message);
        Assert.True(dto.Kind is ItemSplitRequestNotification);
    }

    [Fact]
    public void BuildResolvedSplitNotifications_BuildsApprovedAndRejectedDtos()
    {
        // Arrange
        var approved = new ItemSplitRequest
        {
            Id = 51,
            OriginalItemId = 101,
            CreatedItemId = 201,
            RequesterUserId = "reqUser",
            OwnerUserId = "ownerUser",
            SelectedText = "承認テキスト",
            Status = TradeStatus.Executed,
            UpdatedDate = new DateTime(2026, 3, 2, 10, 0, 0, DateTimeKind.Utc),
            OwnerUser = new ApplicationUser { UserName = "Bob" },
            OwnerId = "reqUser"
        };

        var rejected = new ItemSplitRequest
        {
            Id = 52,
            OriginalItemId = 102,
            RequesterUserId = "reqUser",
            OwnerUserId = "ownerUser",
            SelectedText = "却下テキスト",
            Status = TradeStatus.Rejected,
            RejectReason = "不適切な分割",
            UpdatedDate = new DateTime(2026, 3, 2, 11, 0, 0, DateTimeKind.Utc),
            OwnerUser = new ApplicationUser { UserName = "Bob" },
            OwnerId = "reqUser"
        };

        List<NotificationReadState> readStates = [];

        // Act
        List<NotificationDto> dtos = [.. NotificationService.BuildResolvedSplitNotifications([approved, rejected], readStates)];

        // Assert
        Assert.Equal(2, dtos.Count);

        NotificationDto approvedDto = dtos[0];
        Assert.Equal(51, approvedDto.SourceId);
        Assert.False(approvedDto.IsRead);
        Assert.Equal("Bob", approvedDto.ActorName);
        Assert.Equal("/ItemDetail/201", approvedDto.TargetUrl.ToHref());
        Assert.Contains("Bob さんがアイテム分割リクエスト（「承認テキスト」）を承認しました", approvedDto.Message);
        Assert.True(approvedDto.Kind is ItemSplitApprovedNotification);

        NotificationDto rejectedDto = dtos[1];
        Assert.Equal(52, rejectedDto.SourceId);
        Assert.False(rejectedDto.IsRead);
        Assert.Equal("Bob", rejectedDto.ActorName);
        Assert.Equal("/ItemDetail/102", rejectedDto.TargetUrl.ToHref());
        Assert.Contains("却下しました", rejectedDto.Message);
        Assert.Contains("不適切な分割", rejectedDto.Message);
        Assert.True(rejectedDto.Kind is ItemSplitRejectedNotification);
    }

    [Fact]
    public void BuildTagContentProposalNotifications_MapsPropertiesCorrectly()
    {
        // Arrange
        List<TagContentProposal> proposals =
        [
            new()
            {
                Id = 70,
                TagId = 88,
                RequesterUserId = "userA",
                OwnerUserId = "userB",
                ProposedContent = "新しいタグの説明文",
                Reason = "説明の追加",
                Status = TradeStatus.Proposed,
                CreatedDate = new DateTime(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc),
                OwnerId = "userA",
                Tag = new Tag { Id = 88, Name = "C#", OwnerId = "userB" },
                RequesterUser = new ApplicationUser { UserName = "Alice" }
            }
        ];

        List<NotificationReadState> readStates = [];

        // Act
        List<NotificationDto> dtos = [.. NotificationService.BuildTagContentProposalNotifications(proposals, readStates)];

        // Assert
        Assert.Single(dtos);
        NotificationDto dto = dtos[0];
        Assert.Equal(70, dto.SourceId);
        Assert.Equal("/TagDetail/88", dto.TargetUrl.ToHref());
        Assert.Equal("Alice", dto.ActorName);
        Assert.False(dto.IsRead);
        Assert.Contains("Alice さんからタグ「C#」の編集提案が届いています", dto.Message);
        Assert.True(dto.Kind is TagContentProposalNotification);
        switch (dto.Kind)
        {
            case TagContentProposalNotification note:
                Assert.Equal(70, note.ProposalId);
                Assert.Equal(88, note.TagId);
                Assert.Equal("C#", note.TagName);
                Assert.Equal("Alice", note.RequesterName);
                Assert.Equal("新しいタグの説明文", note.ProposedContent);
                Assert.Equal("説明の追加", note.Reason);
                Assert.Equal(TradeStatus.Proposed, note.Status);
                break;
        }
    }

    [Fact]
    public void BuildResolvedTagContentProposalNotifications_BuildsApprovedAndRejectedDtos()
    {
        // Arrange
        var approved = new TagContentProposal
        {
            Id = 71,
            TagId = 88,
            RequesterUserId = "reqUser",
            OwnerUserId = "ownerUser",
            ProposedContent = "承認された説明文",
            Status = TradeStatus.Executed,
            UpdatedDate = new DateTime(2026, 4, 2, 10, 0, 0, DateTimeKind.Utc),
            OwnerUser = new ApplicationUser { UserName = "Bob" },
            Tag = new Tag { Id = 88, Name = "C#", OwnerId = "ownerUser" },
            OwnerId = "reqUser"
        };

        var rejected = new TagContentProposal
        {
            Id = 72,
            TagId = 89,
            RequesterUserId = "reqUser",
            OwnerUserId = "ownerUser",
            ProposedContent = "却下された説明文",
            Status = TradeStatus.Rejected,
            RejectReason = "不要な説明です",
            UpdatedDate = new DateTime(2026, 4, 2, 11, 0, 0, DateTimeKind.Utc),
            OwnerUser = new ApplicationUser { UserName = "Bob" },
            Tag = new Tag { Id = 89, Name = "F#", OwnerId = "ownerUser" },
            OwnerId = "reqUser"
        };

        List<NotificationReadState> readStates = [];

        // Act
        List<NotificationDto> dtos = [.. NotificationService.BuildResolvedTagContentProposalNotifications([approved, rejected], readStates)];

        // Assert
        Assert.Equal(2, dtos.Count);

        NotificationDto approvedDto = dtos[0];
        Assert.Equal(71, approvedDto.SourceId);
        Assert.False(approvedDto.IsRead);
        Assert.Equal("Bob", approvedDto.ActorName);
        Assert.Equal("/TagDetail/88", approvedDto.TargetUrl.ToHref());
        Assert.Contains("Bob さんがタグ「C#」の編集提案（「承認された説明文」）を承認しました", approvedDto.Message);
        Assert.True(approvedDto.Kind is TagContentProposalApprovedNotification);

        NotificationDto rejectedDto = dtos[1];
        Assert.Equal(72, rejectedDto.SourceId);
        Assert.False(rejectedDto.IsRead);
        Assert.Equal("Bob", rejectedDto.ActorName);
        Assert.Equal("/TagDetail/89", rejectedDto.TargetUrl.ToHref());
        Assert.Contains("却下しました", rejectedDto.Message);
        Assert.Contains("不要な説明です", rejectedDto.Message);
        Assert.True(rejectedDto.Kind is TagContentProposalRejectedNotification);
    }

    [Fact]
    public void BuildTagNameProposalNotifications_MapsPropertiesCorrectly()
    {
        // Arrange
        List<TagNameProposal> proposals =
        [
            new()
            {
                Id = 80,
                TagId = 99,
                RequesterUserId = "userA",
                OwnerUserId = "userB",
                ProposedName = "Rustacean",
                Reason = "より適切な名称への変更",
                Status = TradeStatus.Proposed,
                CreatedDate = new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc),
                OwnerId = "userA",
                Tag = new Tag { Id = 99, Name = "Rust", OwnerId = "userB" },
                RequesterUser = new ApplicationUser { UserName = "Alice" }
            }
        ];

        List<NotificationReadState> readStates = [];

        // Act
        List<NotificationDto> dtos = [.. NotificationService.BuildTagNameProposalNotifications(proposals, readStates)];

        // Assert
        Assert.Single(dtos);
        NotificationDto dto = dtos[0];
        Assert.Equal(80, dto.SourceId);
        Assert.Equal("/TagDetail/99", dto.TargetUrl.ToHref());
        Assert.Equal("Alice", dto.ActorName);
        Assert.False(dto.IsRead);
        Assert.Contains("Alice さんからタグ「Rust」の名前変更提案（「Rustacean」）が届いています", dto.Message);
        Assert.True(dto.Kind is TagNameProposalNotification);
        switch (dto.Kind)
        {
            case TagNameProposalNotification note:
                Assert.Equal(80, note.ProposalId);
                Assert.Equal(99, note.TagId);
                Assert.Equal("Rust", note.CurrentTagName);
                Assert.Equal("Rustacean", note.ProposedName);
                Assert.Equal("Alice", note.RequesterName);
                Assert.Equal("より適切な名称への変更", note.Reason);
                Assert.Equal(TradeStatus.Proposed, note.Status);
                break;
        }
    }

    [Fact]
    public void BuildResolvedTagNameProposalNotifications_BuildsApprovedAndRejectedDtos()
    {
        // Arrange
        var approved = new TagNameProposal
        {
            Id = 81,
            TagId = 99,
            RequesterUserId = "reqUser",
            OwnerUserId = "ownerUser",
            ProposedName = "Rustacean",
            Status = TradeStatus.Executed,
            UpdatedDate = new DateTime(2026, 5, 2, 10, 0, 0, DateTimeKind.Utc),
            OwnerUser = new ApplicationUser { UserName = "Bob" },
            Tag = new Tag { Id = 99, Name = "Rustacean", OwnerId = "ownerUser" },
            OwnerId = "reqUser"
        };

        var rejected = new TagNameProposal
        {
            Id = 82,
            TagId = 100,
            RequesterUserId = "reqUser",
            OwnerUserId = "ownerUser",
            ProposedName = "GoLang",
            Status = TradeStatus.Rejected,
            RejectReason = "現在の名前が適切です",
            UpdatedDate = new DateTime(2026, 5, 2, 11, 0, 0, DateTimeKind.Utc),
            OwnerUser = new ApplicationUser { UserName = "Bob" },
            Tag = new Tag { Id = 100, Name = "Go", OwnerId = "ownerUser" },
            OwnerId = "reqUser"
        };

        List<NotificationReadState> readStates = [];

        // Act
        List<NotificationDto> dtos = [.. NotificationService.BuildResolvedTagNameProposalNotifications([approved, rejected], readStates)];

        // Assert
        Assert.Equal(2, dtos.Count);

        NotificationDto approvedDto = dtos[0];
        Assert.Equal(81, approvedDto.SourceId);
        Assert.False(approvedDto.IsRead);
        Assert.Equal("Bob", approvedDto.ActorName);
        Assert.Equal("/TagDetail/99", approvedDto.TargetUrl.ToHref());
        Assert.Contains("Bob さんがタグ「Rustacean」の名前変更提案（「Rustacean」）を承認しました", approvedDto.Message);
        Assert.True(approvedDto.Kind is TagNameProposalApprovedNotification);

        NotificationDto rejectedDto = dtos[1];
        Assert.Equal(82, rejectedDto.SourceId);
        Assert.False(rejectedDto.IsRead);
        Assert.Equal("Bob", rejectedDto.ActorName);
        Assert.Equal("/TagDetail/100", rejectedDto.TargetUrl.ToHref());
        Assert.Contains("却下しました", rejectedDto.Message);
        Assert.Contains("現在の名前が適切です", rejectedDto.Message);
        Assert.True(rejectedDto.Kind is TagNameProposalRejectedNotification);
    }
}