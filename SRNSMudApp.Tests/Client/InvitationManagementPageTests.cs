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

public class InvitationManagementPageTests : IAsyncDisposable
{
    private readonly BunitContext _ctx = new();
    private readonly Mock<IItemListApiClient> _apiClientMock = new();

    public InvitationManagementPageTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddMudServices();
        _ctx.Services.AddScoped(_ => _apiClientMock.Object);
        _ctx.Render<MudPopoverProvider>();
    }

    [Fact]
    public void WhenInitialized_LoadsInvitationsAndRendersTable()
    {
        // Arrange
        var invitations = new List<InvitationDto>
        {
            new(10, "user@example.com", "INVITE123", DateTime.UtcNow.AddDays(3), false, "Pending", "admin1", DateTime.UtcNow)
        };

        _apiClientMock
            .Setup(c => c.GetInvitationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(invitations);

        // Act
        var cut = _ctx.Render<InvitationManagementPage>();

        // Assert
        Assert.Contains("招待コード管理 (WASM)", cut.Markup);
        Assert.Contains("user@example.com", cut.Markup);
        Assert.Contains("INVITE123", cut.Markup);
        Assert.Contains("有効 (保留中)", cut.Markup);
    }

    [Fact]
    public void WhenCreateClicked_CallsCreateInvitationAsyncAndReloads()
    {
        // Arrange
        _apiClientMock
            .Setup(c => c.GetInvitationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var created = new InvitationDto(1, "newuser@example.com", "NEWCODE", DateTime.UtcNow.AddDays(7), false, "Pending", "admin1", DateTime.UtcNow);
        _apiClientMock
            .Setup(c => c.CreateInvitationAsync(It.IsAny<CreateInvitationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(created);

        var cut = _ctx.Render<InvitationManagementPage>();

        // メールアドレスを入力
        var emailInput = cut.Find("input");
        emailInput.Input("newuser@example.com");

        // Act - 発行ボタンをクリック
        var createBtn = cut.Find("[data-testid='create-invite-btn']");
        createBtn.Click();

        // Assert
        _apiClientMock.Verify(c => c.CreateInvitationAsync(It.Is<CreateInvitationRequest>(r => r.Email == "newuser@example.com"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void WhenDeleteClicked_CallsDeleteInvitationAsync()
    {
        // Arrange
        var invitations = new List<InvitationDto>
        {
            new(42, "del@example.com", "DELCODE", DateTime.UtcNow.AddDays(1), false, "Pending", "admin1", DateTime.UtcNow)
        };

        _apiClientMock
            .Setup(c => c.GetInvitationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(invitations);

        _apiClientMock
            .Setup(c => c.DeleteInvitationAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var cut = _ctx.Render<InvitationManagementPage>();

        // Act - 削除ボタンをクリック
        var deleteBtn = cut.Find("[data-testid='delete-invite-42']");
        deleteBtn.Click();

        // Assert
        _apiClientMock.Verify(c => c.DeleteInvitationAsync(42, It.IsAny<CancellationToken>()), Times.Once);
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}