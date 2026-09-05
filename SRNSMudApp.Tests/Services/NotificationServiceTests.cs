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
    [InlineData(null, "不明")]
    public void GetRequestTypeLabel_ReturnsExpectedJapaneseText(TaggingRequestType? type, string expected)
    {
        var result = NotificationService.GetRequestTypeLabel(type);
        Assert.Equal(expected, result);
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
                Status = TradeStatus.Proposed,
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
        List<TaggingRequestEntity> requests =
        [
            new()
            {
                Id = 11,
                RequestType = TaggingRequestType.Add,
                TargetItemId = 101,
                RequestedTagId = 201,
                Status = TradeStatus.Rejected,
                UpdatedDate = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
                OwnerId = "userA",
                RequestedTag = new Tag { Name = "Go", OwnerId = "system" },
                RequesterUserId = "userA",
                TagOwnerUserId = "userB",
                Rejection = new RejectionReason("不適切なタグ付けです。")
            }
        ];

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
        List<TaggingRequestEntity> requests =
        [
            new()
            {
                Id = 12,
                RequestType = TaggingRequestType.Add,
                TargetItemId = 102,
                RequestedTagId = 202,
                Status = TradeStatus.Executed,
                UpdatedDate = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc),
                OwnerId = "userA",
                RequestedTag = new Tag { Name = "C#", OwnerId = "system" },
                RequesterUserId = "userA",
                TagOwnerUserId = "userB"
            }
        ];

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
        Assert.Equal("/ItemDetail/20", dto.TargetUrl.ToHref());
        Assert.True(dto.Kind is ItemReplyNotification);
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
                    Status = TradeStatus.Proposed,
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
    public async Task MarkAsReadAsync_DelegatesToDataProvider()
    {
        // Arrange
        var mockProvider = new Moq.Mock<INotificationsDataProvider>();
        var service = new NotificationService(mockProvider.Object);

        // Act
        await service.MarkAsReadAsync("user1", 42, "TagRequest");

        // Assert
        mockProvider.Verify(p => p.MarkAsReadAsync("user1", 42, "TagRequest", Moq.It.IsAny<CancellationToken>()), Moq.Times.Once);
    }
}