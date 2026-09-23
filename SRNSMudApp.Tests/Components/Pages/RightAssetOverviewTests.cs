#region

using AngleSharp.Dom;

using Bunit;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.Pages;
using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Services;

#endregion

namespace SRNSMudApp.Tests.Components.Pages;

public sealed class RightAssetOverviewTests : IAsyncLifetime
{
    private readonly BunitContext _ctx = new();
    private readonly Mock<IRightAssetDataProvider> _dataProviderMock = new();
    private readonly Mock<ITagSearchQueryService> _tagSearchMock = new();

    public RightAssetOverviewTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices().AddMockSrnsServices();
        _ = _ctx.Services.AddScoped(_ => _dataProviderMock.Object);
        _ = _ctx.Services.AddScoped(_ => _tagSearchMock.Object);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    [Fact]
    public void InitialRender_WithoutTagId_ShowsTopTagsAndGuidance()
    {
        // Arrange
        var topTags = new List<TagRightAssetSummary>
        {
            new(1, "React", "React tag", 100, 3),
            new(2, "Blazor", "Blazor tag", 50, 2)
        };
        _dataProviderMock.Setup(p => p.GetTopTagsWithRightAssetsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(topTags);

        // Act
        IRenderedComponent<MudPopoverProvider> _ = _ctx.Render<MudPopoverProvider>();
        IRenderedComponent<RightAssetOverview> cut = _ctx.Render<RightAssetOverview>();

        // Assert
        Assert.Contains("RightAsset 保有状況", cut.Markup);
        Assert.Contains("React", cut.Markup);
        Assert.Contains("Blazor", cut.Markup);
        _dataProviderMock.Verify(p => p.GetTopTagsWithRightAssetsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void WhenTagIdProvided_LoadsAndRendersHoldersAndMetrics()
    {
        // Arrange
        var tag = new SRNSMudApp.Data.Tag
        {
            Id = 42,
            Name = "Solana",
            Content = "High speed blockchain",
            CachedWeight = 10,
            OwnerId = "system"
        };

        var holders = new List<RightAssetHolderSummary>
        {
            new("user-1", "Alice", 150, 3, 20, 1, DateTime.UtcNow),
            new("user-2", "Bob", 50, 1, 0, 0, DateTime.UtcNow)
        };

        var assets = new List<RightAssetDetailDto>
        {
            new(101, "user-1", "Alice", 100, false, "有効", DateTime.UtcNow, DateTime.UtcNow),
            new(102, "user-1", "Alice", 50, false, "有効", DateTime.UtcNow, DateTime.UtcNow),
            new(103, "user-2", "Bob", 50, false, "有効", DateTime.UtcNow, DateTime.UtcNow)
        };

        var overviewData = new RightAssetOverviewData(
            Tag: tag,
            TotalHoldersCount: 2,
            TotalActiveAmount: 200,
            TotalActiveAssetsCount: 3,
            TotalBurnedAmount: 20,
            Holders: holders,
            Assets: assets
        );

        _dataProviderMock.Setup(p => p.GetTopTagsWithRightAssetsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _dataProviderMock.Setup(p => p.GetRightAssetOverviewByTagIdAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(overviewData);

        // Act
        var nav = _ctx.Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("/RightAsset/Overview?tagId=42");

        IRenderedComponent<MudPopoverProvider> _ = _ctx.Render<MudPopoverProvider>();
        IRenderedComponent<RightAssetOverview> cut = _ctx.Render<RightAssetOverview>();

        // Assert
        Assert.Contains("Solana", cut.Markup);
        Assert.Contains("High speed blockchain", cut.Markup);
        Assert.Contains("200", cut.Markup); // TotalActiveAmount
        Assert.Contains("Alice", cut.Markup);
        Assert.Contains("Bob", cut.Markup);
        Assert.Contains("150", cut.Markup); // Alice's amount
        Assert.Contains("50", cut.Markup);  // Bob's amount
        Assert.Contains("75.0%", cut.Markup); // Alice's share (150 / 200 = 75%)
        Assert.Contains("25.0%", cut.Markup); // Bob's share (50 / 200 = 25%)

        _dataProviderMock.Verify(p => p.GetRightAssetOverviewByTagIdAsync(42, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void WhenTagHasNoHolders_ShowsEmptyMessage()
    {
        // Arrange
        var tag = new SRNSMudApp.Data.Tag
        {
            Id = 99,
            Name = "EmptyTag",
            CachedWeight = 0,
            OwnerId = "system"
        };

        var overviewData = new RightAssetOverviewData(
            Tag: tag,
            TotalHoldersCount: 0,
            TotalActiveAmount: 0,
            TotalActiveAssetsCount: 0,
            TotalBurnedAmount: 0,
            Holders: [],
            Assets: []
        );

        _dataProviderMock.Setup(p => p.GetTopTagsWithRightAssetsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _dataProviderMock.Setup(p => p.GetRightAssetOverviewByTagIdAsync(99, It.IsAny<CancellationToken>()))
            .ReturnsAsync(overviewData);

        var nav = _ctx.Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("/RightAsset/Overview?tagId=99");

        IRenderedComponent<MudPopoverProvider> _ = _ctx.Render<MudPopoverProvider>();
        IRenderedComponent<RightAssetOverview> cut = _ctx.Render<RightAssetOverview>();

        // Assert
        Assert.Contains("EmptyTag", cut.Markup);
        Assert.Contains("このタグを保有しているユーザーはいません", cut.Markup);
    }

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }
}