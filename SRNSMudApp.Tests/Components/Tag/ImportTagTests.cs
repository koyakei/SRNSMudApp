#region

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

using Bunit;
using Moq;
using Bunit.TestDoubles;

using Microsoft.AspNetCore.Components.Forms;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.Tag;
using SRNSMudApp.Data;
using SRNSMudApp.Services;
using Xunit;

#endregion

namespace SRNSMudApp.Tests.Components.Tag;

// TestContextの継承をやめ、IAsyncDisposableを実装します
public class ImportTagTests : IAsyncDisposable
{
    private readonly TestContext _ctx;

    public ImportTagTests()
    {
        _ctx = new TestContext();

        // 継承元のプロパティではなく、_ctx のプロパティを使用するように変更
        _ = _ctx.Services.AddMudServices();
        var claims = new[] { new Claim(ClaimTypes.Name, "testuser"), new Claim(ClaimTypes.NameIdentifier, "testuser") };
        var identity = new ClaimsIdentity(claims, "TestAuthType");
        var claimsPrincipal = new ClaimsPrincipal(identity);
        var authState = new Microsoft.AspNetCore.Components.Authorization.AuthenticationState(claimsPrincipal);

        var authMock = new Moq.Mock<Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider>();
        authMock.Setup(p => p.GetAuthenticationStateAsync()).ReturnsAsync(authState);
        _ctx.Services.AddScoped<Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider>(_ => authMock.Object);
        _ctx.Services.AddAuthorizationCore();

        var dbName = Guid.NewGuid().ToString();
        _ = _ctx.Services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(dbName), ServiceLifetime.Scoped, ServiceLifetime.Singleton);
        _ = _ctx.Services.AddDbContextFactory<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(dbName));

        var tagEmbeddingServiceMock = new Mock<ITagEmbeddingService>();
        _ = _ctx.Services.AddScoped(sp => tagEmbeddingServiceMock.Object);

        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
    }

    // 非同期でTestContextを破棄し、MudBlazorの非同期サービスの例外を防ぐ
    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }

    [Fact]
    public async Task ImportButton_ShouldBeDisabled_WhenParentTagIsNotSelected_AndEnabled_WhenSelected()
    {
        // Arrange
        // Render ではなく _ctx.RenderComponent を使用します
        IRenderedComponent<ImportTag> component = _ctx.Render<ImportTag>();

        // Simulate file selection so the import button is rendered
        var fileMock = new Mock<IBrowserFile>();
        _ = fileMock.Setup(f => f.Name).Returns("test.csv");
        _ = fileMock.Setup(f => f.Size).Returns(100);

        IRenderedComponent<MudFileUpload<IBrowserFile>> fileUpload =
            component.FindComponent<MudFileUpload<IBrowserFile>>();
        await component.InvokeAsync(() => fileUpload.Instance.FilesChanged.InvokeAsync(fileMock.Object));

        component.Render();

        // Find the import button (the only Success colored button in this component)
        IReadOnlyList<IRenderedComponent<MudButton>> buttons = component.FindComponents<MudButton>();
        IRenderedComponent<MudButton>? importButton = buttons.FirstOrDefault(b => b.Instance.Color == Color.Success);

        Assert.NotNull(importButton);

        // Assert: Parent tag is initially not selected, button should be disabled
        Assert.True(importButton.Instance.Disabled);

        // Act: Select a parent tag
        IRenderedComponent<MudAutocomplete<SRNSMudApp.Data.Tag>> autocomplete =
            component.FindComponent<MudAutocomplete<SRNSMudApp.Data.Tag>>();
        var parentTag = new SRNSMudApp.Data.Tag { Id = 1, Name = "Parent Tag", OwnerId = "test" };
        await component.InvokeAsync(() => autocomplete.Instance.ValueChanged.InvokeAsync(parentTag));

        component.Render();

        // Assert: Parent tag is now selected, button should be enabled
        Assert.False(importButton.Instance.Disabled);
    }
}