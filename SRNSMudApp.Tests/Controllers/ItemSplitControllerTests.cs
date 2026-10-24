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
            .Setup(s => s.RequestSplitAsync(1, "分割テキスト", "u2", It.IsAny<int?>(), It.IsAny<CancellationToken>()))
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
    public async Task RequestSplit_WithRequesterQuoteItemId_PassesQuoteItemIdToService()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "u2")], "test"));
        var controller = CreateController(user);

        var splitRequest = new ItemSplitRequest
        {
            Id = 51,
            OriginalItemId = 1,
            SelectedText = "分割テキスト",
            RequesterUserId = "u2",
            OwnerUserId = "u1",
            OwnerId = "u2",
            Status = TradeStatus.Proposed,
            RequesterQuoteItemId = 88,
            CreatedDate = DateTime.UtcNow
        };

        _itemSplitServiceMock
            .Setup(s => s.RequestSplitAsync(1, "分割テキスト", "u2", 88, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Success<ItemSplitRequest>(splitRequest));

        // Act
        var result = await controller.RequestSplit(new RequestItemSplitRequest(1, "分割テキスト", RequesterQuoteItemId: 88), CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<ItemSplitRequestDto>(okResult.Value);
        Assert.Equal(51, dto.Id);
        Assert.Equal(88, dto.RequesterQuoteItemId);
        _itemSplitServiceMock.Verify(s => s.RequestSplitAsync(1, "分割テキスト", "u2", 88, It.IsAny<CancellationToken>()), Times.Once);
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

    [Fact]
    public async Task RequestSplit_WhenSelectedTextIsEmpty_ReturnsBadRequest()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "u1")], "test"));
        var controller = CreateController(user);

        // Act
        var result = await controller.RequestSplit(new RequestItemSplitRequest(10, "   "), CancellationToken.None);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("分割対象のテキストを指定してください。", badRequestResult.Value);
    }

    [Fact]
    public async Task ApproveSplit_WhenUnauthenticated_ReturnsUnauthorized()
    {
        // Arrange
        var controller = CreateController(null);

        // Act
        var result = await controller.ApproveSplit(50, CancellationToken.None);

        // Assert
        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public async Task ApproveSplit_WhenFailure_ReturnsBadRequest()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "u1")], "test"));
        var controller = CreateController(user);

        _itemSplitServiceMock
            .Setup(s => s.ApproveSplitAsync(50, "u1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Failure("このリクエストを承認する権限がありません。"));

        // Act
        var result = await controller.ApproveSplit(50, CancellationToken.None);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("このリクエストを承認する権限がありません。", badRequestResult.Value);
    }

    [Fact]
    public async Task RejectSplit_WhenUnauthenticated_ReturnsUnauthorized()
    {
        // Arrange
        var controller = CreateController(null);

        // Act
        var result = await controller.RejectSplit(50, null, CancellationToken.None);

        // Assert
        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public async Task RejectSplit_WhenFailure_ReturnsBadRequest()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "u1")], "test"));
        var controller = CreateController(user);

        _itemSplitServiceMock
            .Setup(s => s.RejectSplitAsync(50, "u1", "理由", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Failure("申請中ではないリクエストは却下できません。"));

        // Act
        var result = await controller.RejectSplit(50, new RejectSplitRequest("理由"), CancellationToken.None);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("申請中ではないリクエストは却下できません。", badRequestResult.Value);
    }

    [Fact]
    public async Task CancelSplit_WhenUnauthenticated_ReturnsUnauthorized()
    {
        // Arrange
        var controller = CreateController(null);

        // Act
        var result = await controller.CancelSplit(50, CancellationToken.None);

        // Assert
        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public async Task CancelSplit_WhenFailure_ReturnsBadRequest()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "u2")], "test"));
        var controller = CreateController(user);

        _itemSplitServiceMock
            .Setup(s => s.CancelSplitAsync(50, "u2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Failure("このリクエストを取り下げる権限がありません。"));

        // Act
        var result = await controller.CancelSplit(50, CancellationToken.None);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("このリクエストを取り下げる権限がありません。", badRequestResult.Value);
    }

    [Fact]
    public void Constructor_WhenServiceNull_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new ItemSplitController(null!));
    }

    [Fact]
    public async Task DirectSplit_WhenRequestNull_ThrowsArgumentNullException()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "u1")], "test"));
        var controller = CreateController(user);

        await Assert.ThrowsAsync<ArgumentNullException>(() => controller.DirectSplit(null!, CancellationToken.None));
    }

    [Fact]
    public async Task RequestSplit_WhenRequestNull_ThrowsArgumentNullException()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "u1")], "test"));
        var controller = CreateController(user);

        await Assert.ThrowsAsync<ArgumentNullException>(() => controller.RequestSplit(null!, CancellationToken.None));
    }

    [Fact]
    public async Task RejectSplit_WhenRequestNull_PassesNullReasonAndReturnsOkTrue()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "u1")], "test"));
        var controller = CreateController(user);

        _itemSplitServiceMock
            .Setup(s => s.RejectSplitAsync(50, "u1", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Success<bool>(true));

        var result = await controller.RejectSplit(50, null, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.True((bool)okResult.Value!);
    }

    [Fact]
    public async Task RejectSplit_WhenReasonHasWhitespace_TrimsReason()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "u1")], "test"));
        var controller = CreateController(user);

        _itemSplitServiceMock
            .Setup(s => s.RejectSplitAsync(50, "u1", "トリム理由", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Success<bool>(true));

        var result = await controller.RejectSplit(50, new RejectSplitRequest("  トリム理由  "), CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.True((bool)okResult.Value!);
        _itemSplitServiceMock.Verify(s => s.RejectSplitAsync(50, "u1", "トリム理由", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetPendingSplitRequests_WhenEmpty_ReturnsOkEmptyList()
    {
        var controller = CreateController();

        _itemSplitServiceMock
            .Setup(s => s.GetPendingSplitRequestsForOriginalItemAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await controller.GetPendingSplitRequests(999, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsAssignableFrom<IReadOnlyList<ItemSplitRequestDto>>(okResult.Value);
        Assert.Empty(list);
    }

    [Fact]
    public async Task DirectSplit_WhenSelectedTextHasSurroundingSpaces_PreservesExactSelectedTextForMatching()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "u1")], "test"));
        var controller = CreateController(user);

        var newItem = new Item
        {
            Id = 102,
            Content = "  インデント付きテキスト  ",
            OwnerId = "u1",
            UpdatedDate = DateTime.UtcNow
        };

        _itemSplitServiceMock
            .Setup(s => s.SplitDirectlyAsync(1, "  インデント付きテキスト  ", "u1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Success<Item>(newItem));

        var result = await controller.DirectSplit(new DirectSplitItemRequest(1, "  インデント付きテキスト  "), CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<ItemDto>(okResult.Value);
        Assert.Equal("  インデント付きテキスト  ", dto.Content);
        _itemSplitServiceMock.Verify(s => s.SplitDirectlyAsync(1, "  インデント付きテキスト  ", "u1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RequestSplit_WhenSelectedTextHasSurroundingSpaces_PreservesExactSelectedTextForMatching()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "u2")], "test"));
        var controller = CreateController(user);

        var splitRequest = new ItemSplitRequest
        {
            Id = 55,
            OriginalItemId = 1,
            SelectedText = "  インデント付きテキスト  ",
            RequesterUserId = "u2",
            OwnerUserId = "u1",
            OwnerId = "u2",
            Status = TradeStatus.Proposed,
            CreatedDate = DateTime.UtcNow
        };

        _itemSplitServiceMock
            .Setup(s => s.RequestSplitAsync(1, "  インデント付きテキスト  ", "u2", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Success<ItemSplitRequest>(splitRequest));

        var result = await controller.RequestSplit(new RequestItemSplitRequest(1, "  インデント付きテキスト  "), CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<ItemSplitRequestDto>(okResult.Value);
        Assert.Equal("  インデント付きテキスト  ", dto.SelectedText);
        _itemSplitServiceMock.Verify(s => s.RequestSplitAsync(1, "  インデント付きテキスト  ", "u2", null, It.IsAny<CancellationToken>()), Times.Once);
    }
}