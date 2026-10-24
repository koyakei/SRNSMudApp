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
using SRNSMudApp.Services.Commands;

using Xunit;

public class BountyControllerTests
{
    private readonly Mock<IBountyDataProvider> _bountyDataProviderMock = new();
    private readonly Mock<ITaggingContractService> _taggingContractServiceMock = new();
    private readonly Mock<ICommandHandler<CreateBountyCommand, Result<bool>>> _createBountyHandlerMock = new();
    private readonly Mock<ITagDetailDataProvider> _tagDetailDataProviderMock = new();
    private readonly Mock<IItemCardDataProvider> _itemCardDataProviderMock = new();

    private BountyController CreateController(ClaimsPrincipal? user = null)
    {
        var controller = new BountyController(
            _bountyDataProviderMock.Object,
            _taggingContractServiceMock.Object,
            _createBountyHandlerMock.Object,
            _tagDetailDataProviderMock.Object,
            _itemCardDataProviderMock.Object);

        var httpContext = new DefaultHttpContext();
        if (user != null)
        {
            httpContext.User = user;
        }

        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    [Fact]
    public async Task GetActiveBounties_ReturnsMappedBounties()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-1")], "test"));
        var controller = CreateController(user);

        var bounty = new TaggingRequestEntity
        {
            Id = 1,
            OwnerId = "user-2",
            RequesterUserId = "user-2",
            RequestedTagId = 10,
            RequestedTag = new Tag { Id = 10, Name = "C#", OwnerId = "user-2" },
            TargetItemId = 100,
            TargetItem = new Item { Id = 100, Content = "Item Content", OwnerId = "user-2" },
            Payload = new BountyPayload(5)
        };

        var rewardAsset = new RightAsset
        {
            Id = 5,
            OwnerId = "user-2",
            TargetTag = new Tag { Id = 20, Name = "RewardTag", OwnerId = "user-2" }
        };

        var boardData = new BountyBoardData(
            Bounties: [bounty],
            RewardAssets: new Dictionary<int, RightAsset> { { 5, rewardAsset } }
        );

        _bountyDataProviderMock
            .Setup(p => p.GetActiveBountiesAsync())
            .ReturnsAsync(boardData);

        // Act
        ActionResult<BountyBoardResponse> result = await controller.GetActiveBounties(CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var res = Assert.IsType<BountyBoardResponse>(okResult.Value);
        Assert.Single(res.Bounties);
        Assert.Equal(1, res.Bounties[0].Id);
        Assert.Equal("C#", res.Bounties[0].RequestedTagName);
        Assert.Equal("RewardTag", res.Bounties[0].RewardTagName);
        Assert.False(res.Bounties[0].IsMyBounty);
        Assert.True(res.Bounties[0].CanFulfill);
    }

    [Fact]
    public async Task CreateBounty_Unauthorized_ReturnsUnauthorized()
    {
        // Arrange
        var controller = CreateController(); // 未認証

        // Act
        ActionResult result = await controller.CreateBounty(new CreateBountyRequest(100, 10), CancellationToken.None);

        // Assert
        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task CreateBounty_ValidRequest_CallsHandler_ReturnsOk()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-1")], "test"));
        var controller = CreateController(user);

        var tag = new Tag { Id = 10, Name = "TestTag", OwnerId = "user-1" };
        var item = new Item { Id = 100, Content = "Test", OwnerId = "user-1" };

        _tagDetailDataProviderMock
            .Setup(p => p.GetTagDetailAsync(10, "user-1"))
            .ReturnsAsync(new TagDetailPageData(tag, false, [], [], [], [], []));

        _itemCardDataProviderMock
            .Setup(p => p.GetItemByIdAsync(100))
            .ReturnsAsync(item);

        _createBountyHandlerMock
            .Setup(h => h.HandleAsync(It.IsAny<CreateBountyCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Success<bool>(true));

        // Act
        ActionResult result = await controller.CreateBounty(new CreateBountyRequest(100, 10), CancellationToken.None);

        // Assert
        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task FulfillBounty_Unauthorized_ReturnsUnauthorized()
    {
        // Arrange
        var controller = CreateController(); // 未認証

        // Act
        ActionResult result = await controller.FulfillBounty(1, null, CancellationToken.None);

        // Assert
        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task FulfillBounty_Authorized_CallsService_ReturnsOk()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-1")], "test"));
        var controller = CreateController(user);

        _taggingContractServiceMock
            .Setup(s => s.AcceptContractAsync(1, "user-1", null))
            .ReturnsAsync(new Success<string>("success"));

        // Act
        ActionResult result = await controller.FulfillBounty(1, null, CancellationToken.None);

        // Assert
        Assert.IsType<OkResult>(result);
    }
}