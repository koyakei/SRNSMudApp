using SRNSMudApp.Components.Pages;
using SRNSMudApp.Data;
using SRNSMudApp.Models;

namespace SRNSMudApp.Tests.Components.Pages;

/// <summary>
///     NotificationsViewModel の単体テスト。
///     関連アイテム解決・ハイライトイベント生成・相対時刻表現を bUnit なしで検証する。
/// </summary>
public class NotificationsViewModelTests
{
    private static NotificationDto CreateNotification(int associatedItemId = 0, int? highlightTagId = null) =>
        new()
        {
            Kind = new TagRequestNotification(
                RequestId: 1, RequestType: TaggingRequestType.Add,
                TargetItemId: 0, TargetTagName: "tag", TargetTagId: 10,
                ProposedWeight: 1, Status: TradeStatus.Proposed),
            SourceId = 1,
            AssociatedItemId = associatedItemId,
            HighlightTagId = highlightTagId,
            CreatedAt = DateTimeOffset.UtcNow
        };

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
        SRNSMudApp.Data.Item item5 = new() { Id = 5, Content = "five", OwnerId = "user-1" };

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
    public void CreateHighlightEvents_WithoutTagId_ReturnsEmpty() => Assert.Empty(NotificationsViewModel.CreateHighlightEvents(CreateNotification(), "user-1"));

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
}