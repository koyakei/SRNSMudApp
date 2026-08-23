#region

using System;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;

using Bunit;
using Bunit.TestDoubles;

using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor.Services;

using SRNSMudApp.Components.Tag;
using SRNSMudApp.Data;
using SRNSMudApp.Tests.TestSupport;

using Xunit;

#endregion

namespace SRNSMudApp.Tests.Components.Tag;

// BunitContextの継承をやめ、IAsyncDisposableを実装します
public class TagListConcurrencyTests : IAsyncDisposable
{
    private readonly BunitContext _ctx;

    public TagListConcurrencyTests()
    {
        _ctx = new BunitContext();

        // 認証モック・MudServices・アプリ側サービスは BunitTestSetup に集約
        _ = _ctx.Services.AddAuth("testuser");
        _ = _ctx.Services.AddSrnsComponentServices();

        // TagDialogDataProvider が依存する埋め込みサービスのモック
        var embeddingMock = new Moq.Mock<SRNSMudApp.Services.ITagEmbeddingService>();
        embeddingMock.Setup(s => s.GenerateEmbeddingAsync(It.IsAny<string>())).ReturnsAsync(Array.Empty<float>());
        _ctx.Services.AddScoped(_ => embeddingMock.Object);

        // Setup In-Memory database
        var dbName = Guid.NewGuid().ToString();
        _ = _ctx.Services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(dbName), ServiceLifetime.Scoped, ServiceLifetime.Singleton);

        _ = _ctx.Services.AddDbContextFactory<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(dbName));

        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
    }

    // 非同期でBunitContextを破棄し、MudBlazorの非同期サービスの例外を防ぐ
    public async ValueTask DisposeAsync() => await _ctx.DisposeAsync();

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