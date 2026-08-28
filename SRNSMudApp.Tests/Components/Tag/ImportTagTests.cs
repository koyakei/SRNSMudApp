using System.Text;

using AngleSharp.Dom;

using Bunit;

using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.Tag;
using SRNSMudApp.Services;

namespace SRNSMudApp.Tests.Components.Tag;

public sealed class ImportTagTests : IAsyncLifetime
{
    private readonly BunitContext _ctx = new();
    private readonly Mock<IImportTagDataProvider> _providerMock = new();

    public ImportTagTests()
    {
        _ = _ctx.Services.AddMudServices().AddMockSrnsServices();
        _ = _ctx.Services.AddScoped(_ => _providerMock.Object);
        _ = _ctx.Services.AddAuth("testuser");
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Render<MudPopoverProvider>();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ImportButton_ShouldBeDisabled_WhenParentTagIsNotSelected_AndEnabled_WhenSelected()
    {
        var authStateTask = Task.FromResult(BunitTestSetup.CreateAuthState("testuser"));
        IRenderedComponent<ImportTag> component = _ctx.Render<ImportTag>(p => p.AddCascadingValue(authStateTask));

        // Simulate file selection so the import button is rendered
        var fileMock = new Mock<IBrowserFile>();
        _ = fileMock.Setup(f => f.Name).Returns("test.csv");
        _ = fileMock.Setup(f => f.Size).Returns(100);

        IRenderedComponent<MudFileUpload<IBrowserFile>> fileUpload =
            component.FindComponent<MudFileUpload<IBrowserFile>>();
        await component.InvokeAsync(() => fileUpload.Instance.FilesChanged.InvokeAsync(fileMock.Object));

        component.Render();

        IReadOnlyList<IRenderedComponent<MudButton>> buttons = component.FindComponents<MudButton>();
        IRenderedComponent<MudButton>? importButton = buttons.FirstOrDefault(b => b.Instance.Color == Color.Success);

        Assert.NotNull(importButton);
        Assert.True(importButton.Instance.Disabled);

        // Act: Select a parent tag
        IRenderedComponent<MudAutocomplete<SRNSMudApp.Data.Tag>> autocomplete =
            component.FindComponent<MudAutocomplete<SRNSMudApp.Data.Tag>>();
        var parentTag = new SRNSMudApp.Data.Tag { Id = 1, Name = "Parent Tag", OwnerId = "testuser" };
        await component.InvokeAsync(() => autocomplete.Instance.ValueChanged.InvokeAsync(parentTag));

        component.Render();

        Assert.False(importButton.Instance.Disabled);
    }

    [Fact]
    public async Task ImportCsv_WhenClicked_CallsDataProviderImportCsvTagsAsync()
    {
        var rootTag = new SRNSMudApp.Data.Tag { Id = 1, Name = "RootTag", OwnerId = "testuser" };
        const string csvContent = "tag1,tag2";
        var fileMock = new Mock<IBrowserFile>();
        _ = fileMock.Setup(f => f.Name).Returns("tags.csv");
        _ = fileMock.Setup(f => f.Size).Returns(100);
        _ = fileMock.Setup(f => f.OpenReadStream(It.IsAny<long>(), default))
            .Returns(new MemoryStream(Encoding.UTF8.GetBytes(csvContent)));

        _ = _providerMock
            .Setup(p => p.ImportCsvTagsAsync("testuser", rootTag.Name, csvContent, false))
            .ReturnsAsync(2);

        var authStateTask = Task.FromResult(BunitTestSetup.CreateAuthState("testuser"));
        IRenderedComponent<ImportTag> component = _ctx.Render<ImportTag>(p => p.AddCascadingValue(authStateTask));

        IRenderedComponent<MudAutocomplete<SRNSMudApp.Data.Tag>> autocomplete =
            component.FindComponent<MudAutocomplete<SRNSMudApp.Data.Tag>>();
        await component.InvokeAsync(() => autocomplete.Instance.ValueChanged.InvokeAsync(rootTag));

        IRenderedComponent<MudFileUpload<IBrowserFile>> fileUpload =
            component.FindComponent<MudFileUpload<IBrowserFile>>();
        await component.InvokeAsync(() => fileUpload.Instance.FilesChanged.InvokeAsync(fileMock.Object));

        component.Render();

        IReadOnlyList<IRenderedComponent<MudButton>> buttons = component.FindComponents<MudButton>();
        IRenderedComponent<MudButton>? importButton = buttons.FirstOrDefault(b => b.Instance.Color == Color.Success);
        Assert.NotNull(importButton);
        await component.InvokeAsync(() => importButton.Find("button").Click());

        _providerMock.Verify(p => p.ImportCsvTagsAsync("testuser", rootTag.Name, csvContent, false), Times.Once);
    }

    [Fact]
    public void NonAdminUser_DoesNotShowSystemTagImportSwitch()
    {
        var authStateTask = Task.FromResult(BunitTestSetup.CreateAuthState("testuser"));
        IRenderedComponent<ImportTag> component = _ctx.Render<ImportTag>(p => p.AddCascadingValue(authStateTask));

        IReadOnlyList<IRenderedComponent<MudSwitch<bool>>> switches =
            component.FindComponents<MudSwitch<bool>>();
        Assert.Empty(switches);
    }

    [Fact]
    public async Task AdminUser_ShowsSystemTagImportSwitch_AndPassesAsSystemTrue()
    {
        await using var adminCtx = new BunitContext();
        _ = adminCtx.Services.AddMudServices().AddMockSrnsServices();
        _ = adminCtx.Services.AddScoped(_ => _providerMock.Object);
        _ = adminCtx.Services.AddAuth("adminuser", "Admin");
        adminCtx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = adminCtx.Render<MudPopoverProvider>();

        var rootTag = new SRNSMudApp.Data.Tag { Id = 10, Name = "AdminRoot", OwnerId = "adminuser" };
        const string csvContent = "admin1,admin2";
        var fileMock = new Mock<IBrowserFile>();
        _ = fileMock.Setup(f => f.Name).Returns("tags.csv");
        _ = fileMock.Setup(f => f.Size).Returns(100);
        _ = fileMock.Setup(f => f.OpenReadStream(It.IsAny<long>(), default))
            .Returns(new MemoryStream(Encoding.UTF8.GetBytes(csvContent)));

        _ = _providerMock
            .Setup(p => p.ImportCsvTagsAsync("adminuser", rootTag.Name, csvContent, true))
            .ReturnsAsync(2);

        var authStateTask = Task.FromResult(BunitTestSetup.CreateAuthState("adminuser", "Admin"));
        IRenderedComponent<ImportTag> component = adminCtx.Render<ImportTag>(p => p.AddCascadingValue(authStateTask));

        IRenderedComponent<MudSwitch<bool>> switchComp =
            component.FindComponent<MudSwitch<bool>>();
        Assert.NotNull(switchComp);

        await component.InvokeAsync(() => switchComp.Instance.ValueChanged.InvokeAsync(true));

        IRenderedComponent<MudAutocomplete<SRNSMudApp.Data.Tag>> autocomplete =
            component.FindComponent<MudAutocomplete<SRNSMudApp.Data.Tag>>();
        await component.InvokeAsync(() => autocomplete.Instance.ValueChanged.InvokeAsync(rootTag));

        IRenderedComponent<MudFileUpload<IBrowserFile>> fileUpload =
            component.FindComponent<MudFileUpload<IBrowserFile>>();
        await component.InvokeAsync(() => fileUpload.Instance.FilesChanged.InvokeAsync(fileMock.Object));

        component.Render();

        IReadOnlyList<IRenderedComponent<MudButton>> buttons = component.FindComponents<MudButton>();
        IRenderedComponent<MudButton>? importButton = buttons.FirstOrDefault(b => b.Instance.Color == Color.Success);
        Assert.NotNull(importButton);
        await component.InvokeAsync(() => importButton.Find("button").Click());

        _providerMock.Verify(p => p.ImportCsvTagsAsync("adminuser", rootTag.Name, csvContent, true), Times.Once);
    }

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }
}