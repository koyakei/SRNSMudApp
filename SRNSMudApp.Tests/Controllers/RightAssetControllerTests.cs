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
    private readonly Mock<IRightAssetPurchaseService> _rightAssetPurchaseServiceMock = new();

    private RightAssetController CreateController(System.Security.Claims.ClaimsPrincipal? user = null, bool includePurchaseService = true)
    {
        var controller = new RightAssetController(
            _rightAssetDataProviderMock.Object,
            includePurchaseService ? _rightAssetPurchaseServiceMock.Object : null);

        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        if (user != null)
        {
            httpContext.User = user;
        }

        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    private static System.Security.Claims.ClaimsPrincipal CreateUser(string userId)
    {
        var claims = new[]
        {
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, userId),
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, $"User_{userId}")
        };
        var identity = new System.Security.Claims.ClaimsIdentity(claims, "TestAuth");
        return new System.Security.Claims.ClaimsPrincipal(identity);
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

    [Fact]
    public void GetNetworks_ReturnsNetworks()
    {
        // Arrange
        var networks = new List<JpycNetworkInfo>
        {
            new("polygon-amoy", "Polygon Amoy", 80002, "0x123", "https://faucet.com", "https://explorer.com", true, true)
        };
        _rightAssetPurchaseServiceMock.Setup(s => s.GetSupportedNetworks()).Returns(networks);
        var controller = CreateController();

        // Act
        ActionResult<IReadOnlyList<JpycNetworkDto>> result = controller.GetNetworks();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dtos = Assert.IsAssignableFrom<IReadOnlyList<JpycNetworkDto>>(okResult.Value);
        Assert.Single(dtos);
        Assert.Equal("polygon-amoy", dtos[0].Name);
        Assert.Equal("Polygon Amoy", dtos[0].DisplayName);
        Assert.True(dtos[0].IsRecommended);
    }

    [Fact]
    public void GetNetworks_WhenServiceNull_Returns501()
    {
        // Arrange
        var controller = CreateController(includePurchaseService: false);

        // Act
        ActionResult<IReadOnlyList<JpycNetworkDto>> result = controller.GetNetworks();

        // Assert
        var statusResult = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(Microsoft.AspNetCore.Http.StatusCodes.Status501NotImplemented, statusResult.StatusCode);
    }

    [Fact]
    public async Task GetDepositWallet_Unauthorized_ReturnsUnauthorized()
    {
        // Arrange
        var controller = CreateController(); // user is null

        // Act
        ActionResult<DepositWalletDto> result = await controller.GetDepositWallet();

        // Assert
        Assert.IsType<UnauthorizedObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetDepositWallet_Authorized_ReturnsWallet()
    {
        // Arrange
        var user = CreateUser("user-42");
        var controller = CreateController(user);
        var walletDto = new UserDepositWalletDto("user-42", "polygon-amoy", "0xDepositWalletAddress", DateTime.UtcNow);
        _rightAssetPurchaseServiceMock
            .Setup(s => s.GetOrCreateUserDepositWalletAsync("user-42", "polygon-amoy", It.IsAny<CancellationToken>()))
            .ReturnsAsync(walletDto);

        // Act
        ActionResult<DepositWalletDto> result = await controller.GetDepositWallet("polygon-amoy", CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<DepositWalletDto>(okResult.Value);
        Assert.Equal("user-42", dto.UserId);
        Assert.Equal("polygon-amoy", dto.NetworkName);
        Assert.Equal("0xDepositWalletAddress", dto.DepositAddress);
    }

    [Fact]
    public async Task SimulateDeposit_Authorized_ReturnsTxHash()
    {
        // Arrange
        var user = CreateUser("user-42");
        var controller = CreateController(user);
        var request = new SimulateDepositRequest("polygon-amoy", 500);
        _rightAssetPurchaseServiceMock
            .Setup(s => s.SimulateDepositAsync("user-42", "polygon-amoy", 500, It.IsAny<CancellationToken>()))
            .ReturnsAsync("0xSimulatedTxHash");

        // Act
        ActionResult<string> result = await controller.SimulateDeposit(request, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("0xSimulatedTxHash", okResult.Value);
    }

    [Fact]
    public async Task PurchaseRightAsset_Success_ReturnsResultDto()
    {
        // Arrange
        var user = CreateUser("user-42");
        var controller = CreateController(user);
        var request = new PurchaseRightAssetRequest(
            RequestedTagId: 10,
            Amount: 5,
            UnitPriceJpyc: 100,
            NetworkName: "polygon-amoy",
            TransactionHash: "0xRealTxHash",
            SenderWalletAddress: "0xSender"
        );

        var rightAsset = new RightAsset
        {
            Id = 99,
            TargetTagId = 10,
            Amount = 5,
            OwnerId = "user-42",
            TargetTag = new Tag { Id = 10, Name = "TargetTag", OwnerId = "user-42" }
        };

        _rightAssetPurchaseServiceMock
            .Setup(s => s.PurchaseRightAssetWithJpycAsync(
                "user-42",
                It.Is<JpycPurchaseRequestDto>(r => r.RequestedTagId == 10 && r.Amount == 5),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SRNSMudApp.Models.Unions.Success<RightAsset>(rightAsset));

        // Act
        ActionResult<PurchaseRightAssetResultDto> result = await controller.PurchaseRightAsset(request, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<PurchaseRightAssetResultDto>(okResult.Value);
        Assert.Equal(99, dto.RightAssetId);
        Assert.Equal(10, dto.TagId);
        Assert.Equal("TargetTag", dto.TagName);
        Assert.Equal(5, dto.Amount);
        Assert.Equal(500, dto.TotalJpycAmount);
        Assert.Equal("polygon-amoy", dto.NetworkName);
        Assert.Equal("0xRealTxHash", dto.TransactionHash);
    }

    [Fact]
    public async Task PurchaseRightAsset_Failure_ReturnsBadRequest()
    {
        // Arrange
        var user = CreateUser("user-42");
        var controller = CreateController(user);
        var request = new PurchaseRightAssetRequest(
            RequestedTagId: 10,
            Amount: 5,
            UnitPriceJpyc: 100,
            NetworkName: "polygon-amoy",
            TransactionHash: "0xInvalidTxHash"
        );

        _rightAssetPurchaseServiceMock
            .Setup(s => s.PurchaseRightAssetWithJpycAsync(
                "user-42",
                It.IsAny<JpycPurchaseRequestDto>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SRNSMudApp.Models.Unions.Failure("トランザクションが不正です。"));

        // Act
        ActionResult<PurchaseRightAssetResultDto> result = await controller.PurchaseRightAsset(request, CancellationToken.None);

        // Assert
        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("トランザクションが不正です。", badRequest.Value);
    }
}