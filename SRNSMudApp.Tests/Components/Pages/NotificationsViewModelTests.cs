using Moq;

using SRNSMudApp.Components.Pages;
using SRNSMudApp.Components.UI;
using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;

namespace SRNSMudApp.Tests.Components.Pages;

using Item = SRNSMudApp.Data.Item;
using Tag = SRNSMudApp.Data.Tag;

/// <summary>
///     NotificationsViewModel の単体テスト。
///     通知読み込み、タグ・アイテム解決、各種承認/却下アクションを bUnit なしで検証する。
/// </summary>
public class NotificationsViewModelTests
{
    private readonly Mock<INotificationService> _notificationServiceMock = new();
    private readonly Mock<IHomeDataProvider> _homeDataMock = new();
    private readonly Mock<INotificationsDataProvider> _notificationsDataMock = new();
    private readonly Mock<ITaggingRequestActions> _requestActionsMock = new();
    private readonly Mock<ISystemTagEnsurer> _systemTagEnsurerMock = new();
    private readonly Mock<ITaggingContractService> _taggingContractServiceMock = new();
    private readonly Mock<IItemSplitService> _itemSplitServiceMock = new();
    private readonly Mock<ITagContentProposalService> _tagContentProposalServiceMock = new();
    private readonly Mock<ITagNameProposalService> _tagNameProposalServiceMock = new();
    private readonly Mock<IRightAssetDataProvider> _rightAssetDataProviderMock = new();

