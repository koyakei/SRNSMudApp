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

public class PublicOfferControllerTests
{
    private readonly Mock<IPublicOfferDataProvider> _publicOfferDataProviderMock = new();
    private readonly Mock<ITaggingContractService> _taggingContractServiceMock = new();
    private readonly Mock<ICommandHandler<CreatePublicOfferCommand, Result<bool>>> _createPublicOfferHandlerMock = new();
    private readonly Mock<ICommandHandler<CreateTriggerContractCommand, Result<bool>>> _createTriggerContractHandlerMock = new();
    private readonly Mock<ITagDetailDataProvider> _tagDetailDataProviderMock = new();
    private readonly Mock<IItemCardDataProvider> _itemCardDataProviderMock = new();

    private PublicOfferController CreateController(ClaimsPrincipal? user = null)
    {
        var controller = new PublicOfferController(
            _publicOfferDataProviderMock.Object,
            _taggingContractServiceMock.Object,
            _createPublicOfferHandlerMock.Object,
            _createTriggerContractHandlerMock.Object,
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
    public async Task GetActiveOffers_ReturnsMappedOffers()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-1")], "test"));
        var controller = CreateController(user);

        var offer = new PublicTradeOffer
        {
            Id = 1,
            OfferedTagId = 10,
            OfferedTag = new Tag { Id = 10, Name = "C#", OwnerId = "user-2" },
            RequiredAssetAmount = 2,
            OwnerId = "user-2",
            Owner = new ApplicationUser { Id = "user-2", UserName = "bob" },
            IsActive = true,
            CreatedDate = DateTime.UtcNow
        };

        _publicOfferDataProviderMock
            .Setup(p => p.GetActivePublicOffersAsync())
            .ReturnsAsync([offer]);

        // Act
        ActionResult<IReadOnlyList<PublicOfferDto>> result = await controller.GetActiveOffers(CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dtos = Assert.IsAssignableFrom<IReadOnlyList<PublicOfferDto>>(okResult.Value);
        Assert.Single(dtos);
        Assert.Equal(1, dtos[0].Id);
        Assert.Equal("C#", dtos[0].OfferedTagName);
        Assert.Equal(2, dtos[0].RequiredAssetAmount);
        Assert.Equal("bob", dtos[0].OwnerUserName);
        Assert.False(dtos[0].IsOwner);
    }

    [Fact]
    public async Task CreateOffer_Unauthorized_ReturnsUnauthorized()
    {
        // Arrange
        var controller = CreateController(); // 未認証

        // Act
        ActionResult result = await controller.CreateOffer(new CreatePublicOfferRequest(10, 0), CancellationToken.None);

        // Assert
        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task CreateOffer_Valid_CallsHandler_ReturnsOk()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-1")], "test"));
        var controller = CreateController(user);

        var tag = new Tag { Id = 10, Name = "TestTag", OwnerId = "user-1" };
        _tagDetailDataProviderMock
            .Setup(p => p.GetTagDetailAsync(10, "user-1"))
            .ReturnsAsync(new TagDetailPageData(tag, false, [], [], [], [], []));

        _createPublicOfferHandlerMock
            .Setup(h => h.HandleAsync(It.IsAny<CreatePublicOfferCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Success<bool>(true));

        // Act
        ActionResult result = await controller.CreateOffer(new CreatePublicOfferRequest(10, 0), CancellationToken.None);

        // Assert
        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task CreateOffer_WhenNotTagOwner_ReturnsBadRequest()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "attacker")], "test"));
        var controller = CreateController(user);

        var tag = new Tag { Id = 10, Name = "TestTag", OwnerId = "real-owner" };
        _tagDetailDataProviderMock
            .Setup(p => p.GetTagDetailAsync(10, "attacker"))
            .ReturnsAsync(new TagDetailPageData(tag, false, [], [], [], [], []));

        // Act
        ActionResult result = await controller.CreateOffer(new CreatePublicOfferRequest(10, 0), CancellationToken.None);

        // Assert
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("自身が所有していないタグ", badRequest.Value?.ToString());
    }

    [Fact]
    public async Task DeactivateOffer_Unauthorized_ReturnsUnauthorized()
    {
        // Arrange
        var controller = CreateController(); // 未認証

        // Act
        ActionResult result = await controller.DeactivateOffer(1, CancellationToken.None);

        // Assert
        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task DeactivateOffer_Authorized_CallsProvider_ReturnsOk()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-1")], "test"));
        var controller = CreateController(user);

        _publicOfferDataProviderMock
            .Setup(p => p.DeactivatePublicOfferAsync(1, "user-1"))
            .ReturnsAsync(true);

        // Act
        ActionResult result = await controller.DeactivateOffer(1, CancellationToken.None);

        // Assert
        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task TriggerOffer_Valid_CreatesContractAndAccepts_ReturnsOk()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-1")], "test"));
        var controller = CreateController(user);

        var offer = new PublicTradeOffer
        {
            Id = 5,
            OfferedTagId = 10,
            OfferedTag = new Tag { Id = 10, Name = "Tag1", OwnerId = "user-2" },
            RequiredAssetAmount = 0,
            OwnerId = "user-2",
            IsActive = true
        };

        var item = new Item { Id = 100, Content = "My Item", OwnerId = "user-1" };

        _publicOfferDataProviderMock
            .Setup(p => p.GetActivePublicOffersAsync())
            .ReturnsAsync([offer]);

        _itemCardDataProviderMock
            .Setup(p => p.GetItemByIdAsync(100))
            .ReturnsAsync(item);

        _createTriggerContractHandlerMock
            .Setup(h => h.HandleAsync(It.IsAny<CreateTriggerContractCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Success<bool>(true));

        _taggingContractServiceMock
            .Setup(s => s.AcceptContractAsync(It.IsAny<int>(), "user-1", null))
            .ReturnsAsync(new Success<string>("success"));

        // Act
        ActionResult result = await controller.TriggerOffer(5, new TriggerPublicOfferRequest(100), CancellationToken.None);

        // Assert
        Assert.IsType<OkResult>(result);
    }
}