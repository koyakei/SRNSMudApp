#region

using AngleSharp.Dom;

using Bunit;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.Tag;
using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;

using TagEntity = SRNSMudApp.Data.Tag;

#endregion

namespace SRNSMudApp.Tests.Components.Tag;

public sealed class PurchaseRightAssetDialogTests : IAsyncDisposable
{
    private const string CurrentUserId = "buyer-user";
    private const string UserDepositAddress = "0x9876543210abcdef9876543210abcdef98765432";
    private readonly BunitContext _ctx = new();
    private readonly Mock<IRightAssetPurchaseService> _purchaseServiceMock = new();
    private readonly Mock<ISnackbar> _snackbarMock = new();

    private readonly List<JpycNetworkInfo> _mockNetworks =
    [
        new(
            Name: "polygon-amoy",
            DisplayName: "Polygon Amoy (テストネット)",
            ChainId: 80002,
            ContractAddress: "0xE7C3D8C9a439feDe00D2600032D5dB0Be71C3c29",
            FaucetUrl: "https://faucet.jpyc.co.jp",
            ExplorerUrl: "https://amoy.polygonscan.com/",
            IsTestnet: true,
            IsRecommended: true),
        new(
            Name: "ethereum-sepolia",
            DisplayName: "Ethereum Sepolia (テストネット)",
            ChainId: 11155111,
            ContractAddress: "0xE7C3D8C9a439feDe00D2600032D5dB0Be71C3c29",
            FaucetUrl: "https://faucet.jpyc.co.jp",
            ExplorerUrl: "https://sepolia.etherscan.io/",
            IsTestnet: true)
    ];

    public PurchaseRightAssetDialogTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices().AddMockSrnsServices();
        _ = _ctx.Services.AddAuthorizationCore();
        _ = _ctx.Services.AddAuth(CurrentUserId);
        _ = _ctx.Services.AddScoped(_ => _purchaseServiceMock.Object);
        _ = _ctx.Services.AddScoped(_ => _snackbarMock.Object);
        _ = _ctx.Render<MudPopoverProvider>();

        _ = _purchaseServiceMock.Setup(p => p.GetSupportedNetworks()).Returns(_mockNetworks);
        _ = _purchaseServiceMock.Setup(p => p.GetOrCreateUserDepositWalletAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserDepositWalletDto(CurrentUserId, "polygon-amoy", UserDepositAddress, DateTime.UtcNow));
        _ = _purchaseServiceMock.Setup(p => p.SimulateDepositAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("0xabcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890");
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }

    [Fact]
    public async Task PurchaseRightAssetDialog_RendersTagAndNetworkInfo_WithContractAndDepositAddress()
    {
        // Arrange
        IRenderedComponent<DialogHost> host = _ctx.Render<DialogHost>();
        IDialogService dialogService = _ctx.Services.GetRequiredService<IDialogService>();

        var tag = new TagEntity
        {
            Id = 15,
            Name = "Solana",
            OwnerId = "tag-owner"
        };

        var parameters = new DialogParameters<PurchaseRightAssetDialog>
        {
            { x => x.RequestedTag, tag },
            { x => x.DefaultAmount, 2 },
            { x => x.DefaultUnitPriceJpyc, 120 }
        };

        // Act
        _ = await dialogService.ShowAsync<PurchaseRightAssetDialog>("操作権限の購入", parameters);

        // Assert
        host.WaitForAssertion(() =>
        {
            Assert.Contains("「Solana」の操作権限を JPYC で購入", host.Markup);
            Assert.Contains("0xE7C3D8C9a439feDe00D2600032D5dB0Be71C3c29", host.Markup);
            // ユーザー専用の受取ウォレットアドレスが表示されていること
            Assert.Contains(UserDepositAddress, host.Markup);
            Assert.Contains("https://faucet.jpyc.co.jp", host.Markup);
            Assert.Contains("https://faq.jpyc.co.jp/s/article/developer-documentation", host.Markup);
            // 2 x 120 = 240 JPYC
            Assert.Contains("240 JPYC", host.Markup);
        });
    }

