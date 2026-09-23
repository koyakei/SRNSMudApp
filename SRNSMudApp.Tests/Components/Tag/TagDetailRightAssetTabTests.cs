#region

using AngleSharp.Dom;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.Tag;
using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Services;
using SRNSMudApp.Services.Dialogs;
using SRNSMudApp.Tests.TestSupport;

using TagEntity = SRNSMudApp.Data.Tag;

#endregion

namespace SRNSMudApp.Tests.Components.Tag;

/// <summary>
///     <see cref="TagDetail" /> 画面における「操作権限」タブ（RightAsset 保有状況一覧）のコンポーネントテスト。
/// </summary>
public sealed class TagDetailRightAssetTabTests : IAsyncLifetime
{
    private readonly BunitContext _ctx = new();
    private readonly Mock<ITagDetailDataProvider> _tagDetailDataMock = new();
    private readonly Mock<ITagLockService> _tagLockServiceMock = new();
    private readonly Mock<ITagContentProposalService> _contentProposalMock = new();
    private readonly Mock<ITagNameProposalService> _nameProposalMock = new();
    private readonly Mock<ISnackbar> _snackbarMock = new();
    private readonly Mock<IDialogLauncher> _dialogLauncherMock = new();

    public TagDetailRightAssetTabTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices();
        _ = _ctx.Services.AddMockSrnsServices();
        _ = _ctx.Services.AddScoped(_ => _tagDetailDataMock.Object);
        _ = _ctx.Services.AddScoped(_ => _tagLockServiceMock.Object);
        _ = _ctx.Services.AddScoped(_ => _contentProposalMock.Object);
        _ = _ctx.Services.AddScoped(_ => _nameProposalMock.Object);
        _ = _ctx.Services.AddScoped(_ => _snackbarMock.Object);
        _ = _ctx.Services.AddScoped(_ => _dialogLauncherMock.Object);
        _ = _ctx.Services.AddAuthorizationCore();
        _ = _ctx.Services.AddAuth("test-user");
        _ = _ctx.Render<MudPopoverProvider>();

        _ = _contentProposalMock.Setup(s => s.GetPendingProposalsForTagAsync(It.IsAny<int>()))
            .ReturnsAsync([]);
        _ = _nameProposalMock.Setup(s => s.GetPendingProposalsForTagAsync(It.IsAny<int>()))
            .ReturnsAsync([]);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _ctx.DisposeAsync().AsTask();

    [Fact]
    public void WhenTagHasRightAssets_RendersPermissionTab_WithHoldersAndAssets()
    {
        // Arrange
        var tag = new TagEntity
        {
            Id = 10,
            Name = "Solana",
            Content = "Solana blockchain",
            OwnerId = "owner-user"
        };

        var holders = new List<RightAssetHolderSummary>
        {
            new("user-alice", "Alice", 100, 2, 0, 0, DateTime.UtcNow),
            new("user-bob", "Bob", 40, 1, 10, 1, DateTime.UtcNow)
        };

        var assets = new List<RightAssetDetailDto>
        {
            new(1, "user-alice", "Alice", 60, false, "有効", DateTime.UtcNow, DateTime.UtcNow),
            new(2, "user-alice", "Alice", 40, false, "有効", DateTime.UtcNow, DateTime.UtcNow),
            new(3, "user-bob", "Bob", 40, false, "有効", DateTime.UtcNow, DateTime.UtcNow)
        };

        var overview = new RightAssetOverviewData(
            Tag: tag,
            TotalHoldersCount: 2,
            TotalActiveAmount: 140,
            TotalActiveAssetsCount: 3,
            TotalBurnedAmount: 10,
            Holders: holders,
            Assets: assets
        );

        var pageData = new TagDetailPageData(
            Tag: tag,
            IsFollowing: false,
            RelatedItems: [],
            RelatedTags: [],
            WeightLedgers: [],
            PublicOffers: [],
            PendingRequests: [],
            RightAssetOverview: overview
        );

        _ = _tagDetailDataMock.Setup(d => d.GetTagDetailAsync(10, It.IsAny<string?>()))
            .ReturnsAsync(pageData);

        // Act
        IRenderedComponent<TagDetail> cut = _ctx.Render<TagDetail>(parameters => parameters
            .Add(p => p.TagId, 10)
            .AddCascadingValue(Task.FromResult(BunitTestSetup.CreateAuthState("test-user"))));

        // Assert: タブヘッダーが存在することを確認
        Assert.Contains("操作権限", cut.Markup);

        // 「操作権限」タブをクリックしてアクティブ化
        IElement? tabButton = cut.FindAll(".mud-tab")
            .FirstOrDefault(t => t.TextContent.Contains("操作権限"));
        Assert.NotNull(tabButton);
        tabButton.Click();

        // パネル内のコンテンツを検証
        Assert.Contains("RightAsset 保有状況", cut.Markup);
        Assert.Contains("総保有量: 140", cut.Markup);
        Assert.Contains("保有者: 2 人", cut.Markup);
        Assert.Contains("Alice", cut.Markup);
        Assert.Contains("Bob", cut.Markup);
        Assert.Contains("100", cut.Markup); // Alice's amount
        Assert.Contains("40", cut.Markup);  // Bob's amount

        // 保有者テーブルにアクション列と「リクエスト」ボタンが存在することを検証
        Assert.Contains("アクション", cut.Markup);
        IElement? aliceRequestBtn = cut.FindAll("[data-testid='request-permission-holder-user-alice']").FirstOrDefault();
        IElement? bobRequestBtn = cut.FindAll("[data-testid='request-permission-holder-user-bob']").FirstOrDefault();
        Assert.NotNull(aliceRequestBtn);
        Assert.NotNull(bobRequestBtn);
    }

    [Fact]
    public void WhenTagHasNoRightAssets_RendersEmptyMessageInPermissionTab()
    {
        // Arrange
        var tag = new TagEntity
        {
            Id = 20,
            Name = "EmptyAssetTag",
            OwnerId = "owner-user"
        };

        var overview = new RightAssetOverviewData(
            Tag: tag,
            TotalHoldersCount: 0,
            TotalActiveAmount: 0,
            TotalActiveAssetsCount: 0,
            TotalBurnedAmount: 0,
            Holders: [],
            Assets: []
        );

        var pageData = new TagDetailPageData(
            Tag: tag,
            IsFollowing: false,
            RelatedItems: [],
            RelatedTags: [],
            WeightLedgers: [],
            PublicOffers: [],
            PendingRequests: [],
            RightAssetOverview: overview
        );

        _ = _tagDetailDataMock.Setup(d => d.GetTagDetailAsync(20, It.IsAny<string?>()))
            .ReturnsAsync(pageData);

        // Act
        IRenderedComponent<TagDetail> cut = _ctx.Render<TagDetail>(parameters => parameters
            .Add(p => p.TagId, 20)
            .AddCascadingValue(Task.FromResult(BunitTestSetup.CreateAuthState("test-user"))));

        // Assert: タブヘッダーが存在することを確認
        Assert.Contains("操作権限", cut.Markup);

        // 「操作権限」タブをクリックしてアクティブ化
        IElement? tabButton = cut.FindAll(".mud-tab")
            .FirstOrDefault(t => t.TextContent.Contains("操作権限"));
        Assert.NotNull(tabButton);
        tabButton.Click();

        // パネル内の空メッセージを検証
        Assert.Contains("このタグに対する有効な RightAsset はまだ発行されていません", cut.Markup);
    }
}