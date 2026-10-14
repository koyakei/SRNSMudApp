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

public class ContractControllerTests
{
    private readonly Mock<IContractManagementDataProvider> _contractDataProviderMock = new();
    private readonly Mock<ITaggingContractService> _taggingContractServiceMock = new();

    private ContractController CreateController(ClaimsPrincipal? user = null)
    {
        var controller = new ContractController(
            _contractDataProviderMock.Object,
            _taggingContractServiceMock.Object);

        var httpContext = new DefaultHttpContext();
        if (user != null)
        {
            httpContext.User = user;
        }

        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    [Fact]
    public async Task GetContracts_Unauthorized_ReturnsUnauthorized()
    {
        // Arrange
        var controller = CreateController(); // 未認証

        // Act
        ActionResult<ContractPageResponse> result = await controller.GetContracts(CancellationToken.None);

        // Assert
        Assert.IsType<UnauthorizedObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetContracts_Authorized_ReturnsMappedContracts()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-1")], "test"));
        var controller = CreateController(user);

        var incomingContract = new TaggingRequestEntity
        {
            Id = 1,
            ContractType = "Gratis",
            OwnerId = "user-2",
            RequesterUserId = "user-2",
            TagOwnerUserId = "user-1",
            RequestedTagId = 10,
            RequestedTag = new Tag { Id = 10, Name = "C#", OwnerId = "user-1" },
            TargetItemId = 100,
            TargetItem = new Item { Id = 100, Content = "Content", OwnerId = "user-2" }
        };

        var outgoingContract = new TaggingRequestEntity
        {
            Id = 2,
            ContractType = "Mutual",
            OwnerId = "user-1",
            RequesterUserId = "user-1",
            TagOwnerUserId = "user-3",
            RequestedTagId = 20,
            RequestedTag = new Tag { Id = 20, Name = "Rust", OwnerId = "user-3" },
            TargetItemId = 200,
            TargetItem = new Item { Id = 200, Content = "Outgoing Content", OwnerId = "user-1" }
        };

        _contractDataProviderMock
            .Setup(p => p.GetContractsAsync("user-1"))
            .ReturnsAsync(new ContractManagementPageData([incomingContract], [outgoingContract]));

        // Act
        ActionResult<ContractPageResponse> result = await controller.GetContracts(CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var res = Assert.IsType<ContractPageResponse>(okResult.Value);
        Assert.Single(res.IncomingContracts);
        Assert.Single(res.OutgoingContracts);
        Assert.Equal("C#", res.IncomingContracts[0].RequestedTagName);
        Assert.Equal("Rust", res.OutgoingContracts[0].RequestedTagName);
        Assert.True(res.OutgoingContracts[0].CanCancel);
    }

    [Fact]
    public async Task AcceptContract_Authorized_CallsService_ReturnsOk()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-1")], "test"));
        var controller = CreateController(user);

        _taggingContractServiceMock
            .Setup(s => s.AcceptContractAsync(1, "user-1", null))
            .ReturnsAsync(new Success<string>("success"));

        // Act
        ActionResult result = await controller.AcceptContract(1, CancellationToken.None);

        // Assert
        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task RejectContract_Authorized_CallsService_ReturnsOk()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-1")], "test"));
        var controller = CreateController(user);

        _taggingContractServiceMock
            .Setup(s => s.CancelContractAsync(1, "user-1"))
            .ReturnsAsync(new Success<string>("canceled"));

        // Act
        ActionResult result = await controller.RejectContract(1, CancellationToken.None);

        // Assert
        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task CancelContract_Authorized_CallsService_ReturnsOk()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-1")], "test"));
        var controller = CreateController(user);

        _taggingContractServiceMock
            .Setup(s => s.CancelContractAsync(1, "user-1"))
            .ReturnsAsync(new Success<string>("canceled"));

        // Act
        ActionResult result = await controller.CancelContract(1, CancellationToken.None);

        // Assert
        Assert.IsType<OkResult>(result);
    }
}