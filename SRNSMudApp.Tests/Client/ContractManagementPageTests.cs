namespace SRNSMudApp.Tests.Client;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor.Services;

using SRNSMudApp.Client.Models.Api;
using SRNSMudApp.Client.Pages;
using SRNSMudApp.Client.Services;

using Xunit;

public class ContractManagementPageTests : IAsyncDisposable
{
    private readonly BunitContext _ctx = new();
    private readonly Mock<IItemListApiClient> _apiClientMock = new();

    public ContractManagementPageTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddMudServices();
        _ctx.Services.AddScoped(_ => _apiClientMock.Object);
    }

    [Fact]
    public void WhenInitialized_LoadsAndRendersInboxAndOutbox()
    {
        // Arrange
        var incoming = new List<ContractDto>
        {
            new(
                Id: 1,
                ContractType: "Gratis",
                Status: "Proposed",
                RequestedTagId: 10,
                RequestedTagName: "GraphQL",
                TargetItemId: 100,
                TargetItemContent: "アイテム本文",
                RequesterUserId: "u2",
                RequesterUserName: "bob",
                TagOwnerUserId: "u1",
                TagOwnerUserName: "alice",
                CreatedDate: DateTimeOffset.UtcNow,
                CanCancel: false
            )
        };

        var response = new ContractPageResponse(incoming, []);

        _apiClientMock
            .Setup(c => c.GetContractsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        IRenderedComponent<ContractManagementPage> cut = _ctx.Render<ContractManagementPage>();

        // Assert
        Assert.Contains("#GraphQL", cut.Markup);
        Assert.Contains("bob", cut.Markup);
        Assert.Contains("アイテム本文", cut.Markup);
        Assert.Contains("提案中", cut.Markup);
        Assert.Contains("承認する", cut.Markup);
        Assert.Contains("拒否する", cut.Markup);
    }

    [Fact]
    public void WhenEmpty_RendersEmptyAlert()
    {
        // Arrange
        var response = new ContractPageResponse([], []);

        _apiClientMock
            .Setup(c => c.GetContractsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        IRenderedComponent<ContractManagementPage> cut = _ctx.Render<ContractManagementPage>();

        // Assert
        Assert.Contains("現在、受信したコントラクトの提案はありません。", cut.Markup);
    }

    [Fact]
    public async Task WhenAcceptClicked_CallsApiClient()
    {
        // Arrange
        var incoming = new List<ContractDto>
        {
            new(
                Id: 1,
                ContractType: "Gratis",
                Status: "Proposed",
                RequestedTagId: 10,
                RequestedTagName: "GraphQL",
                TargetItemId: 100,
                TargetItemContent: "アイテム本文",
                RequesterUserId: "u2",
                RequesterUserName: "bob",
                TagOwnerUserId: "u1",
                TagOwnerUserName: "alice",
                CreatedDate: DateTimeOffset.UtcNow,
                CanCancel: false
            )
        };

        var response = new ContractPageResponse(incoming, []);

        _apiClientMock
            .Setup(c => c.GetContractsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        _apiClientMock
            .Setup(c => c.AcceptContractAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        IRenderedComponent<ContractManagementPage> cut = _ctx.Render<ContractManagementPage>();

        // Act
        var acceptBtn = cut.Find("button[data-testid='accept-contract-btn-1']");
        await cut.InvokeAsync(() => acceptBtn.Click());

        // Assert
        _apiClientMock.Verify(c => c.AcceptContractAsync(1, It.IsAny<CancellationToken>()), Times.Once);
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}