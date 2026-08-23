#region

using System;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;

using Bunit;

using SRNSMudApp.Tests.TestSupport;
using Moq;
using Bunit.TestDoubles;

using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using MudBlazor.Services;

using SRNSMudApp.Components.Tag;
using SRNSMudApp.Data;
using Xunit;

#endregion

namespace SRNSMudApp.Tests.Components.Tag;

// TestContextの継承をやめ、IAsyncDisposableを実装します
public class TagListConcurrencyTests : IAsyncDisposable
{
    private readonly TestContext _ctx;

    public TagListConcurrencyTests()
    {
        _ctx = new TestContext();

        // Add MudBlazor services
        // 継承元のプロパティではなく、_ctx のプロパティを使用するように変更
        _ = _ctx.Services.AddMudServices().AddSrnsComponentServices();

        // TagDialogDataProvider が依存する埋め込みサービスのモック
        var embeddingMock = new Moq.Mock<SRNSMudApp.Services.ITagEmbeddingService>();
        embeddingMock.Setup(s => s.GenerateEmbeddingAsync(It.IsAny<string>())).ReturnsAsync(Array.Empty<float>());
        _ctx.Services.AddScoped(_ => embeddingMock.Object);

        // Setup mock authentication
        var claims = new[] { new Claim(ClaimTypes.Name, "testuser"), new Claim(ClaimTypes.NameIdentifier, "testuser") };
        var identity = new ClaimsIdentity(claims, "TestAuthType");
        var claimsPrincipal = new ClaimsPrincipal(identity);
        var authState = new Microsoft.AspNetCore.Components.Authorization.AuthenticationState(claimsPrincipal);

        var authMock = new Moq.Mock<Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider>();
        authMock.Setup(p => p.GetAuthenticationStateAsync()).ReturnsAsync(authState);
        _ctx.Services.AddScoped<Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider>(_ => authMock.Object);
        _ctx.Services.AddAuthorizationCore();

        // Setup In-Memory database
        var dbName = Guid.NewGuid().ToString();
        _ = _ctx.Services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(dbName), ServiceLifetime.Scoped, ServiceLifetime.Singleton);

        _ = _ctx.Services.AddDbContextFactory<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(dbName));

        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
    }

    // 非同期でTestContextを破棄し、MudBlazorの非同期サービスの例外を防ぐ
    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }

    [Fact]
    public void RenderingTagList_ShouldNotThrowConcurrencyException()
    {
        // Act & Assert
        // If the components share the same Scoped DbContext and perform async DB operations simultaneously,
        // this will throw an InvalidOperationException from Entity Framework Core.
        // By using IDbContextFactory internally, this error is avoided.
        Exception? exception = Record.Exception(() =>
        {
            // Render ではなく、_ctx.Render<T>() を使用します
            _ = _ctx.Render<TagList>();
        });

        // Verify that no exception was thrown
        Assert.Null(exception);
    }

    [Theory]
    [InlineData(typeof(TagList))]
    [InlineData(typeof(TagTable))]
    public void Component_ShouldNotInjectDbContextDirectly_ToPreventConcurrencyIssues(Type componentType)
    {
        // Arrange
        PropertyInfo[] properties =
            componentType.GetProperties(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        // Act
        var hasDirectDbContextInjection = properties.Any(p =>
            p.PropertyType == typeof(ApplicationDbContext) &&
            p.GetCustomAttributes(typeof(InjectAttribute), true).Length != 0);

        // Assert
        Assert.False(hasDirectDbContextInjection,
            $"{componentType.Name} should inject IDbContextFactory<ApplicationDbContext> instead of ApplicationDbContext directly to prevent InvalidOperationException during concurrent rendering.");
    }
}