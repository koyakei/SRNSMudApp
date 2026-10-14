namespace SRNSMudApp.Tests.Client;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor.Services;

using SRNSMudApp.Client.Models.Api;
using SRNSMudApp.Client.Pages;
using SRNSMudApp.Client.Services;

using Xunit;

public class NotificationPageTests : IAsyncDisposable
{
    private readonly BunitContext _ctx = new();
    private readonly Mock<IItemListApiClient> _apiClientMock = new();

    public NotificationPageTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddMudServices();
        _ctx.Services.AddScoped(_ => _apiClientMock.Object);
    }

    [Fact]
    public void WhenNoNotifications_RendersEmptyMessage()
    {
        // Arrange
        _apiClientMock
            .Setup(c => c.GetNotificationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _apiClientMock
            .Setup(c => c.GetUnreadNotificationCountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        // Act
        IRenderedComponent<NotificationPage> cut = _ctx.Render<NotificationPage>();

        // Assert
        Assert.Contains("通知はありません", cut.Markup);
    }

    [Fact]
    public void WhenNotificationsExist_RendersNotificationsAndUnreadBadge()
    {
        // Arrange
        var notification1 = new NotificationItemDto(
            SourceId: 1,
            SourceType: "ItemReply",
            Message: "テストリプライメッセージ",
            CreatedAt: DateTimeOffset.UtcNow,
            IsRead: false,
            ActorName: "alice",
            AssociatedItemId: 10,
            HighlightTagId: null,
            TargetPath: null
        );
        var notification2 = new NotificationItemDto(
            SourceId: 2,
            SourceType: "TagRequestApproved",
            Message: "リクエストが承認されました",
            CreatedAt: DateTimeOffset.UtcNow,
            IsRead: true,
            ActorName: "bob",
            AssociatedItemId: 0,
            HighlightTagId: 5,
            TargetPath: null
        );

        _apiClientMock
            .Setup(c => c.GetNotificationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([notification1, notification2]);
        _apiClientMock
            .Setup(c => c.GetUnreadNotificationCountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        IRenderedComponent<NotificationPage> cut = _ctx.Render<NotificationPage>();

        // Assert
        Assert.Contains("テストリプライメッセージ", cut.Markup);
        Assert.Contains("リクエストが承認されました", cut.Markup);
        Assert.Contains("alice", cut.Markup);
        Assert.Contains("未読", cut.Markup);
        Assert.Contains("すべて (2)", cut.Markup);
        Assert.Contains("未読のみ (1)", cut.Markup);
        Assert.Contains("アイテム #10 を開く", cut.Markup);
        Assert.Contains("タグ詳細を開く", cut.Markup);
    }

    [Fact]
    public async Task WhenMarkAllAsReadClicked_CallsApi()
    {
        // Arrange
        var notification = new NotificationItemDto(
            SourceId: 1,
            SourceType: "ItemReply",
            Message: "テストリプライ",
            CreatedAt: DateTimeOffset.UtcNow,
            IsRead: false,
            ActorName: "alice",
            AssociatedItemId: 10,
            HighlightTagId: null,
            TargetPath: null
        );

        _apiClientMock
            .Setup(c => c.GetNotificationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([notification]);
        _apiClientMock
            .Setup(c => c.GetUnreadNotificationCountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _apiClientMock
            .Setup(c => c.MarkAllNotificationsAsReadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        IRenderedComponent<NotificationPage> cut = _ctx.Render<NotificationPage>();

        // Act
        var button = cut.Find("button:contains('すべて既読にする')");
        await cut.InvokeAsync(() => button.Click());

        // Assert
        _apiClientMock.Verify(c => c.MarkAllNotificationsAsReadAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}