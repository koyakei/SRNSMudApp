namespace SRNSMudApp.Tests.Push;

using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Moq;

using SRNSMudApp.Controllers;
using SRNSMudApp.Models.Push;
using SRNSMudApp.Services.Push;
using SRNSMudApp.Tests.TestSupport;

using Xunit;

public class PushNotificationAdversarialTests(TestWebApplicationFactory factory)
    : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory = factory;

    [Fact]
    public async Task Challenge_Subscribe_UnauthenticatedAttacker_SpoofsVictimUserId_DirectStoreExploit()
    {
        // 1. Arrange: 実際の InMemoryPushSubscriptionStore を使用してコントローラーを生成
        var realStore = new InMemoryPushSubscriptionStore();
        var mockPush = new Mock<IWebPushNotificationService>();
        var options = Options.Create(new VapidOptions());
        var controller = new PushNotificationController(realStore, mockPush.Object, options);

        // 未認証ユーザーとしてリクエスト
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) }
        };

        var attackerDto = new PushSubscriptionDto(
            "https://attacker.com/push/exploit-endpoint",
            new PushSubscriptionKeysDto("attacker-p256", "attacker-auth"),
            UserId: "victim-target-user-id" // 攻撃者が被害者のユーザーIDを指定
        );

        // 2. Act: 未認証ユーザーが Subscribe を呼ぶ
        var result = await controller.Subscribe(attackerDto, default) as OkObjectResult;
        Assert.NotNull(result);

        // 3. Assert: 攻撃対象の被害者ユーザーIDで検索した際、攻撃者のサブスクリプションが登録されていないこと
        var victimSubs = await realStore.GetByUserIdAsync("victim-target-user-id");
        Assert.Empty(victimSubs); // 脆弱性がある場合、ここで Fail する (victimSubs に攻撃者のサブスクリプションが含まれてしまう)

        var allSubs = await realStore.GetAllAsync();
        var stored = Assert.Single(allSubs);
        Assert.Null(stored.UserId);
    }

    [Fact]
    public async Task Challenge_Subscribe_UnauthenticatedAttacker_SpoofsVictimUserId_EndToEndHttpExploit()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IPushSubscriptionStore>();
        var client = _factory.CreateClient();

        var attackerDto = new PushSubscriptionDto(
            "https://attacker.com/push/http-exploit-endpoint",
            new PushSubscriptionKeysDto("attacker-p256", "attacker-auth"),
            UserId: "e2e-victim-user-id"
        );

        // Act: 匿名HTTPクライアントから Subscribe API を呼び出す
        var response = await client.PostAsJsonAsync("/api/pushnotification/subscribe", attackerDto);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Assert: 被害者のユーザーIDに攻撃者のサブスクリプションが紐付いていないこと
        var victimSubs = await store.GetByUserIdAsync("e2e-victim-user-id");
        Assert.Empty(victimSubs);
    }

    [Fact]
    public async Task Challenge_SendNotification_UnauthenticatedCaller_ReturnsUnauthorizedOrForbidden()
    {
        // Arrange: 匿名クライアント
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var payload = new PushNotificationPayload("Adversarial Title", "Adversarial Body");

        // Act
        var response = await client.PostAsJsonAsync("/api/pushnotification/send", payload);

        // Assert: 匿名リクエストは拒絶されること (401 Unauthorized または 403 Forbidden または 302 Redirect to Login)
        Assert.True(
            response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.Redirect,
            $"Expected 401, 403, or 302 redirect, but received: {response.StatusCode}");
    }

    [Fact]
    public void PushNotificationController_SendNotification_RequiresAdminRoleSpecifically()
    {
        var method = typeof(PushNotificationController).GetMethod(nameof(PushNotificationController.SendNotification));
        Assert.NotNull(method);

        var authorizeAttr = System.Reflection.CustomAttributeExtensions.GetCustomAttribute<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>(method);
        Assert.NotNull(authorizeAttr);
        Assert.Equal("Admin", authorizeAttr.Roles);
    }
}