namespace SRNSMudApp.Tests.Controllers;

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using Moq;

using SRNSMudApp.Client.Models.Api;
using SRNSMudApp.Controllers;
using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Services;

using Xunit;

public class NotificationControllerTests
{
    private readonly Mock<INotificationService> _notificationServiceMock = new();

    private NotificationController CreateController(ClaimsPrincipal? user = null)
    {
        var controller = new NotificationController(_notificationServiceMock.Object);
        if (user != null)
        {
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user }
            };
        }
        return controller;
    }

    [Fact]
    public async Task GetNotifications_Unauthorized_ReturnsUnauthorized()
    {
        // Arrange
        var controller = CreateController(); // 未認証

        // Act
        ActionResult<IReadOnlyList<NotificationItemDto>> result = await controller.GetNotifications(CancellationToken.None);

        // Assert
        Assert.IsType<UnauthorizedObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetNotifications_Authorized_ReturnsNotifications()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-123")], "test"));
        var controller = CreateController(user);
        var notifications = new List<NotificationDto>
        {
            new()
            {
                SourceId = 1,
                Kind = new ItemReplyNotification(11, 10, "返信者"),
                Message = "返信がありました",
                CreatedAt = DateTimeOffset.UtcNow,
                IsRead = false,
                ActorName = "返信者",
                AssociatedItemId = 10
            }
        };

        _notificationServiceMock
            .Setup(s => s.GetUserNotificationsAsync("user-123"))
            .ReturnsAsync(notifications);

        // Act
        ActionResult<IReadOnlyList<NotificationItemDto>> result = await controller.GetNotifications(CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dtos = Assert.IsAssignableFrom<IReadOnlyList<NotificationItemDto>>(okResult.Value);
        Assert.Single(dtos);
        Assert.Equal(1, dtos[0].SourceId);
        Assert.Equal("ItemReply", dtos[0].SourceType);
        Assert.False(dtos[0].IsRead);
    }

    [Fact]
    public async Task GetUnreadCount_Unauthorized_ReturnsUnauthorized()
    {
        // Arrange
        var controller = CreateController(); // 未認証

        // Act
        ActionResult<UnreadCountResponse> result = await controller.GetUnreadCount(CancellationToken.None);

        // Assert
        Assert.IsType<UnauthorizedObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetUnreadCount_Authorized_ReturnsCount()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-123")], "test"));
        var controller = CreateController(user);

        _notificationServiceMock
            .Setup(s => s.GetUnreadCountAsync("user-123"))
            .ReturnsAsync(3);

        // Act
        ActionResult<UnreadCountResponse> result = await controller.GetUnreadCount(CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<UnreadCountResponse>(okResult.Value);
        Assert.Equal(3, response.UnreadCount);
    }

    [Fact]
    public async Task MarkAsRead_Unauthorized_ReturnsUnauthorized()
    {
        // Arrange
        var controller = CreateController(); // 未認証

        // Act
        ActionResult result = await controller.MarkAsRead(1, new MarkAsReadRequest("ItemReply"), CancellationToken.None);

        // Assert
        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task MarkAsRead_Authorized_CallsService_ReturnsOk()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-123")], "test"));
        var controller = CreateController(user);

        _notificationServiceMock
            .Setup(s => s.MarkAsReadAsync("user-123", 1, "ItemReply"))
            .Returns(Task.CompletedTask);

        // Act
        ActionResult result = await controller.MarkAsRead(1, new MarkAsReadRequest("ItemReply"), CancellationToken.None);

        // Assert
        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task MarkAllAsRead_Unauthorized_ReturnsUnauthorized()
    {
        // Arrange
        var controller = CreateController(); // 未認証

        // Act
        ActionResult result = await controller.MarkAllAsRead(CancellationToken.None);

        // Assert
        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task MarkAllAsRead_Authorized_CallsService_ReturnsOk()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-123")], "test"));
        var controller = CreateController(user);

        _notificationServiceMock
            .Setup(s => s.MarkAllAsReadAsync("user-123"))
            .Returns(Task.CompletedTask);

        // Act
        ActionResult result = await controller.MarkAllAsRead(CancellationToken.None);

        // Assert
        Assert.IsType<OkResult>(result);
    }
}