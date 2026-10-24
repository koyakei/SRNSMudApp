namespace SRNSMudApp.Tests.Client;

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Client.Models.Api;
using SRNSMudApp.Client.Pages;
using SRNSMudApp.Client.Services;

using Xunit;

public class RequireConfirmedAccountPageTests : IAsyncDisposable
{
    private readonly BunitContext _ctx = new();
    private readonly Mock<IItemListApiClient> _apiClientMock = new();

    public RequireConfirmedAccountPageTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddMudServices();
        _ctx.Services.AddScoped(_ => _apiClientMock.Object);
        _ctx.Services.AddCascadingAuthenticationState();
        _ctx.Services.AddAuthorizationCore();
        _ctx.Render<MudPopoverProvider>();
    }

    [Fact]
    public void WhenInitialized_LoadsAndRendersUsers()
    {
        // Arrange
        var users = new List<UserConfirmationDto>
        {
            new("u1", "alice", "alice@example.com", true),
            new("u2", "bob", "bob@example.com", false)
        };

        _apiClientMock
            .Setup(c => c.GetConfirmedAccountsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(users);

        // Act
        var cut = _ctx.Render<RequireConfirmedAccountPage>();

        // Assert
        Assert.Contains("ユーザー確認設定 (WASM)", cut.Markup);
        Assert.Contains("alice@example.com", cut.Markup);
        Assert.Contains("bob@example.com", cut.Markup);
        Assert.Contains("確認済み (Confirmed)", cut.Markup);
        Assert.Contains("未確認 (Unconfirmed)", cut.Markup);
    }

    [Fact]
    public async Task WhenToggleClicked_CallsApiAndUpdatesStatus()
    {
        // Arrange
        var users = new List<UserConfirmationDto>
        {
            new("u1", "alice", "alice@example.com", false)
        };

        _apiClientMock
            .Setup(c => c.GetConfirmedAccountsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(users);

        _apiClientMock
            .Setup(c => c.ToggleUserConfirmationAsync("u1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var cut = _ctx.Render<RequireConfirmedAccountPage>();

        // Act
        var toggleBtn = cut.Find("[data-testid='toggle-btn-u1']");
        await cut.InvokeAsync(() => toggleBtn.Click());

        // Assert
        _apiClientMock.Verify(c => c.ToggleUserConfirmationAsync("u1", It.IsAny<CancellationToken>()), Times.Once);
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("確認済み (Confirmed)", cut.Markup);
        });
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}