using System.Net;
using System.Net.Http.Json;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services.Auth;
using SRNSMudApp.Tests.TestSupport;

namespace SRNSMudApp.Tests.Auth;

public class ExternalLoginCallbackIntegrationTests(TestWebApplicationFactory factory)
    : IClassFixture<TestWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> _factory = factory.WithWebHostBuilder(builder =>
    {
        _ = builder.ConfigureServices(services =>
        {
            _ = services.RemoveAll<IExternalTokenVerificationService>();
            _ = services.AddScoped<IExternalTokenVerificationService, TestExternalTokenVerificationService>();
        });
    });

    private sealed class TestExternalTokenVerificationService : IExternalTokenVerificationService
    {
        public Task<Result<ExternalTokenPayload>> VerifyTokenAsync(
            string provider,
            string token,
            CancellationToken cancellationToken = default)
        {
            if (token.StartsWith("mock-"))
            {
                var providerKey = $"{token.Replace("mock-", "")}-id";
                var email = $"{token.Replace("mock-", "")}@example.com";
                var payload = new ExternalTokenPayload(email, providerKey);
                return Task.FromResult<Result<ExternalTokenPayload>>(new Success<ExternalTokenPayload>(payload));
            }

            return Task.FromResult<Result<ExternalTokenPayload>>(new Failure("Invalid mock token"));
        }
    }

    private HttpClient CreateCustomClient()
    {
        return _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [Theory]
    [InlineData("Google", "mock-google-test")]
    [InlineData("Line", "mock-line-test")]
    [InlineData("Github", "mock-github-test")]
    public async Task ExternalLogin_WithValidMockToken_ReturnsOkAndIssuesAuthCookie(string provider, string token)
    {
        // Arrange
        using HttpClient client = CreateCustomClient();
        var requestPayload = new
        {
            Provider = provider,
            Token = token
        };

        // Act: POST /api/auth/external-login
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/auth/external-login", requestPayload);

        // Assert: 200 OK が返り、Cookie が発行される
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // レスポンスヘッダーに Set-Cookie が含まれること（ASP.NET Core Identity Cookie）
        Assert.True(response.Headers.Contains("Set-Cookie"), "Set-Cookie header should be present after successful external login");
        var cookieHeader = string.Join(";", response.Headers.GetValues("Set-Cookie"));
        Assert.Contains(".AspNetCore.Identity.Application", cookieHeader);
    }

    [Fact]
    public async Task ExternalLogin_WithInvalidToken_ReturnsUnauthorized()
    {
        // Arrange
        using HttpClient client = CreateCustomClient();
        var requestPayload = new
        {
            Provider = "Google",
            Token = "invalid-token"
        };

        // Act
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/auth/external-login", requestPayload);

        // Assert: 401 Unauthorized
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ExternalLogin_WhenUserIsBanned_ReturnsForbiddenAndNoAuthCookie()
    {
        // Arrange
        using HttpClient client = CreateCustomClient();

        var uniqueKey = Guid.NewGuid().ToString("N")[..8];
        var token = $"mock-ban-{uniqueKey}";
        var expectedEmail = $"ban-{uniqueKey}@example.com";
        var requestPayload = new
        {
            Provider = "Google",
            Token = token
        };

        // 1回目のログインでユーザー作成
        HttpResponseMessage firstResponse = await client.PostAsJsonAsync("/api/auth/external-login", requestPayload);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        // データベースから対象ユーザーを取得し、BAN状態にする
        using (var scope = _factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByEmailAsync(expectedEmail);
            Assert.NotNull(user);
            user.IsBanned = true;
            user.LockoutEnabled = true;
            user.LockoutEnd = DateTimeOffset.MaxValue;
            _ = await userManager.UpdateSecurityStampAsync(user);
            _ = await userManager.UpdateAsync(user);
        }

        // Act: BANされた後に再度ログインを試みる
        HttpResponseMessage bannedResponse = await client.PostAsJsonAsync("/api/auth/external-login", requestPayload);

        // Assert: 403 Forbidden が返り、Cookie は発行されない
        Assert.Equal(HttpStatusCode.Forbidden, bannedResponse.StatusCode);
        Assert.False(bannedResponse.Headers.Contains("Set-Cookie"));
    }

    private sealed class TestFirstUserAdminService(
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager) : IFirstUserAdminService
    {
        public async Task GrantAdminIfFirstUserAsync(string userId, CancellationToken cancellationToken = default)
        {
            var user = await userManager.FindByIdAsync(userId);
            if (user != null)
            {
                if (!await roleManager.RoleExistsAsync("Admin"))
                {
                    _ = await roleManager.CreateAsync(new IdentityRole("Admin"));
                }
                _ = await userManager.AddToRoleAsync(user, "Admin");
            }
        }
    }

    [Fact]
    public async Task ExternalLogin_FirstRegisteredUser_IsGrantedAdminRole()
    {
        // Arrange: 初回ユーザーとして Admin が付与されるフローを検証する
        using var customFactory = _factory.WithWebHostBuilder(builder =>
        {
            _ = builder.ConfigureServices(services =>
            {
                _ = services.RemoveAll<IFirstUserAdminService>();
                _ = services.AddScoped<IFirstUserAdminService, TestFirstUserAdminService>();
            });
        });

        using HttpClient client = customFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var uniqueKey = Guid.NewGuid().ToString("N")[..8];
        var token = $"mock-firstadmin-{uniqueKey}";
        var expectedEmail = $"firstadmin-{uniqueKey}@example.com";
        var requestPayload = new { Provider = "Google", Token = token };

        // 事前に同じメールアドレスのユーザーが存在しないことを確認
        using (var scope = customFactory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var preExisting = await userManager.FindByEmailAsync(expectedEmail);
            Assert.Null(preExisting);
        }

        // Act: 登録
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/auth/external-login", requestPayload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Assert: 登録後に Admin ロールが付与されていること
        using (var scope = customFactory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByEmailAsync(expectedEmail);
            Assert.NotNull(user);
            var isAdmin = await userManager.IsInRoleAsync(user, "Admin");
            Assert.True(isAdmin, $"最初に登録された一般ユーザー ({expectedEmail}) は Admin ロールを持つべきです。");
        }
    }

    [Fact]
    public async Task ExternalLogin_SecondRegisteredUser_IsNotGrantedAdminRole()
    {
        // Arrange: 1人目を登録して Admin にした後、2人目は Admin にならないことを確認
        using HttpClient client = CreateCustomClient();

        var uniqueKey = Guid.NewGuid().ToString("N")[..8];

        // 1人目
        var firstToken = $"mock-first-{uniqueKey}";
        var firstEmail = $"first-{uniqueKey}@example.com";
        HttpResponseMessage firstResponse = await client.PostAsJsonAsync(
            "/api/auth/external-login",
            new { Provider = "Google", Token = firstToken });
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        // 2人目
        var secondToken = $"mock-second-{uniqueKey}";
        var secondEmail = $"second-{uniqueKey}@example.com";
        HttpResponseMessage secondResponse = await client.PostAsJsonAsync(
            "/api/auth/external-login",
            new { Provider = "Google", Token = secondToken });
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);

        // Assert: 2人目は Admin ロールを持たないこと
        using (var scope = _factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var secondUser = await userManager.FindByEmailAsync(secondEmail);
            Assert.NotNull(secondUser);
            var isAdmin = await userManager.IsInRoleAsync(secondUser, "Admin");
            Assert.False(isAdmin, $"2番目に登録されたユーザー ({secondEmail}) は Admin ロールを持つべきではありません。");
        }
    }
}