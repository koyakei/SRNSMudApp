#region

using System.Security.Claims;

using Bunit;
using Moq;
using Bunit.TestDoubles;

using Microsoft.AspNetCore.Components.Forms;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.Tag;
using SRNSMudApp.Data;
using SRNSMudApp.Services;

#endregion

namespace SRNSMudApp.Tests.Components.Tag;

public class ImportTagTests : TestContext
{
    public ImportTagTests()
    {
        _ = Services.AddMudServices();
        var claims = new[] { new Claim(ClaimTypes.Name, "testuser"), new Claim(ClaimTypes.NameIdentifier, "testuser") };
        var identity = new ClaimsIdentity(claims, "TestAuthType");
        var claimsPrincipal = new ClaimsPrincipal(identity);
        var authState = new Microsoft.AspNetCore.Components.Authorization.AuthenticationState(claimsPrincipal);

        var authMock = new Moq.Mock<Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider>();
        authMock.Setup(p => p.GetAuthenticationStateAsync()).ReturnsAsync(authState);
        Services.AddScoped<Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider>(_ => authMock.Object);
        Services.AddAuthorizationCore();

        var dbName = Guid.NewGuid().ToString();
        _ = Services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(dbName), ServiceLifetime.Scoped, ServiceLifetime.Singleton);
        _ = Services.AddDbContextFactory<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(dbName));

        var tagEmbeddingServiceMock = new Mock<ITagEmbeddingService>();
        _ = Services.AddScoped(sp => tagEmbeddingServiceMock.Object);

        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public async Task ImportButton_ShouldBeDisabled_WhenParentTagIsNotSelected_AndEnabled_WhenSelected()
    {
        // Arrange
        IRenderedComponent<ImportTag> component = RenderComponent<ImportTag>();

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