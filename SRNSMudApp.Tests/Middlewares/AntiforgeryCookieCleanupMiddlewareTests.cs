#region

using System.Security.Cryptography;
using System.Text;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

using SRNSMudApp.Middlewares;

#endregion

namespace SRNSMudApp.Tests.Middlewares;

public class AntiforgeryCookieCleanupMiddlewareTests
{
    private const string AntiforgeryPurpose = "Microsoft.AspNetCore.Antiforgery.AntiforgeryToken.v1";
    private const string CookieName = ".AspNetCore.Antiforgery.TestCookie";

    [Fact]
    public async Task InvokeAsync_WithValidAntiforgeryCookie_RetainsCookieAndDoesNotDelete()
    {
        // Arrange
        ServiceCollection services = new();
        _ = services.AddDataProtection();
        _ = services.Configure<AntiforgeryOptions>(o => o.Cookie.Name = CookieName);
        ServiceProvider serviceProvider = services.BuildServiceProvider();

        IDataProtectionProvider dataProtectionProvider = serviceProvider.GetRequiredService<IDataProtectionProvider>();
        IDataProtector protector = dataProtectionProvider.CreateProtector(AntiforgeryPurpose);

        byte[] payload = Encoding.UTF8.GetBytes("valid-token-data");
        byte[] protectedBytes = protector.Protect(payload);
        string cookieValue = WebEncoders.Base64UrlEncode(protectedBytes);

        DefaultHttpContext context = new()
        {
            RequestServices = serviceProvider
        };
        context.Request.Headers[HeaderNames.Cookie] = $"{CookieName}={cookieValue}";

        var nextCalled = false;
        RequestDelegate next = ctx =>
        {
            nextCalled = true;
            Assert.True(ctx.Request.Cookies.ContainsKey(CookieName));
            Assert.Equal(cookieValue, ctx.Request.Cookies[CookieName]);
            return Task.CompletedTask;
        };

        var middleware = new AntiforgeryCookieCleanupMiddleware(next, NullLogger<AntiforgeryCookieCleanupMiddleware>.Instance);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled);
        // レスポンスに Set-Cookie (削除指示) が含まれていないこと
        Assert.False(context.Response.Headers.ContainsKey(HeaderNames.SetCookie));
    }

    [Fact]
    public async Task InvokeAsync_WithInvalidAntiforgeryCookie_DeletesCookieAndRemovesFromRequest()
    {
        // Arrange
        var serverDir = Directory.CreateTempSubdirectory();
        var foreignDir = Directory.CreateTempSubdirectory();
        try
        {
            // サーバー側の DataProtectionProvider
            ServiceCollection serverServices = new();
            _ = serverServices.AddDataProtection().PersistKeysToFileSystem(serverDir);
            _ = serverServices.Configure<AntiforgeryOptions>(o => o.Cookie.Name = CookieName);
            ServiceProvider serverProvider = serverServices.BuildServiceProvider();

            // 別の独立した DataProtectionProvider（過去のコンテナや別インスタンスを模倣）
            ServiceCollection foreignServices = new();
            _ = foreignServices.AddDataProtection().PersistKeysToFileSystem(foreignDir);
            ServiceProvider foreignProvider = foreignServices.BuildServiceProvider();
            IDataProtector foreignProtector = foreignProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector(AntiforgeryPurpose);

            // 別のキーで暗号化されたトークン
            byte[] payload = Encoding.UTF8.GetBytes("old-container-token");
            byte[] foreignProtectedBytes = foreignProtector.Protect(payload);
            string invalidCookieValue = WebEncoders.Base64UrlEncode(foreignProtectedBytes);

            DefaultHttpContext context = new()
            {
                RequestServices = serverProvider
            };
            context.Request.Headers[HeaderNames.Cookie] = $"{CookieName}={invalidCookieValue}; otherCookie=123";

            var nextCalled = false;
            RequestDelegate next = ctx =>
            {
                nextCalled = true;
                // リクエスト Cookie から無効な Antiforgery Cookie が除外されていること
                Assert.False(ctx.Request.Cookies.ContainsKey(CookieName));
                // その他の Cookie は残っていること
                Assert.True(ctx.Request.Cookies.ContainsKey("otherCookie"));
                Assert.Equal("123", ctx.Request.Cookies["otherCookie"]);

                // IRequestCookiesFeature も同期されていること
                IRequestCookiesFeature? feature = ctx.Features.Get<IRequestCookiesFeature>();
                Assert.NotNull(feature);
                Assert.False(feature.Cookies.ContainsKey(CookieName));
                Assert.True(feature.Cookies.ContainsKey("otherCookie"));

                return Task.CompletedTask;
            };

            var middleware = new AntiforgeryCookieCleanupMiddleware(next, NullLogger<AntiforgeryCookieCleanupMiddleware>.Instance);

            // Act
            await middleware.InvokeAsync(context);

            // Assert
            Assert.True(nextCalled);
            // レスポンスに Set-Cookie (削除指示) が設定されていること
            Assert.True(context.Response.Headers.ContainsKey(HeaderNames.SetCookie));
            var setCookieHeader = context.Response.Headers[HeaderNames.SetCookie].ToString();
            Assert.Contains(CookieName, setCookieHeader, StringComparison.Ordinal);
            Assert.Contains("expires=Thu, 01 Jan 1970 00:00:00 GMT", setCookieHeader, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            serverDir.Delete(true);
            foreignDir.Delete(true);
        }
    }

    [Fact]
    public async Task InvokeAsync_WithOldHashAntiforgeryCookie_DeletesOldCookieAndRemovesFromRequest()
    {
        // Arrange: 以前のコンテナや異なる環境で発行された別ハッシュの .AspNetCore.Antiforgery.* クッキー
        const string oldCookieName = ".AspNetCore.Antiforgery.OldDifferentHash123";

        var serverDir = Directory.CreateTempSubdirectory();
        var foreignDir = Directory.CreateTempSubdirectory();
        try
        {
            ServiceCollection serverServices = new();
            _ = serverServices.AddDataProtection().PersistKeysToFileSystem(serverDir);
            _ = serverServices.Configure<AntiforgeryOptions>(o => o.Cookie.Name = CookieName);
            ServiceProvider serverProvider = serverServices.BuildServiceProvider();

            ServiceCollection foreignServices = new();
            _ = foreignServices.AddDataProtection().PersistKeysToFileSystem(foreignDir);
            ServiceProvider foreignProvider = foreignServices.BuildServiceProvider();
            IDataProtector foreignProtector = foreignProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector(AntiforgeryPurpose);

            byte[] payload = Encoding.UTF8.GetBytes("unrecoverable-old-token");
            byte[] foreignProtectedBytes = foreignProtector.Protect(payload);
            string invalidCookieValue = WebEncoders.Base64UrlEncode(foreignProtectedBytes);

            DefaultHttpContext context = new()
            {
                RequestServices = serverProvider
            };
            context.Request.Headers[HeaderNames.Cookie] = $"{oldCookieName}={invalidCookieValue}; keepMe=abc";

            var nextCalled = false;
            RequestDelegate next = ctx =>
            {
                nextCalled = true;
                Assert.False(ctx.Request.Cookies.ContainsKey(oldCookieName));
                Assert.True(ctx.Request.Cookies.ContainsKey("keepMe"));
                Assert.Equal("abc", ctx.Request.Cookies["keepMe"]);
                return Task.CompletedTask;
            };

            var middleware = new AntiforgeryCookieCleanupMiddleware(next, NullLogger<AntiforgeryCookieCleanupMiddleware>.Instance);

            // Act
            await middleware.InvokeAsync(context);

            // Assert
            Assert.True(nextCalled);
            Assert.True(context.Response.Headers.ContainsKey(HeaderNames.SetCookie));
            var setCookieHeader = context.Response.Headers[HeaderNames.SetCookie].ToString();
            Assert.Contains(oldCookieName, setCookieHeader, StringComparison.Ordinal);
        }
        finally
        {
            serverDir.Delete(true);
            foreignDir.Delete(true);
        }
    }

    [Fact]
    public async Task InvokeAsync_WithoutAntiforgeryCookie_PassesThroughUnchanged()
    {
        // Arrange
        ServiceCollection services = new();
        _ = services.AddDataProtection();
        ServiceProvider serviceProvider = services.BuildServiceProvider();

        DefaultHttpContext context = new()
        {
            RequestServices = serviceProvider
        };
        context.Request.Headers[HeaderNames.Cookie] = "session=abc";

        var nextCalled = false;
        RequestDelegate next = ctx =>
        {
            nextCalled = true;
            Assert.Equal("abc", ctx.Request.Cookies["session"]);
            return Task.CompletedTask;
        };

        var middleware = new AntiforgeryCookieCleanupMiddleware(next, NullLogger<AntiforgeryCookieCleanupMiddleware>.Instance);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled);
        Assert.False(context.Response.Headers.ContainsKey(HeaderNames.SetCookie));
    }
}