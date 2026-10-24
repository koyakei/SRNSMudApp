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

public class PublicOfferBoardPageTests : IAsyncDisposable
{
    private readonly BunitContext _ctx = new();
    private readonly Mock<IItemListApiClient> _apiClientMock = new();

    public PublicOfferBoardPageTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddMudServices();
        _ctx.Services.AddScoped(_ => _apiClientMock.Object);
    }

    [Fact]
    public void WhenInitialized_LoadsAndRendersOffers()
    {
        // Arrange
        var offers = new List<PublicOfferDto>
        {
            new(
                Id: 1,
                OfferedTagId: 10,
                OfferedTagName: "Rust",
                RequiredAssetAmount: 0,
                OwnerUserId: "user-1",
                OwnerUserName: "alice",
                CreatedDate: DateTimeOffset.UtcNow,
                IsOwner: false
            )
        };

        _apiClientMock
            .Setup(c => c.GetPublicOffersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(offers);

        // Act
        IRenderedComponent<PublicOfferBoardPage> cut = _ctx.Render<PublicOfferBoardPage>();

        // Assert
        Assert.Contains("#Rust", cut.Markup);
        Assert.Contains("alice", cut.Markup);
        Assert.Contains("無償提供", cut.Markup);
        Assert.Contains("オファーを承諾", cut.Markup);
    }

    [Fact]
    public void WhenEmpty_RendersEmptyAlert()
    {
        // Arrange
        _apiClientMock
            .Setup(c => c.GetPublicOffersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        IRenderedComponent<PublicOfferBoardPage> cut = _ctx.Render<PublicOfferBoardPage>();

        // Assert
        Assert.Contains("現在、公開されているオファーはありません。", cut.Markup);
    }

    [Fact]
    public async Task WhenDeactivateClicked_CallsApiClient()
    {
        // Arrange
        var offers = new List<PublicOfferDto>
        {
            new(
                Id: 1,
                OfferedTagId: 10,
                OfferedTagName: "Rust",
                RequiredAssetAmount: 2,
                OwnerUserId: "user-1",
                OwnerUserName: "alice",
                CreatedDate: DateTimeOffset.UtcNow,
                IsOwner: true
            )
        };

        _apiClientMock
            .Setup(c => c.GetPublicOffersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(offers);

        _apiClientMock
            .Setup(c => c.DeactivatePublicOfferAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        IRenderedComponent<PublicOfferBoardPage> cut = _ctx.Render<PublicOfferBoardPage>();

        // Act
        var deactivateBtn = cut.Find("button[data-testid='deactivate-offer-btn-1']");
        await cut.InvokeAsync(() => deactivateBtn.Click());

        // Assert
        _apiClientMock.Verify(c => c.DeactivatePublicOfferAsync(1, It.IsAny<CancellationToken>()), Times.Once);
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}