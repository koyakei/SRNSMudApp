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

public class RightAssetOverviewPageTests : IAsyncDisposable
{
    private readonly BunitContext _ctx = new();
    private readonly Mock<IItemListApiClient> _apiClientMock = new();

    public RightAssetOverviewPageTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddMudServices();
        _ctx.Services.AddScoped(_ => _apiClientMock.Object);
        _ctx.Render<MudPopoverProvider>();
    }

    [Fact]
    public void WhenInitialized_WithoutTagId_LoadsAndRendersTopTags()
    {
        // Arrange
        var topTags = new List<TagRightAssetSummaryDto>
        {
            new(1, "TagAlpha", "Alpha Desc", 100, 3),
            new(2, "TagBeta", null, 50, 1)
        };

        _apiClientMock
            .Setup(c => c.GetTopTagsWithRightAssetsAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(topTags);

        // Act
        var cut = _ctx.Render<RightAssetOverviewPage>();

        // Assert
        Assert.Contains("RightAsset 保有状況", cut.Markup);
        Assert.Contains("TagAlpha (100 持分 / 3 人)", cut.Markup);
        Assert.Contains("TagBeta (50 持分 / 1 人)", cut.Markup);
    }

    [Fact]
    public void WhenInitialized_WithTagId_LoadsAndRendersOverview()
    {
        // Arrange
        var overview = new RightAssetOverviewResponse(
            TagId: 42,
            TagName: "QuantumTag",
            TagContent: "Tag explanation",
            TagWeight: 7,
            IsSystem: false,
            TotalHoldersCount: 2,
            TotalActiveAmount: 120,
            TotalActiveAssetsCount: 2,
            TotalBurnedAmount: 5,
            Holders:
            [
                new RightAssetHolderDto("user-1", "Alice", 100, 1, 0, 0, DateTime.UtcNow),
                new RightAssetHolderDto("user-2", "Bob", 20, 1, 5, 1, DateTime.UtcNow)
            ],
            Assets:
            [
                new RightAssetItemDto(1, "user-1", "Alice", 100, false, "有効", DateTime.UtcNow, DateTime.UtcNow),
                new RightAssetItemDto(2, "user-2", "Bob", 20, false, "有効", DateTime.UtcNow, DateTime.UtcNow)
            ]
        );

        _apiClientMock
            .Setup(c => c.GetRightAssetOverviewAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(overview);

        // Act
        var cut = _ctx.Render<RightAssetOverviewPage>(parameters => parameters.Add(p => p.TagId, 42));

        // Assert
        Assert.Contains("QuantumTag", cut.Markup);
        Assert.Contains("Tag explanation", cut.Markup);
        Assert.Contains("Weight: 7", cut.Markup);
        Assert.Contains("120", cut.Markup); // 総有効保有量
        Assert.Contains("Alice", cut.Markup);
        Assert.Contains("Bob", cut.Markup);
    }

    [Fact]
    public void WhenTagSelected_RendersOverviewData()
    {
        // Arrange
        var topTags = new List<TagRightAssetSummaryDto>
        {
            new(5, "InitialTag", null, 30, 1)
        };
        _apiClientMock
            .Setup(c => c.GetTopTagsWithRightAssetsAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(topTags);

        var overview = new RightAssetOverviewResponse(
            TagId: 5,
            TagName: "InitialTag",
            TagContent: null,
            TagWeight: 1,
            IsSystem: true,
            TotalHoldersCount: 1,
            TotalActiveAmount: 30,
            TotalActiveAssetsCount: 1,
            TotalBurnedAmount: 0,
            Holders: [new RightAssetHolderDto("u1", "UserOne", 30, 1, 0, 0, null)],
            Assets: [new RightAssetItemDto(10, "u1", "UserOne", 30, false, "有効", DateTime.UtcNow, DateTime.UtcNow)]
        );
        _apiClientMock
            .Setup(c => c.GetRightAssetOverviewAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(overview);

        // Act
        var cut = _ctx.Render<RightAssetOverviewPage>();

        // クリックで代表タグを選択
        var chip = cut.Find(".mud-chip");
        chip.Click();

        // Assert
        Assert.Contains("InitialTag", cut.Markup);
        Assert.Contains("システムタグ", cut.Markup);
        Assert.Contains("UserOne", cut.Markup);
    }

    [Fact]
    public async Task WhenPurchaseButtonClicked_TogglesPurchasePanel_AndLoadsNetworksAndWallet()
    {
        // Arrange
        var overview = new RightAssetOverviewResponse(
            TagId: 10,
            TagName: "TestTag",
            TagContent: "Desc",
            TagWeight: 5,
            IsSystem: false,
            TotalHoldersCount: 1,
            TotalActiveAmount: 50,
            TotalActiveAssetsCount: 1,
            TotalBurnedAmount: 0,
            Holders: [],
            Assets: []
        );
        _apiClientMock
            .Setup(c => c.GetRightAssetOverviewAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(overview);

        var networks = new List<JpycNetworkDto>
        {
            new("polygon-amoy", "Polygon Amoy", 80002, "0xContractAddress", "https://faucet.com", "https://explorer.com", true, true)
        };
        _apiClientMock
            .Setup(c => c.GetJpycNetworksAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(networks);

        var wallet = new DepositWalletDto("u1", "polygon-amoy", "0xUserDepositAddress", DateTime.UtcNow);
        _apiClientMock
            .Setup(c => c.GetUserDepositWalletAsync("polygon-amoy", It.IsAny<CancellationToken>()))
            .ReturnsAsync(wallet);

        var cut = _ctx.Render<RightAssetOverviewPage>(parameters => parameters.Add(p => p.TagId, 10));

        // Act - JPYCで購入ボタンをクリック
        var purchaseBtn = cut.Find("[data-testid='overview-purchase-jpyc-button']");
        await cut.InvokeAsync(() => purchaseBtn.Click());

        // Assert - パネルが表示され、ウォレットアドレスとコントラクトアドレスが描画されていること
        Assert.Contains("data-testid=\"purchase-jpyc-panel\"", cut.Markup);
        Assert.Contains("0xUserDepositAddress", cut.Markup);
        Assert.Contains("0xContractAddress", cut.Markup);
        Assert.Contains("Polygon Amoy", cut.Markup);
    }

    [Fact]
    public async Task WhenSimulatePaymentClicked_SetsTxHash()
    {
        // Arrange
        var overview = new RightAssetOverviewResponse(
            TagId: 10,
            TagName: "TestTag",
            TagContent: "Desc",
            TagWeight: 5,
            IsSystem: false,
            TotalHoldersCount: 1,
            TotalActiveAmount: 50,
            TotalActiveAssetsCount: 1,
            TotalBurnedAmount: 0,
            Holders: [],
            Assets: []
        );
        _apiClientMock
            .Setup(c => c.GetRightAssetOverviewAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(overview);

        var networks = new List<JpycNetworkDto>
        {
            new("polygon-amoy", "Polygon Amoy", 80002, "0xContractAddress", "https://faucet.com", "https://explorer.com", true, true)
        };
        _apiClientMock
            .Setup(c => c.GetJpycNetworksAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(networks);

        _apiClientMock
            .Setup(c => c.GetUserDepositWalletAsync("polygon-amoy", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DepositWalletDto("u1", "polygon-amoy", "0xUserDepositAddress", DateTime.UtcNow));

        _apiClientMock
            .Setup(c => c.SimulateDepositAsync(It.IsAny<SimulateDepositRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("0xSimulatedHash123");

        var cut = _ctx.Render<RightAssetOverviewPage>(parameters => parameters.Add(p => p.TagId, 10));

        // JPYCで購入ボタンをクリック
        var purchaseBtn = cut.Find("[data-testid='overview-purchase-jpyc-button']");
        await cut.InvokeAsync(() => purchaseBtn.Click());

        // Act - シミュレートリンクをクリック
        var simulateLink = cut.Find("[data-testid='simulate-payment-link']");
        await cut.InvokeAsync(() => simulateLink.Click());

        // Assert - TxHash が反映される
        Assert.Contains("0xSimulatedHash123", cut.Markup);
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}