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
using SRNSMudApp.Services;

using Xunit;

public class InvitationControllerTests
{
    private readonly Mock<IAdminDataProvider> _adminDataProviderMock = new();

    private InvitationController CreateController(ClaimsPrincipal? user = null)
    {
        var controller = new InvitationController(_adminDataProviderMock.Object);
        var httpContext = new DefaultHttpContext();
        if (user != null)
        {
            httpContext.User = user;
        }
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    [Fact]
    public async Task GetInvitations_ReturnsMappedInvitations()
    {
        // Arrange
        var controller = CreateController();
        var invitations = new List<Invitation>
        {
            new()
            {
                Id = 1,
                Email = "invitee@example.com",
                InvitationCode = "code123",
                ExpirationDate = DateTime.UtcNow.AddDays(5),
                IsUsed = false,
                InvitedByAdminId = "admin1",
                OwnerId = "admin1"
            }
        };

        _adminDataProviderMock
            .Setup(p => p.GetInvitationsAsync())
            .ReturnsAsync(invitations);

        // Act
        ActionResult<IReadOnlyList<InvitationDto>> result =
            await controller.GetInvitations(CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dtos = Assert.IsAssignableFrom<IReadOnlyList<InvitationDto>>(okResult.Value);
        Assert.Single(dtos);
        Assert.Equal("invitee@example.com", dtos[0].Email);
        Assert.Equal("code123", dtos[0].InvitationCode);
        Assert.Equal("Pending", dtos[0].Status);
    }

    [Fact]
    public async Task CreateInvitation_EmptyEmail_ReturnsBadRequest()
    {
        // Arrange
        var controller = CreateController();
        var request = new CreateInvitationRequest(string.Empty);

        // Act
        ActionResult<InvitationDto> result =
            await controller.CreateInvitation(request, CancellationToken.None);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task CreateInvitation_ValidEmail_CreatesAndReturnsInvitation()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "admin1")], "test"));
        var controller = CreateController(user);
        var request = new CreateInvitationRequest("newuser@example.com");

        _adminDataProviderMock
            .Setup(p => p.CreateInvitationAsync(It.IsAny<Invitation>()))
            .Returns(Task.CompletedTask);

        // Act
        ActionResult<InvitationDto> result =
            await controller.CreateInvitation(request, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<InvitationDto>(okResult.Value);
        Assert.Equal("newuser@example.com", dto.Email);
        Assert.False(string.IsNullOrEmpty(dto.InvitationCode));
        Assert.Equal("Pending", dto.Status);
        Assert.Equal("admin1", dto.InvitedByAdminId);
    }

    [Fact]
    public async Task DeleteInvitation_NotFound_ReturnsNotFound()
    {
        // Arrange
        var controller = CreateController();
        _adminDataProviderMock
            .Setup(p => p.GetInvitationsAsync())
            .ReturnsAsync([]);

        // Act
        ActionResult<bool> result = await controller.DeleteInvitation(999, CancellationToken.None);

        // Assert
        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task DeleteInvitation_Found_DeletesAndReturnsTrue()
    {
        // Arrange
        var controller = CreateController();
        var target = new Invitation
        {
            Id = 42,
            Email = "del@example.com",
            InvitationCode = "delcode",
            ExpirationDate = DateTime.UtcNow.AddDays(1),
            InvitedByAdminId = "admin1",
            OwnerId = "admin1"
        };

        _adminDataProviderMock
            .Setup(p => p.GetInvitationsAsync())
            .ReturnsAsync([target]);

        _adminDataProviderMock
            .Setup(p => p.DeleteInvitationAsync(target))
            .Returns(Task.CompletedTask);

        // Act
        ActionResult<bool> result = await controller.DeleteInvitation(42, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.True((bool)okResult.Value!);
        _adminDataProviderMock.Verify(p => p.DeleteInvitationAsync(target), Times.Once);
    }
}