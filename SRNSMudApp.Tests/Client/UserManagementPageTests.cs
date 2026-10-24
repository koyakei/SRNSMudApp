namespace SRNSMudApp.Tests.Client;

using System;
using System.Collections.Generic;
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

public class UserManagementPageTests : IAsyncDisposable
{
    private readonly BunitContext _ctx = new();
    private readonly Mock<IItemListApiClient> _apiClientMock = new();

    public UserManagementPageTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddMudServices();
        _ctx.Services.AddScoped(_ => _apiClientMock.Object);
        _ctx.Render<MudPopoverProvider>();
    }

    [Fact]
    public void WhenInitialized_LoadsUsersAndRendersTable()
    {
        // Arrange
        var users = new List<ManagedUserDto>
        {
            new("admin-id", "adminUser", true, false, true),
            new("user-id", "regularUser", false, true, false)
        };

        _apiClientMock
            .Setup(c => c.GetManagementUsersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(users);

        // Act
        var cut = _ctx.Render<UserManagementPage>();

        // Assert
        Assert.Contains("ユーザー管理 (WASM)", cut.Markup);
        Assert.Contains("adminUser", cut.Markup);
        Assert.Contains("保護アカウント", cut.Markup);
        Assert.Contains("regularUser", cut.Markup);
        Assert.Contains("利用停止中", cut.Markup);
    }

    [Fact]
    public void WhenAdminToggled_CallsSetUserAdminAsync()
    {
        // Arrange
        var users = new List<ManagedUserDto>
        {
            new("user-id", "regularUser", false, false, false)
        };

        _apiClientMock
            .Setup(c => c.GetManagementUsersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(users);

        _apiClientMock
            .Setup(c => c.SetUserAdminAsync("user-id", It.IsAny<SetUserAdminRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var cut = _ctx.Render<UserManagementPage>();

        // Act - スイッチをクリック
        var checkboxes = cut.FindAll("input[type='checkbox']");
        Assert.NotEmpty(checkboxes);
        checkboxes[0].Change(true);

        // Assert
        _apiClientMock.Verify(c => c.SetUserAdminAsync("user-id", It.Is<SetUserAdminRequest>(r => r.IsAdmin), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void WhenBanToggled_CallsSetUserBanAsync()
    {
        // Arrange
        var users = new List<ManagedUserDto>
        {
            new("user-id", "regularUser", false, false, false)
        };

        _apiClientMock
            .Setup(c => c.GetManagementUsersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(users);

        _apiClientMock
            .Setup(c => c.SetUserBanAsync("user-id", It.IsAny<SetUserBanRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var cut = _ctx.Render<UserManagementPage>();

        // Act - BANスイッチをクリック
        var checkboxes = cut.FindAll("input[type='checkbox']");
        Assert.True(checkboxes.Count >= 2);
        checkboxes[1].Change(true);

        // Assert
        _apiClientMock.Verify(c => c.SetUserBanAsync("user-id", It.Is<SetUserBanRequest>(r => r.IsBanned), It.IsAny<CancellationToken>()), Times.Once);
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}