    public NotificationsViewModelTests()
    {
        _notificationServiceMock.Setup(s => s.GetUserNotificationsAsync(It.IsAny<string>()))
            .ReturnsAsync([]);
        _homeDataMock.Setup(s => s.GetTagsAndRelationsAsync())
            .ReturnsAsync(([], []));
        _notificationsDataMock.Setup(s => s.GetAssociatedItemsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
    }

    private NotificationsViewModel CreateViewModel() =>
        new(
            _notificationServiceMock.Object,
            _homeDataMock.Object,
            _notificationsDataMock.Object,
            _requestActionsMock.Object,
            _systemTagEnsurerMock.Object,
            _taggingContractServiceMock.Object,
            _itemSplitServiceMock.Object,
            _tagContentProposalServiceMock.Object,
            _tagNameProposalServiceMock.Object,
            _rightAssetDataProviderMock.Object);

    private static NotificationDto CreateNotification(int associatedItemId = 0, int? highlightTagId = null, bool isRead = false) =>
        new()
        {
            Kind = new TagRequestNotification(
                RequestId: 1, RequestType: TaggingRequestType.Add,
                TargetItemId: 0, TargetTagName: "tag", TargetTagId: 10,
                ProposedWeight: 1, Status: TradeStatus.Proposed),
            SourceId = 1,
            AssociatedItemId = associatedItemId,
            HighlightTagId = highlightTagId,
            IsRead = isRead,
            CreatedAt = DateTimeOffset.UtcNow
        };

    private static NotificationDto CreateSplitNotification(int sourceId = 2, int associatedItemId = 5) =>
        new()
        {
            Kind = new ItemSplitRequestNotification(
                SplitRequestId: sourceId, OriginalItemId: associatedItemId,
                RequesterName: "requester", SelectedText: "split text", Status: TradeStatus.Proposed),
            SourceId = sourceId,
            AssociatedItemId = associatedItemId,
            CreatedAt = DateTimeOffset.UtcNow
        };

    private static NotificationDto CreateTagProposalNotification(int sourceId = 3, int tagId = 10) =>
        new()
        {
            Kind = new TagContentProposalNotification(
                ProposalId: sourceId, TagId: tagId,
                TagName: "tag", RequesterName: "requester",
                ProposedContent: "content", Reason: null, Status: TradeStatus.Proposed),
            SourceId = sourceId,
            CreatedAt = DateTimeOffset.UtcNow
        };

    private static NotificationDto CreateTagNameProposalNotification(int sourceId = 4, int tagId = 11) =>
        new()
        {
            Kind = new TagNameProposalNotification(
                ProposalId: sourceId, TagId: tagId,
                CurrentTagName: "old-name", ProposedName: "new-name",
                RequesterName: "requester", Reason: null, Status: TradeStatus.Proposed),
            SourceId = sourceId,
            CreatedAt = DateTimeOffset.UtcNow
        };

    private static NotificationDto CreateTagPermissionRequestNotification(int sourceId = 5, int tagId = 12) =>
        new()
        {
            Kind = new TagPermissionRequestNotification(
                ItemId: sourceId,
                RequestedTagId: tagId,
                RequestedTagName: "permission-tag",
                RequestedAmount: 1,
                OfferedRightAssetId: null,
                OfferedTagName: null,
                OfferedAmount: 0,
                RequesterName: "requester",
                Message: "Please approve",
                Status: TradeStatus.Proposed),
            SourceId = sourceId,
            CreatedAt = DateTimeOffset.UtcNow
        };

    // ============================================================
    // 静的ヘルパーのテスト
    // ============================================================

    [Fact]
    public void GetAssociatedItemIds_DeduplicatesAndSkipsZero()
    {
        List<NotificationDto> notifications =
        [
            CreateNotification(associatedItemId: 5),
            CreateNotification(associatedItemId: 5),
            CreateNotification(),
            CreateNotification(associatedItemId: 7)
        ];

        var ids = NotificationsViewModel.GetAssociatedItemIds(notifications);

        Assert.Equal([5, 7], ids);
    }

    [Fact]
    public void MapAssociatedItems_AssignsItemsById()
    {
        NotificationDto n5 = CreateNotification(associatedItemId: 5);
        NotificationDto n6 = CreateNotification(associatedItemId: 6);
        Item item5 = new() { Id = 5, Content = "five", OwnerId = "user-1" };

        NotificationsViewModel.MapAssociatedItems([n5, n6], [item5]);

        Assert.Same(item5, n5.AssociatedItem);
        Assert.Null(n6.AssociatedItem);
    }

    [Fact]
    public void MapAssociatedItems_WithEmptyItems_IsNoOp()
    {
        NotificationDto n1 = CreateNotification(associatedItemId: 1);

        NotificationsViewModel.MapAssociatedItems([n1], []);

        Assert.Null(n1.AssociatedItem);
    }

    [Fact]
    public void CreateHighlightEvents_WithTagId_CreatesUpdateEvent()
    {
        var notification = CreateNotification(highlightTagId: 42);

        var events = NotificationsViewModel.CreateHighlightEvents(notification, "user-1");

        TimelineEvent e = Assert.Single(events);
        Assert.Equal("Update", e.EventType);
        Assert.Equal(42, e.FollowedTagId);
        Assert.Equal("user-1", e.OwnerId);
    }

    [Fact]
    public void CreateHighlightEvents_WithoutTagId_ReturnsEmpty() =>
        Assert.Empty(NotificationsViewModel.CreateHighlightEvents(CreateNotification(), "user-1"));

    [Theory]
    [InlineData(10, "10秒前")]
    [InlineData(90, "1分前")]
    [InlineData(3600 + 120, "1時間前")]
    [InlineData(86400 * 2, "2日前")]
    public void GetRelativeTime_FormatsElapsedDurations(double seconds, string expected)
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var dateTime = now.AddSeconds(-seconds);

        Assert.Equal(expected, NotificationsViewModel.GetRelativeTime(dateTime, now));
    }

    // ============================================================
    // 初期化・データ取得のテスト
    // ============================================================

