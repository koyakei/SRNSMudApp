namespace SRNSMudApp.Tests.Push;

using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Moq;

using SRNSMudApp.Controllers;
using SRNSMudApp.Models.Push;
using SRNSMudApp.Services.Push;

using WebPush;

using Xunit;

public class PushNotificationTests
{
    [Fact]
    public void VapidHelper_CanGenerateValidKeys()
    {
        // Act
        var keys = VapidHelper.GenerateVapidKeys();

        // Assert
        Assert.False(string.IsNullOrWhiteSpace(keys.PublicKey));
        Assert.False(string.IsNullOrWhiteSpace(keys.PrivateKey));
    }

    [Fact]
    public async Task InMemoryPushSubscriptionStore_AddAndRetrieve_WorksCorrectly()
    {
        // Arrange
        var store = new InMemoryPushSubscriptionStore();
        var dto = new PushSubscriptionDto(
            "https://fcm.googleapis.com/fcm/send/sample-token",
            new PushSubscriptionKeysDto("p256dh-key", "auth-key")
        );

        // Act
        await store.AddOrUpdateAsync(dto);
        var all = await store.GetAllAsync();

        // Assert
        Assert.Single(all);
        Assert.Contains(all, s => s.Endpoint == dto.Endpoint);

        // Remove
        await store.RemoveAsync(dto.Endpoint);
        var afterRemove = await store.GetAllAsync();
        Assert.Empty(afterRemove);
    }

    [Fact]
    public async Task PushNotificationController_GetVapidPublicKey_ReturnsConfiguredKey()
    {
        // Arrange
        var mockStore = new Mock<IPushSubscriptionStore>();
        var mockPush = new Mock<IWebPushNotificationService>();
        var options = Options.Create(new VapidOptions
        {
            Subject = "mailto:admin@example.com",
            PublicKey = "test-public-key",
            PrivateKey = "test-private-key"
        });

        var controller = new PushNotificationController(mockStore.Object, mockPush.Object, options);

        // Act
        var result = controller.GetVapidPublicKey() as OkObjectResult;

        // Assert
        Assert.NotNull(result);
        var val = result.Value;
        Assert.NotNull(val);
        var property = val.GetType().GetProperty("publicKey");
        Assert.Equal("test-public-key", property?.GetValue(val));
    }

    [Fact]
    public async Task PushNotificationController_Subscribe_ValidPayload_ReturnsOk()
    {
        // Arrange
        var store = new InMemoryPushSubscriptionStore();
        var mockPush = new Mock<IWebPushNotificationService>();
        var options = Options.Create(new VapidOptions
        {
            Subject = "mailto:admin@example.com",
            PublicKey = "test-public-key",
            PrivateKey = "test-private-key"
        });

        var controller = new PushNotificationController(store, mockPush.Object, options);
        var dto = new PushSubscriptionDto(
            "https://example.com/push/123",
            new PushSubscriptionKeysDto("key1", "key2")
        );

        // Act
        var result = await controller.Subscribe(dto, default) as OkObjectResult;

        // Assert
        Assert.NotNull(result);
        var stored = await store.GetAllAsync();
        Assert.Single(stored);
    }

    [Fact]
    public async Task PushNotificationController_Subscribe_InvalidPayload_ReturnsBadRequest()
    {
        // Arrange
        var store = new InMemoryPushSubscriptionStore();
        var mockPush = new Mock<IWebPushNotificationService>();
        var options = Options.Create(new VapidOptions());
        var controller = new PushNotificationController(store, mockPush.Object, options);

        // Act
        var result = await controller.Subscribe(new PushSubscriptionDto("", null!), default);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task PushNotificationController_Send_ValidPayload_ReturnsOk()
    {
        // Arrange
        var mockStore = new Mock<IPushSubscriptionStore>();
        var mockPush = new Mock<IWebPushNotificationService>();
        mockPush.Setup(p => p.SendNotificationToAllAsync(It.IsAny<PushNotificationPayload>(), default))
            .ReturnsAsync(new PushSendResult(1, 0, 0));

        var options = Options.Create(new VapidOptions());
        var controller = new PushNotificationController(mockStore.Object, mockPush.Object, options);

        // Act
        var result = await controller.SendNotification(new PushNotificationPayload("Title", "Body"), default) as OkObjectResult;

        // Assert
        Assert.NotNull(result);
        mockPush.Verify(p => p.SendNotificationToAllAsync(It.IsAny<PushNotificationPayload>(), default), Times.Once);
    }

    [Fact]
    public async Task WebPushNotificationService_SendNotificationToAllAsync_NoSubscriptions_ReturnsZero()
    {
        // Arrange
        var store = new InMemoryPushSubscriptionStore();
        var options = Options.Create(new VapidOptions
        {
            Subject = "mailto:test@example.com",
            PublicKey = "pub",
            PrivateKey = "priv"
        });
        var service = new WebPushNotificationService(store, options, NullLogger<WebPushNotificationService>.Instance);

        // Act
        var result = await service.SendNotificationToAllAsync(new PushNotificationPayload("T", "B"));

        // Assert
        Assert.Equal(0, result.SucceededCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Equal(0, result.ExpiredCount);
    }
}