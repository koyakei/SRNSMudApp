namespace SRNSMudApp.Tests.Controllers;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;

using Moq;

using SRNSMudApp.Client.Models.Api;
using SRNSMudApp.Controllers;
using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Services;

using Xunit;

public class RightAssetControllerTests
{
    private readonly Mock<IRightAssetDataProvider> _rightAssetDataProviderMock = new();

    private RightAssetController CreateController()
    {
        return new RightAssetController(_rightAssetDataProviderMock.Object);
    }

    [Fact]
    public async Task GetTopTags_ReturnsMappedTopTags()
    {
        // Arrange
        var summaries = new List<TagRightAssetSummary>
        {
            new(1, "TagAlpha", "Content Alpha", 100, 3),
            new(2, "TagBeta", null, 50, 1)
        };
        _rightAssetDataProviderMock
            .Setup(p => p.GetTopTagsWithRightAssetsAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(summaries);

        var controller = CreateController();

        // Act
        ActionResult<IReadOnlyList<TagRightAssetSummaryDto>> result =
            await controller.GetTopTags(10, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dtos = Assert.IsAssignableFrom<IReadOnlyList<TagRightAssetSummaryDto>>(okResult.Value);
        Assert.Equal(2, dtos.Count);
        Assert.Equal("TagAlpha", dtos[0].TagName);
        Assert.Equal(100, dtos[0].TotalAmount);
        Assert.Equal(3, dtos[0].HolderCount);
        Assert.Equal("TagBeta", dtos[1].TagName);
    }

    [Fact]
    public async Task GetOverview_WhenTagNotFound_ReturnsNotFound()
    {
        // Arrange
        _rightAssetDataProviderMock
            .Setup(p => p.GetRightAssetOverviewByTagIdAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RightAssetOverviewData?)null);

        var controller = CreateController();

        // Act
        ActionResult<RightAssetOverviewResponse> result =
            await controller.GetOverview(999, CancellationToken.None);

        // Assert
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetOverview_WhenTagExists_ReturnsMappedOverview()
    {
        // Arrange
        var tag = new Tag
        {
            Id = 42,
            Name = "CryptoTag",
            Content = "Test tag content",
            CachedWeight = 10,
            IsSystem = false,
            OwnerId = "user-1"
        };

        var holders = new List<RightAssetHolderSummary>
        {
            new("user-1", "Alice", 80, 2, 0, 0, DateTime.UtcNow),
            new("user-2", "Bob", 20, 1, 10, 1, DateTime.UtcNow)
        };

        var assets = new List<RightAssetDetailDto>
        {
            new(101, "user-1", "Alice", 50, false, "有効", DateTime.UtcNow.AddDays(-2), DateTime.UtcNow),
            new(102, "user-1", "Alice", 30, false, "有効", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow),
            new(103, "user-2", "Bob", 20, false, "有効", DateTime.UtcNow, DateTime.UtcNow)
        };

        var overviewData = new RightAssetOverviewData(
            Tag: tag,
            TotalHoldersCount: 2,
            TotalActiveAmount: 100,
            TotalActiveAssetsCount: 3,
            TotalBurnedAmount: 10,
            Holders: holders,
            Assets: assets
        );

        _rightAssetDataProviderMock
            .Setup(p => p.GetRightAssetOverviewByTagIdAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(overviewData);

        var controller = CreateController();

        // Act
        ActionResult<RightAssetOverviewResponse> result =
            await controller.GetOverview(42, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<RightAssetOverviewResponse>(okResult.Value);
        Assert.Equal(42, response.TagId);
        Assert.Equal("CryptoTag", response.TagName);
        Assert.Equal("Test tag content", response.TagContent);
        Assert.Equal(10, response.TagWeight);
        Assert.False(response.IsSystem);
        Assert.Equal(2, response.TotalHoldersCount);
        Assert.Equal(100, response.TotalActiveAmount);
        Assert.Equal(3, response.TotalActiveAssetsCount);
        Assert.Equal(10, response.TotalBurnedAmount);
        Assert.Equal(2, response.Holders.Count);
        Assert.Equal(3, response.Assets.Count);
        Assert.Equal("Alice", response.Holders[0].UserName);
        Assert.Equal(101, response.Assets[0].Id);
    }
}