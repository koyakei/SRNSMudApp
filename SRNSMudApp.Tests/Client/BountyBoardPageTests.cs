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

public class BountyBoardPageTests : IAsyncDisposable
{
    private readonly BunitContext _ctx = new();
    private readonly Mock<IItemListApiClient> _apiClientMock = new();

    public BountyBoardPageTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddMudServices();
        _ctx.Services.AddScoped(_ => _apiClientMock.Object);
    }

    [Fact]
    public void WhenInitialized_LoadsAndRendersBounties()
    {
        // Arrange
        var bounties = new List<BountyDto>
        {
            new(
                Id: 1,
                RequesterUserId: "u1",
                RequesterUserName: "alice",
                RequestedTagId: 10,
                RequestedTagName: "Blazor",
                TargetItemId: 100,
                TargetItemContent: "アイテムの本文",
                RewardTagName: "MudBlazor",
                RewardAssetId: 5,
                CreatedDate: DateTimeOffset.UtcNow,
                IsMyBounty: false,
                CanFulfill: true
            )
        };

        _apiClientMock
            .Setup(c => c.GetBountiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BountyBoardResponse(bounties, 1));

        // Act
        IRenderedComponent<BountyBoardPage> cut = _ctx.Render<BountyBoardPage>();

        // Assert
        Assert.Contains("#Blazor", cut.Markup);
        Assert.Contains("alice", cut.Markup);
        Assert.Contains("アイテムの本文", cut.Markup);
        Assert.Contains("MudBlazor の権利", cut.Markup);
        Assert.Contains("依頼を完了する", cut.Markup);
    }

    [Fact]
    public void WhenEmpty_RendersEmptyAlert()
    {
        // Arrange
        _apiClientMock
            .Setup(c => c.GetBountiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BountyBoardResponse([], 0));

        // Act
        IRenderedComponent<BountyBoardPage> cut = _ctx.Render<BountyBoardPage>();

        // Assert
        Assert.Contains("現在、募集中のバウンティはありません。", cut.Markup);
    }

    [Fact]
    public async Task WhenFulfillClicked_CallsApiClient()
    {
        // Arrange
        var bounties = new List<BountyDto>
        {
            new(
                Id: 1,
                RequesterUserId: "u2",
                RequesterUserName: "bob",
                RequestedTagId: 10,
                RequestedTagName: "Blazor",
                TargetItemId: 100,
                TargetItemContent: "アイテムの本文",
                RewardTagName: null,
                RewardAssetId: null,
                CreatedDate: DateTimeOffset.UtcNow,
                IsMyBounty: false,
                CanFulfill: true
            )
        };

        _apiClientMock
            .Setup(c => c.GetBountiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BountyBoardResponse(bounties, 1));

        _apiClientMock
            .Setup(c => c.FulfillBountyAsync(1, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        IRenderedComponent<BountyBoardPage> cut = _ctx.Render<BountyBoardPage>();

        // Act
        var fulfillBtn = cut.Find("button[data-testid='fulfill-bounty-btn-1']");
        await cut.InvokeAsync(() => fulfillBtn.Click());

        // Assert
        _apiClientMock.Verify(c => c.FulfillBountyAsync(1, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}