namespace SRNSMudApp.Tests.Push;

using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc.Testing;

using SRNSMudApp.Models.Push;
using SRNSMudApp.Tests.TestSupport;

using Xunit;

public class PushNotificationIntegrationTests(TestWebApplicationFactory factory)
    : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GetRoot_ReturnsSuccess()
    {
        // Act
        var response = await _client.GetAsync("/");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.True(response.IsSuccessStatusCode, $"Status: {response.StatusCode}, Content: {content}");
    }

    [Fact]
    public async Task GetManifestJson_ReturnsSuccessAndValidContent()
    {
        // Act
        var response = await _client.GetAsync("/manifest.json");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("SRNS", content);
        Assert.Contains("standalone", content);
    }

    [Fact]
    public async Task GetServiceWorker_ReturnsSuccessAndScriptContent()
    {
        // Act
        var response = await _client.GetAsync("/sw.js");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("srns-pwa-cache", content);
    }

    [Fact]
    public async Task GetVapidPublicKey_ReturnsOkWithKey()
    {
        // Act
        var response = await _client.GetAsync("/api/pushnotification/vapid-public-key");

        var content = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("publicKey", content);
    }

    [Fact]
    public async Task SubscribeAndSend_EndToEnd_Succeeds()
    {
        // 1. Subscribe
        var subDto = new PushSubscriptionDto(
            "https://fcm.googleapis.com/fcm/send/integration-test-endpoint",
            new PushSubscriptionKeysDto("p256dh-sample-key", "auth-sample-key")
        );

        var subResponse = await _client.PostAsJsonAsync("/api/pushnotification/subscribe", subDto);
        Assert.Equal(HttpStatusCode.OK, subResponse.StatusCode);

        // 2. Unsubscribe
        var unsubResponse = await _client.PostAsJsonAsync("/api/pushnotification/unsubscribe",
            new PushUnsubscribeRequest(subDto.Endpoint));
        Assert.Equal(HttpStatusCode.OK, unsubResponse.StatusCode);
    }
}