    [Fact]
    public async Task InitializeAsync_WhenUserIdIsNull_DoesNotFetchDataAndSetsLoadingFalse()
    {
        var sut = CreateViewModel();

        await sut.InitializeAsync(null);

        Assert.Null(sut.CurrentUserId);
        Assert.False(sut.IsLoading);
        Assert.Empty(sut.Notifications);
        _notificationServiceMock.Verify(s => s.GetUserNotificationsAsync(It.IsAny<string>()), Times.Never);
        _notificationServiceMock.Verify(s => s.MarkAllAsReadAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task InitializeAsync_WhenUserIdProvided_FetchesNotificationsTagsItemsAndMarksAllAsRead()
    {
        var sut = CreateViewModel();
        NotificationDto note = CreateNotification(associatedItemId: 10);
        _notificationServiceMock.Setup(s => s.GetUserNotificationsAsync("user-1"))
            .ReturnsAsync([note]);
        _homeDataMock.Setup(s => s.GetTagsAndRelationsAsync())
            .ReturnsAsync(([], []));
        _notificationsDataMock.Setup(s => s.GetAssociatedItemsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Item { Id = 10, Content = "item-10", OwnerId = "user-1" }]);

        await sut.InitializeAsync("user-1");

        Assert.Equal("user-1", sut.CurrentUserId);
        Assert.False(sut.IsLoading);
        Assert.Single(sut.Notifications);
        Assert.NotNull(sut.Notifications[0].AssociatedItem);
        Assert.Equal("item-10", sut.Notifications[0].AssociatedItem!.Content);
        _notificationServiceMock.Verify(s => s.MarkAllAsReadAsync("user-1"), Times.Once);
    }

    [Fact]
    public async Task EnsureSystemTagsExistAsync_UpdatesProperties_AndRefetchesWhenRequested()
    {
        var sut = CreateViewModel();
        _homeDataMock.Setup(s => s.GetTagsAndRelationsAsync())
            .ReturnsAsync(([new Tag { Id = 100, Name = "tag-100", OwnerId = "user-1" }], []));
        _systemTagEnsurerMock.Setup(s => s.EnsureAllAsync(
                It.IsAny<string?>(),
                It.IsAny<SystemTagIds>(),
                It.IsAny<ReactionTagIds>()))
            .ReturnsAsync((new SystemTagIds(1, 2), new ReactionTagIds(3, 4, 5), true));

        await sut.EnsureSystemTagsExistAsync();

        Assert.Equal(1, sut.CurrentUserGoodTagId);
        Assert.Equal(2, sut.CurrentUserBadTagId);
        Assert.Equal(3, sut.CurrentUserShinjiTagId);
        Assert.Equal(4, sut.CurrentUserZenTagId);
        Assert.Equal(5, sut.CurrentUserBiTagId);
        _homeDataMock.Verify(s => s.GetTagsAndRelationsAsync(), Times.Once);
        Assert.Single(sut.AllTags);
    }

    [Fact]
    public async Task EnsureSystemTagsExistAsync_WhenRefetchFalse_DoesNotRefetchTags()
    {
        var sut = CreateViewModel();
        _systemTagEnsurerMock.Setup(s => s.EnsureAllAsync(
                It.IsAny<string?>(),
                It.IsAny<SystemTagIds>(),
                It.IsAny<ReactionTagIds>()))
            .ReturnsAsync((new SystemTagIds(1, 2), new ReactionTagIds(3, 4, 5), false));

        await sut.EnsureSystemTagsExistAsync();

        Assert.Equal(1, sut.CurrentUserGoodTagId);
        _homeDataMock.Verify(s => s.GetTagsAndRelationsAsync(), Times.Never);
    }

    // ============================================================
    // 既読化のテスト
    // ============================================================

    [Fact]
    public async Task MarkAsReadAsync_WhenUnreadAndUserLoggedIn_CallsService()
    {
        var sut = CreateViewModel();
        await sut.InitializeAsync("user-1");
        var notification = CreateNotification(isRead: false);

        await sut.MarkAsReadAsync(notification);

        _notificationServiceMock.Verify(s => s.MarkAsReadAsync("user-1", notification.SourceId, notification.Kind.SourceType), Times.Once);
    }

    [Fact]
    public async Task MarkAsReadAsync_WhenAlreadyRead_DoesNotCallService()
    {
        var sut = CreateViewModel();
        await sut.InitializeAsync("user-1");
        var notification = CreateNotification(isRead: true);

        await sut.MarkAsReadAsync(notification);

        _notificationServiceMock.Verify(s => s.MarkAsReadAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task MarkAsReadAsync_WhenUserNotLoggedIn_DoesNotCallService()
    {
        var sut = CreateViewModel();
        var notification = CreateNotification(isRead: false);

        await sut.MarkAsReadAsync(notification);

        _notificationServiceMock.Verify(s => s.MarkAsReadAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    // ============================================================
    // タグリクエスト承認・却下のテスト
    // ============================================================

    [Fact]
    public async Task ApproveRequestAsync_WhenUserNull_ReturnsNoOp()
    {
        var sut = CreateViewModel();
        var notification = CreateNotification();

        var result = await sut.ApproveRequestAsync(notification);

        Assert.Equal(TagCardActionResultType.NoOp, result.Type);
    }

    [Fact]
    public async Task ApproveRequestAsync_WhenActionFails_ReturnsNoOp()
    {
        var sut = CreateViewModel();
        _notificationServiceMock.Setup(s => s.GetUserNotificationsAsync("user-1"))
            .ReturnsAsync([CreateNotification()]);
        await sut.InitializeAsync("user-1");
        var note = sut.Notifications[0];

        _requestActionsMock.Setup(a => a.ApproveAsync(note.SourceId, "user-1")).ReturnsAsync(false);

        var result = await sut.ApproveRequestAsync(note);

        Assert.Equal(TagCardActionResultType.NoOp, result.Type);
    }

    [Fact]
    public async Task ApproveRequestAsync_WhenActionSucceeds_UpdatesNotificationAndReturnsSuccess()
    {
        var sut = CreateViewModel();
        _notificationServiceMock.Setup(s => s.GetUserNotificationsAsync("user-1"))
            .ReturnsAsync([CreateNotification()]);
        await sut.InitializeAsync("user-1");
        var note = sut.Notifications[0];

        _requestActionsMock.Setup(a => a.ApproveAsync(note.SourceId, "user-1")).ReturnsAsync(true);

        var result = await sut.ApproveRequestAsync(note);

        Assert.Equal(TagCardActionResultType.Success, result.Type);
        Assert.True(result.ShouldNotifyChanged);
        var updatedNote = sut.Notifications[0];
        Assert.True(updatedNote.IsRead);
        Assert.True(updatedNote.Kind is TagRequestNotification { Status: TradeStatus.Executed });
    }

    [Fact]
    public async Task RejectRequestAsync_WhenUserNull_ReturnsNoOp()
    {
        var sut = CreateViewModel();
        var notification = CreateNotification();

        var result = await sut.RejectRequestAsync(notification, "comment");

        Assert.Equal(TagCardActionResultType.NoOp, result.Type);
    }

    [Fact]
    public async Task RejectRequestAsync_WhenCancelSucceeds_UpdatesNotificationAndReturnsSuccess()
    {
        var sut = CreateViewModel();
        _notificationServiceMock.Setup(s => s.GetUserNotificationsAsync("user-1"))
            .ReturnsAsync([CreateNotification()]);
        await sut.InitializeAsync("user-1");
        var note = sut.Notifications[0];

        Result<string> cancelRes = new Success<string>("ok");
        _taggingContractServiceMock.Setup(s => s.RejectContractAsync(note.SourceId, "user-1", It.IsAny<string?>()))
            .ReturnsAsync(cancelRes);

        var result = await sut.RejectRequestAsync(note, "comment");

        Assert.Equal(TagCardActionResultType.Success, result.Type);
        Assert.Equal("リクエストを却下しました。", result.Message);
        var updatedNote = sut.Notifications[0];
        Assert.True(updatedNote.IsRead);
        Assert.True(updatedNote.Kind is TagRequestNotification { Status: TradeStatus.Rejected });
    }

    [Fact]
    public async Task RejectRequestAsync_WhenExceptionThrown_ReturnsError()
    {
        var sut = CreateViewModel();
        _notificationServiceMock.Setup(s => s.GetUserNotificationsAsync("user-1"))
            .ReturnsAsync([CreateNotification()]);
        await sut.InitializeAsync("user-1");
        var note = sut.Notifications[0];

        _taggingContractServiceMock.Setup(s => s.RejectContractAsync(note.SourceId, "user-1", It.IsAny<string?>()))
            .ThrowsAsync(new InvalidOperationException("database failure"));

        var result = await sut.RejectRequestAsync(note, "comment");

        Assert.Equal(TagCardActionResultType.Error, result.Type);
        Assert.Contains("database failure", result.Message);
    }

    // ============================================================
    // 分割リクエスト承認・却下のテスト
    // ============================================================

    [Fact]
    public async Task ApproveSplitRequestAsync_WhenUserNull_ReturnsNoOp()
    {
        var sut = CreateViewModel();
        var notification = CreateSplitNotification();

        var result = await sut.ApproveSplitRequestAsync(notification);

        Assert.Equal(TagCardActionResultType.NoOp, result.Type);
    }

    [Fact]
    public async Task ApproveSplitRequestAsync_WhenSuccess_UpdatesNotificationAndFetchesItems()
    {
        var sut = CreateViewModel();
        _notificationServiceMock.Setup(s => s.GetUserNotificationsAsync("user-1"))
            .ReturnsAsync([CreateSplitNotification()]);
        await sut.InitializeAsync("user-1");
        var note = sut.Notifications[0];

        Result<Item> res = new Success<Item>(new Item { Id = 5, Content = "split", OwnerId = "user-1" });
        _itemSplitServiceMock.Setup(s => s.ApproveSplitAsync(note.SourceId, "user-1")).ReturnsAsync(res);
        _notificationsDataMock.Setup(s => s.GetAssociatedItemsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Item { Id = 5, Content = "split", OwnerId = "user-1" }]);

        var result = await sut.ApproveSplitRequestAsync(note);

        Assert.Equal(TagCardActionResultType.Success, result.Type);
        Assert.Equal("分割リクエストを承認しました。", result.Message);
        var updatedNote = sut.Notifications[0];
        Assert.True(updatedNote.IsRead);
        Assert.True(updatedNote.Kind is ItemSplitRequestNotification { Status: TradeStatus.Executed });
        _notificationsDataMock.Verify(s => s.GetAssociatedItemsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ApproveSplitRequestAsync_WhenFailure_ReturnsError()
    {
        var sut = CreateViewModel();
        await sut.InitializeAsync("user-1");
        var note = CreateSplitNotification();

        Result<Item> fail = new Failure("cannot split");
        _itemSplitServiceMock.Setup(s => s.ApproveSplitAsync(note.SourceId, "user-1")).ReturnsAsync(fail);

        var result = await sut.ApproveSplitRequestAsync(note);

        Assert.Equal(TagCardActionResultType.Error, result.Type);
        Assert.Equal("cannot split", result.Message);
    }

    [Fact]
    public async Task RejectSplitRequestAsync_WhenSuccess_UpdatesStatusAndReturnsSuccess()
    {
        var sut = CreateViewModel();
        _notificationServiceMock.Setup(s => s.GetUserNotificationsAsync("user-1"))
            .ReturnsAsync([CreateSplitNotification()]);
        await sut.InitializeAsync("user-1");
        var note = sut.Notifications[0];

        Result<bool> res = new Success<bool>(true);
        _itemSplitServiceMock.Setup(s => s.RejectSplitAsync(note.SourceId, "user-1", "reason")).ReturnsAsync(res);

        var result = await sut.RejectSplitRequestAsync(note, "reason");

        Assert.Equal(TagCardActionResultType.Success, result.Type);
        Assert.Equal("分割リクエストを却下しました。", result.Message);
        var updated = sut.Notifications[0];
        Assert.True(updated.IsRead);
        Assert.True(updated.Kind is ItemSplitRequestNotification { Status: TradeStatus.Rejected });
    }

    [Fact]
    public async Task RejectSplitRequestAsync_WhenFailure_ReturnsError()
    {
        var sut = CreateViewModel();
        await sut.InitializeAsync("user-1");
        var note = CreateSplitNotification();

        Result<bool> fail = new Failure("reject failed");
        _itemSplitServiceMock.Setup(s => s.RejectSplitAsync(note.SourceId, "user-1", "reason")).ReturnsAsync(fail);

        var result = await sut.RejectSplitRequestAsync(note, "reason");

        Assert.Equal(TagCardActionResultType.Error, result.Type);
        Assert.Equal("reject failed", result.Message);
    }

    [Fact]
    public async Task RejectSplitRequestAsync_WhenExceptionThrown_ReturnsError()
    {
        var sut = CreateViewModel();
        await sut.InitializeAsync("user-1");
        var note = CreateSplitNotification();

        _itemSplitServiceMock.Setup(s => s.RejectSplitAsync(note.SourceId, "user-1", "reason"))
            .ThrowsAsync(new InvalidOperationException("split boom"));

        var result = await sut.RejectSplitRequestAsync(note, "reason");

        Assert.Equal(TagCardActionResultType.Error, result.Type);
        Assert.Contains("split boom", result.Message);
    }

    // ============================================================
    // タグコンテンツ提案承認・却下のテスト
    // ============================================================

    [Fact]
    public async Task ApproveTagProposalAsync_WhenSuccess_UpdatesNotificationAndFetchesTags()
    {
        var sut = CreateViewModel();
        _notificationServiceMock.Setup(s => s.GetUserNotificationsAsync("user-1"))
            .ReturnsAsync([CreateTagProposalNotification()]);
        await sut.InitializeAsync("user-1");
        var note = sut.Notifications[0];

        Result<Tag> res = new Success<Tag>(new Tag { Id = 10, Name = "tag", OwnerId = "user-1" });
        _tagContentProposalServiceMock.Setup(s => s.ApproveProposalAsync(note.SourceId, "user-1")).ReturnsAsync(res);

        var result = await sut.ApproveTagProposalAsync(note);

        Assert.Equal(TagCardActionResultType.Success, result.Type);
        Assert.Equal("編集提案を承認しました。", result.Message);
        var updated = sut.Notifications[0];
        Assert.True(updated.IsRead);
        Assert.True(updated.Kind is TagContentProposalNotification { Status: TradeStatus.Executed });
        _homeDataMock.Verify(s => s.GetTagsAndRelationsAsync(), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ApproveTagProposalAsync_WhenFailure_ReturnsError()
    {
        var sut = CreateViewModel();
        await sut.InitializeAsync("user-1");
        var note = CreateTagProposalNotification();

        Result<Tag> fail = new Failure("approve proposal error");
        _tagContentProposalServiceMock.Setup(s => s.ApproveProposalAsync(note.SourceId, "user-1")).ReturnsAsync(fail);

        var result = await sut.ApproveTagProposalAsync(note);

        Assert.Equal(TagCardActionResultType.Error, result.Type);
        Assert.Equal("approve proposal error", result.Message);
    }

    [Fact]
    public async Task RejectTagProposalAsync_WhenSuccess_UpdatesNotificationAndReturnsSuccess()
    {
        var sut = CreateViewModel();
        _notificationServiceMock.Setup(s => s.GetUserNotificationsAsync("user-1"))
            .ReturnsAsync([CreateTagProposalNotification()]);
        await sut.InitializeAsync("user-1");
        var note = sut.Notifications[0];

        Result<bool> res = new Success<bool>(true);
        _tagContentProposalServiceMock.Setup(s => s.RejectProposalAsync(note.SourceId, "user-1", "reason")).ReturnsAsync(res);

        var result = await sut.RejectTagProposalAsync(note, "reason");

        Assert.Equal(TagCardActionResultType.Success, result.Type);
        Assert.True(result.ShouldNotifyChanged);
        Assert.Equal("編集提案を却下しました。", result.Message);
        var updated = sut.Notifications[0];
        Assert.True(updated.IsRead);
        Assert.True(updated.Kind is TagContentProposalNotification { Status: TradeStatus.Rejected });
    }

    [Fact]
    public async Task RejectTagProposalAsync_WhenExceptionThrown_ReturnsError()
    {
        var sut = CreateViewModel();
        await sut.InitializeAsync("user-1");
        var note = CreateTagProposalNotification();

        _tagContentProposalServiceMock.Setup(s => s.RejectProposalAsync(note.SourceId, "user-1", "reason"))
            .ThrowsAsync(new InvalidOperationException("proposal reject crash"));

        var result = await sut.RejectTagProposalAsync(note, "reason");

        Assert.Equal(TagCardActionResultType.Error, result.Type);
        Assert.Contains("proposal reject crash", result.Message);
    }

    // ============================================================
    // タグ名提案承認・却下のテスト
    // ============================================================

    [Fact]
    public async Task ApproveTagNameProposalAsync_WhenSuccess_UpdatesNotificationAndFetchesTags()
    {
        var sut = CreateViewModel();
        _notificationServiceMock.Setup(s => s.GetUserNotificationsAsync("user-1"))
            .ReturnsAsync([CreateTagNameProposalNotification()]);
        await sut.InitializeAsync("user-1");
        var note = sut.Notifications[0];

        Result<Tag> res = new Success<Tag>(new Tag { Id = 11, Name = "new-name", OwnerId = "user-1" });
        _tagNameProposalServiceMock.Setup(s => s.ApproveProposalAsync(note.SourceId, "user-1")).ReturnsAsync(res);

        var result = await sut.ApproveTagNameProposalAsync(note);

        Assert.Equal(TagCardActionResultType.Success, result.Type);
        Assert.Equal("名前変更提案を承認しました。", result.Message);
        var updated = sut.Notifications[0];
        Assert.True(updated.IsRead);
        Assert.True(updated.Kind is TagNameProposalNotification { Status: TradeStatus.Executed });
        _homeDataMock.Verify(s => s.GetTagsAndRelationsAsync(), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ApproveTagNameProposalAsync_WhenFailure_ReturnsError()
    {
        var sut = CreateViewModel();
        await sut.InitializeAsync("user-1");
        var note = CreateTagNameProposalNotification();

        Result<Tag> fail = new Failure("tag name approve failed");
        _tagNameProposalServiceMock.Setup(s => s.ApproveProposalAsync(note.SourceId, "user-1")).ReturnsAsync(fail);

        var result = await sut.ApproveTagNameProposalAsync(note);

        Assert.Equal(TagCardActionResultType.Error, result.Type);
        Assert.Equal("tag name approve failed", result.Message);
    }

    [Fact]
    public async Task RejectTagNameProposalAsync_WhenSuccess_UpdatesNotificationAndReturnsSuccess()
    {
        var sut = CreateViewModel();
        _notificationServiceMock.Setup(s => s.GetUserNotificationsAsync("user-1"))
            .ReturnsAsync([CreateTagNameProposalNotification()]);
        await sut.InitializeAsync("user-1");
        var note = sut.Notifications[0];

        Result<bool> res = new Success<bool>(true);
        _tagNameProposalServiceMock.Setup(s => s.RejectProposalAsync(note.SourceId, "user-1", "reason")).ReturnsAsync(res);

        var result = await sut.RejectTagNameProposalAsync(note, "reason");

        Assert.Equal(TagCardActionResultType.Success, result.Type);
        Assert.True(result.ShouldNotifyChanged);
        Assert.Equal("名前変更提案を却下しました。", result.Message);
        var updated = sut.Notifications[0];
        Assert.True(updated.IsRead);
        Assert.True(updated.Kind is TagNameProposalNotification { Status: TradeStatus.Rejected });
    }

    [Fact]
    public async Task RejectTagNameProposalAsync_WhenExceptionThrown_ReturnsError()
    {
        var sut = CreateViewModel();
        await sut.InitializeAsync("user-1");
        var note = CreateTagNameProposalNotification();

        _tagNameProposalServiceMock.Setup(s => s.RejectProposalAsync(note.SourceId, "user-1", "reason"))
            .ThrowsAsync(new InvalidOperationException("name reject error"));

        var result = await sut.RejectTagNameProposalAsync(note, "reason");

        Assert.Equal(TagCardActionResultType.Error, result.Type);
        Assert.Contains("name reject error", result.Message);
    }

    [Fact]
    public async Task ApprovePermissionRequestAsync_WhenSuccess_UpdatesNotificationAndReturnsSuccess()
    {
        var sut = CreateViewModel();
        _notificationServiceMock.Setup(s => s.GetUserNotificationsAsync("user-1"))
            .ReturnsAsync([CreateTagPermissionRequestNotification()]);
        await sut.InitializeAsync("user-1");
        var note = sut.Notifications[0];

        Result<bool> res = new Success<bool>(true);
        _rightAssetDataProviderMock.Setup(s => s.ApprovePermissionRequestAsync(note.SourceId, "user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(res);

        var result = await sut.ApprovePermissionRequestAsync(note);

        Assert.Equal(TagCardActionResultType.Success, result.Type);
        Assert.True(result.ShouldNotifyChanged);
        Assert.Equal("操作権限リクエストを承認しました。", result.Message);
        var updated = sut.Notifications[0];
        Assert.True(updated.IsRead);
        Assert.True(updated.Kind is TagPermissionRequestNotification { Status: TradeStatus.Executed });
    }

    [Fact]
    public async Task ApprovePermissionRequestAsync_WhenFailure_ReturnsError()
    {
        var sut = CreateViewModel();
        await sut.InitializeAsync("user-1");
        var note = CreateTagPermissionRequestNotification();

        Result<bool> fail = new Failure("権限不足です。");
        _rightAssetDataProviderMock.Setup(s => s.ApprovePermissionRequestAsync(note.SourceId, "user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(fail);

        var result = await sut.ApprovePermissionRequestAsync(note);

        Assert.Equal(TagCardActionResultType.Error, result.Type);
        Assert.Equal("権限不足です。", result.Message);
    }

    [Fact]
    public async Task RejectPermissionRequestAsync_WhenSuccess_UpdatesNotificationAndReturnsSuccess()
    {
        var sut = CreateViewModel();
        _notificationServiceMock.Setup(s => s.GetUserNotificationsAsync("user-1"))
            .ReturnsAsync([CreateTagPermissionRequestNotification()]);
        await sut.InitializeAsync("user-1");
        var note = sut.Notifications[0];

        Result<bool> res = new Success<bool>(true);
        _rightAssetDataProviderMock.Setup(s => s.RejectPermissionRequestAsync(note.SourceId, "user-1", "reason", It.IsAny<CancellationToken>()))
            .ReturnsAsync(res);

        var result = await sut.RejectPermissionRequestAsync(note, "reason");

        Assert.Equal(TagCardActionResultType.Success, result.Type);
        Assert.True(result.ShouldNotifyChanged);
        Assert.Equal("操作権限リクエストを却下しました。", result.Message);
        var updated = sut.Notifications[0];
        Assert.True(updated.IsRead);
        Assert.True(updated.Kind is TagPermissionRequestNotification { Status: TradeStatus.Rejected });
    }

    [Fact]
    public async Task RejectPermissionRequestAsync_WhenFailure_ReturnsError()
    {
        var sut = CreateViewModel();
        await sut.InitializeAsync("user-1");
        var note = CreateTagPermissionRequestNotification();

        Result<bool> fail = new Failure("却下に失敗しました。");
        _rightAssetDataProviderMock.Setup(s => s.RejectPermissionRequestAsync(note.SourceId, "user-1", "reason", It.IsAny<CancellationToken>()))
            .ReturnsAsync(fail);

        var result = await sut.RejectPermissionRequestAsync(note, "reason");

        Assert.Equal(TagCardActionResultType.Error, result.Type);
        Assert.Equal("却下に失敗しました。", result.Message);
    }
}