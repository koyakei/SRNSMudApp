namespace SRNSMudApp.Tests.Controllers;

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using Moq;

using SRNSMudApp.Client.Models.Api;
using SRNSMudApp.Controllers;
using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;

using Xunit;

public class ItemSplitControllerTests
{
    private readonly Mock<IItemSplitService> _itemSplitServiceMock = new();

    private ItemSplitController CreateController(ClaimsPrincipal? user = null)
    {
        var controller = new ItemSplitController(_itemSplitServiceMock.Object);
        if (user != null)
        {
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user }
            };
        }
        return controller;
    }

    [Fact]
    public async Task DirectSplit_WhenUnauthenticated_ReturnsUnauthorized()
    {
        // Arrange
        var controller = CreateController();

        // Act
        var result = await controller.DirectSplit(new DirectSplitItemRequest(1, "分割テキスト"), CancellationToken.None);

        // Assert
        Assert.IsType<UnauthorizedObjectResult>(result.Result);
    }

    [Fact]
    public async Task DirectSplit_WhenEmptyText_ReturnsBadRequest()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "u1")], "test"));
        var controller = CreateController(user);

        // Act
        var result = await controller.DirectSplit(new DirectSplitItemRequest(1, "   "), CancellationToken.None);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task DirectSplit_WhenSuccess_ReturnsOkDto()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "u1")], "test"));
        var controller = CreateController(user);

        var newItem = new Item
        {
            Id = 100,
            Content = "分割テキスト",
            OwnerId = "u1",
            UpdatedDate = DateTime.UtcNow
        };

        _itemSplitServiceMock
            .Setup(s => s.SplitDirectlyAsync(1, "分割テキスト", "u1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Success<Item>(newItem));

        // Act
        var result = await controller.DirectSplit(new DirectSplitItemRequest(1, "分割テキスト"), CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<ItemDto>(okResult.Value);
        Assert.Equal(100, dto.Id);
        Assert.Equal("分割テキスト", dto.Content);
    }

    [Fact]
    public async Task DirectSplit_WhenFailure_ReturnsBadRequest()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "u1")], "test"));
        var controller = CreateController(user);

        _itemSplitServiceMock
            .Setup(s => s.SplitDirectlyAsync(1, "分割テキスト", "u1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Failure("分割権限がありません。"));

        // Act
        var result = await controller.DirectSplit(new DirectSplitItemRequest(1, "分割テキスト"), CancellationToken.None);

        // Assert
        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("分割権限がありません。", badRequest.Value);
    }

    [Fact]
    public async Task RequestSplit_WhenUnauthenticated_ReturnsUnauthorized()
    {
        // Arrange
        var controller = CreateController();

        // Act
        var result = await controller.RequestSplit(new RequestItemSplitRequest(1, "分割テキスト"), CancellationToken.None);

        // Assert
        Assert.IsType<UnauthorizedObjectResult>(result.Result);
    }

    [Fact]
    public async Task RequestSplit_WhenSuccess_ReturnsOkDto()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "u2")], "test"));
        var controller = CreateController(user);

        var splitRequest = new ItemSplitRequest
        {
            Id = 50,
            OriginalItemId = 1,
            SelectedText = "分割テキスト",
            RequesterUserId = "u2",
            OwnerUserId = "u1",
            OwnerId = "u2",
            Status = TradeStatus.Proposed,
            CreatedDate = DateTime.UtcNow
        };

        _itemSplitServiceMock
            .Setup(s => s.RequestSplitAsync(1, "分割テキスト", "u2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Success<ItemSplitRequest>(splitRequest));

        // Act
        var result = await controller.RequestSplit(new RequestItemSplitRequest(1, "分割テキスト"), CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<ItemSplitRequestDto>(okResult.Value);
        Assert.Equal(50, dto.Id);
        Assert.Equal("Proposed", dto.Status);
    }

    [Fact]
    public async Task GetPendingSplitRequests_ReturnsOkList()
    {
        // Arrange
        var controller = CreateController();
        var requests = new List<ItemSplitRequest>
        {
            new()
            {
                Id = 1,
                OriginalItemId = 10,
                SelectedText = "テキスト1",
                RequesterUserId = "u2",
                OwnerUserId = "u1",
                OwnerId = "u2",
                Status = TradeStatus.Proposed,
                CreatedDate = DateTime.UtcNow
            }
        };

        _itemSplitServiceMock
            .Setup(s => s.GetPendingSplitRequestsForOriginalItemAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(requests);

        // Act
        var result = await controller.GetPendingSplitRequests(10, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsAssignableFrom<IReadOnlyList<ItemSplitRequestDto>>(okResult.Value);
        Assert.Single(list);
        Assert.Equal("テキスト1", list[0].SelectedText);
    }

    [Fact]
    public async Task ApproveSplit_WhenSuccess_ReturnsOkDto()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "u1")], "test"));
        var controller = CreateController(user);

        var newItem = new Item
        {
            Id = 101,
            Content = "承認された分割テキスト",
            OwnerId = "u1",
            UpdatedDate = DateTime.UtcNow
        };

        _itemSplitServiceMock
            .Setup(s => s.ApproveSplitAsync(50, "u1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Success<Item>(newItem));

        // Act
        var result = await controller.ApproveSplit(50, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<ItemDto>(okResult.Value);
        Assert.Equal(101, dto.Id);
    }

    [Fact]
    public async Task RejectSplit_WhenSuccess_ReturnsOkTrue()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "u1")], "test"));
        var controller = CreateController(user);

        _itemSplitServiceMock
            .Setup(s => s.RejectSplitAsync(50, "u1", "不要な分割", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Success<bool>(true));

        // Act
        var result = await controller.RejectSplit(50, new RejectSplitRequest("不要な分割"), CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.True((bool)okResult.Value!);
    }

    [Fact]
    public async Task CancelSplit_WhenSuccess_ReturnsOkTrue()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "u2")], "test"));
        var controller = CreateController(user);

        _itemSplitServiceMock
            .Setup(s => s.CancelSplitAsync(50, "u2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Success<bool>(true));

        // Act
        var result = await controller.CancelSplit(50, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.True((bool)okResult.Value!);
    }
}