    [Fact]
    public async Task PurchaseRightAssetDialog_WhenSimulatePaymentClicked_GeneratesTxHash()
    {
        // Arrange
        IRenderedComponent<DialogHost> host = _ctx.Render<DialogHost>();
        IDialogService dialogService = _ctx.Services.GetRequiredService<IDialogService>();

        var tag = new TagEntity { Id = 15, Name = "Solana", OwnerId = "owner" };
        var parameters = new DialogParameters<PurchaseRightAssetDialog>
        {
            { x => x.RequestedTag, tag }
        };

        // Act
        _ = await dialogService.ShowAsync<PurchaseRightAssetDialog>("操作権限の購入", parameters);

        host.WaitForAssertion(() =>
        {
            IElement? simLink = host.FindAll("[data-testid='simulate-payment-link']").FirstOrDefault();
            Assert.NotNull(simLink);
        });

        IElement simulateLink = host.Find("[data-testid='simulate-payment-link']");
        await host.InvokeAsync(() => simulateLink.Click());

        // Assert: TxHash フィールドにシミュレーションTxHashが入っていること
        host.WaitForAssertion(() =>
        {
            Assert.Contains("0xabcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890", host.Markup);
        });
    }

    [Fact]
    public async Task PurchaseRightAssetDialog_WhenSubmitted_CallsPurchaseService_AndCloses()
    {
        // Arrange
        IRenderedComponent<DialogHost> host = _ctx.Render<DialogHost>();
        IDialogService dialogService = _ctx.Services.GetRequiredService<IDialogService>();

        var tag = new TagEntity { Id = 15, Name = "Solana", OwnerId = "owner" };
        var createdAsset = new RightAsset { Id = 77, TargetTagId = 15, OwnerId = CurrentUserId, Amount = 3 };

        _ = _purchaseServiceMock.Setup(p => p.PurchaseRightAssetWithJpycAsync(
                CurrentUserId,
                It.IsAny<JpycPurchaseRequestDto>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Success<RightAsset>(createdAsset));

        var parameters = new DialogParameters<PurchaseRightAssetDialog>
        {
            { x => x.RequestedTag, tag },
            { x => x.DefaultAmount, 3 },
            { x => x.DefaultUnitPriceJpyc, 200 } // カスタム単価
        };

        // Act
        IDialogReference dialogRef = await dialogService.ShowAsync<PurchaseRightAssetDialog>("操作権限の購入", parameters);

        // まずシミュレーションリンクをクリックして TxHash をセット
        host.WaitForAssertion(() =>
        {
            IElement? simLink = host.FindAll("[data-testid='simulate-payment-link']").FirstOrDefault();
            Assert.NotNull(simLink);
        });
        IElement simulateLink = host.Find("[data-testid='simulate-payment-link']");
        await host.InvokeAsync(() => simulateLink.Click());

        // TxHash が反映されるのを待機
        host.WaitForAssertion(() =>
        {
            Assert.Contains("0xabcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890", host.Markup);
        });

        // 確認ボタンをクリック
        host.WaitForAssertion(() =>
        {
            IElement? confirmBtn = host.FindAll("[data-testid='confirm-purchase-button']").FirstOrDefault();
            Assert.NotNull(confirmBtn);
        });

        IElement confirmButton = host.Find("[data-testid='confirm-purchase-button']");
        await host.InvokeAsync(() => confirmButton.Click());

        // Assert: サービスが正しい引数で呼ばれたこと (3 * 200 = 600 JPYC)
        _purchaseServiceMock.Verify(p => p.PurchaseRightAssetWithJpycAsync(
            CurrentUserId,
            It.Is<JpycPurchaseRequestDto>(r => r.RequestedTagId == 15 && r.Amount == 3 && r.UnitPriceJpyc == 200 && r.TotalJpycAmount == 600 && !string.IsNullOrEmpty(r.TransactionHash)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private sealed class DialogHost : ComponentBase
    {
        [Parameter] public RenderFragment ChildContent { get; set; } = _ => { };

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<CascadingAuthenticationState>(0);
            builder.AddAttribute(1, nameof(CascadingAuthenticationState.ChildContent), (RenderFragment)(b =>
            {
                b.OpenComponent<MudDialogProvider>(0);
                b.CloseComponent();
                b.AddContent(1, ChildContent);
            }));
            builder.CloseComponent();
        }
    }
}