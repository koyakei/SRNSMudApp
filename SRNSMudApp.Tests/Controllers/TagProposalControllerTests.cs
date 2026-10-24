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

public class TagProposalControllerTests
{
    private readonly Mock<ITagNameProposalService> _nameProposalMock = new();
    private readonly Mock<ITagContentProposalService> _contentProposalMock = new();

    private TagProposalController CreateController(ClaimsPrincipal? user = null)
    {
        var controller = new TagProposalController(_nameProposalMock.Object, _contentProposalMock.Object);
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
    public async Task ProposeTagName_WhenUnauthenticated_ReturnsUnauthorized()
    {
        // Arrange
        var controller = CreateController();

        // Act
        var result = await controller.ProposeTagName(new ProposeTagNameRequest(1, "新タグ名"), CancellationToken.None);

        // Assert
        Assert.IsType<UnauthorizedObjectResult>(result.Result);
    }

    [Fact]
    public async Task ProposeTagName_WhenEmptyName_ReturnsBadRequest()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "u1")], "test"));
        var controller = CreateController(user);

        // Act
        var result = await controller.ProposeTagName(new ProposeTagNameRequest(1, "   "), CancellationToken.None);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task ProposeTagName_WhenSuccess_ReturnsOkDto()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "u1")], "test"));
        var controller = CreateController(user);

        var proposal = new TagNameProposal
        {
            Id = 10,
            TagId = 1,
            Tag = new Tag { Id = 1, Name = "旧タグ名", OwnerId = "owner-1" },
            ProposedName = "新タグ名",
            RequesterUserId = "u1",
            OwnerUserId = "owner-1",
            OwnerId = "u1",
            Reason = "改善提案",
            Status = TradeStatus.Proposed
        };

        _nameProposalMock
            .Setup(s => s.ProposeNameAsync(1, "新タグ名", "改善提案", "u1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Success<TagNameProposal>(proposal));

        // Act
        var result = await controller.ProposeTagName(new ProposeTagNameRequest(1, "新タグ名", "改善提案"), CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<TagNameProposalDto>(okResult.Value);
        Assert.Equal(10, dto.Id);
        Assert.Equal("新タグ名", dto.ProposedName);
        Assert.Equal("改善提案", dto.Reason);
    }

    [Fact]
    public async Task ProposeTagContent_WhenSuccess_ReturnsOkDto()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "u1")], "test"));
        var controller = CreateController(user);

        var proposal = new TagContentProposal
        {
            Id = 20,
            TagId = 1,
            Tag = new Tag { Id = 1, Name = "タグ", OwnerId = "owner-1" },
            ProposedContent = "新説明文",
            RequesterUserId = "u1",
            OwnerUserId = "owner-1",
            OwnerId = "u1",
            Reason = "補足追加",
            Status = TradeStatus.Proposed
        };

        _contentProposalMock
            .Setup(s => s.ProposeContentAsync(1, "新説明文", "補足追加", "u1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Success<TagContentProposal>(proposal));

        // Act
        var result = await controller.ProposeTagContent(new ProposeTagContentRequest(1, "新説明文", "補足追加"), CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<TagContentProposalDto>(okResult.Value);
        Assert.Equal(20, dto.Id);
        Assert.Equal("新説明文", dto.ProposedContent);
    }

    [Fact]
    public async Task GetPendingProposals_ReturnsPendingListDto()
    {
        // Arrange
        var controller = CreateController();

        var nameProposals = new List<TagNameProposal>
        {
            new() { Id = 1, TagId = 10, ProposedName = "提案名", RequesterUserId = "u1", OwnerUserId = "o1", OwnerId = "u1", Status = TradeStatus.Proposed }
        };
        var contentProposals = new List<TagContentProposal>
        {
            new() { Id = 2, TagId = 10, ProposedContent = "提案内容", RequesterUserId = "u2", OwnerUserId = "o1", OwnerId = "u2", Status = TradeStatus.Proposed }
        };

        _nameProposalMock
            .Setup(s => s.GetPendingProposalsForTagAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(nameProposals);
        _contentProposalMock
            .Setup(s => s.GetPendingProposalsForTagAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(contentProposals);

        // Act
        var result = await controller.GetPendingProposals(10, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<TagPendingProposalsDto>(okResult.Value);
        Assert.Single(dto.NameProposals);
        Assert.Single(dto.ContentProposals);
    }

    [Fact]
    public async Task ApproveNameProposal_WhenSuccess_ReturnsOkTrue()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "owner-1")], "test"));
        var controller = CreateController(user);

        var updatedTag = new Tag { Id = 1, Name = "新タグ名", OwnerId = "owner-1" };
        _nameProposalMock
            .Setup(s => s.ApproveProposalAsync(100, "owner-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Success<Tag>(updatedTag));

        // Act
        var result = await controller.ApproveNameProposal(100, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.True((bool)okResult.Value!);
    }

    [Fact]
    public async Task RejectNameProposal_WhenSuccess_ReturnsOkTrue()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "owner-1")], "test"));
        var controller = CreateController(user);

        _nameProposalMock
            .Setup(s => s.RejectProposalAsync(100, "owner-1", "不適切", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Success<bool>(true));

        // Act
        var result = await controller.RejectNameProposal(100, new RejectProposalRequest("不適切"), CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.True((bool)okResult.Value!);
    }
}