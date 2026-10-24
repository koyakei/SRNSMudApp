namespace SRNSMudApp.Tests.Client;

using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Bunit;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Client.Models.Api;
using SRNSMudApp.Client.Pages;
using SRNSMudApp.Client.Services;

using Xunit;

public class AddTagPageTests : IAsyncDisposable
{
    private readonly BunitContext _ctx = new();
    private readonly Mock<IItemListApiClient> _apiClientMock = new();

    public AddTagPageTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddMudServices();
        _ctx.Services.AddScoped(_ => _apiClientMock.Object);
        _ctx.Services.AddCascadingAuthenticationState();
        _ctx.Services.AddAuthorizationCore();
        _ctx.Render<MudPopoverProvider>();
    }

    [Fact]
    public void WhenInitialized_RendersInputFields()
    {
        // Act
        var cut = _ctx.Render<AddTagPage>();

        // Assert
        Assert.Contains("新規タグ作成 (WASM)", cut.Markup);
        var nameInput = cut.Find("[data-testid='tag-name-input']");
        var contentInput = cut.Find("[data-testid='tag-content-input']");
        var submitBtn = cut.Find("[data-testid='create-tag-submit-btn']");

        Assert.NotNull(nameInput);
        Assert.NotNull(contentInput);
        Assert.True(submitBtn.HasAttribute("disabled"));
    }

    [Fact]
    public async Task WhenValidInputSubmitted_CreatesTagAndNavigates()
    {
        // Arrange
        var navMan = _ctx.Services.GetRequiredService<NavigationManager>();
        var createdTag = new TagDto(
            Id: 101,
            Name: "テストタグ",
            Content: "説明文",
            OwnerUserId: "admin-1",
            OwnerUserName: "admin",
            Score: 1,
            IsPrivate: false,
            TargetUserGroupName: null
        );

        _apiClientMock
            .Setup(c => c.CreateTagAsync(It.Is<CreateTagRequest>(r => r.Name == "テストタグ" && r.Content == "説明文"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdTag);

        var cut = _ctx.Render<AddTagPage>();

        // Act
        var nameField = cut.FindComponent<MudTextField<string>>();
        await cut.InvokeAsync(async () =>
        {
            await nameField.Instance.ValueChanged.InvokeAsync("テストタグ");
        });

        var contentInputs = cut.FindComponents<MudTextField<string>>();
        if (contentInputs.Count > 1)
        {
            await cut.InvokeAsync(async () =>
            {
                await contentInputs[1].Instance.ValueChanged.InvokeAsync("説明文");
            });
        }

        var submitBtn = cut.Find("[data-testid='create-tag-submit-btn']");
        Assert.False(submitBtn.HasAttribute("disabled"));

        await cut.InvokeAsync(() => submitBtn.Click());

        // Assert
        _apiClientMock.Verify(c => c.CreateTagAsync(It.Is<CreateTagRequest>(r => r.Name == "テストタグ"), It.IsAny<CancellationToken>()), Times.Once);
        Assert.EndsWith("/Tag/WasmDetail/101", navMan.Uri);
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}