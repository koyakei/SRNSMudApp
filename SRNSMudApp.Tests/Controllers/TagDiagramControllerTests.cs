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

public class TagDiagramControllerTests
{
    private readonly Mock<ITagDiagramDataProvider> _diagramDataMock = new();
    private readonly Mock<ITagEdgeService> _tagEdgeServiceMock = new();

    private TagDiagramController CreateController(ClaimsPrincipal? user = null, ITagEdgeService? tagEdgeService = null)
    {
        var controller = new TagDiagramController(_diagramDataMock.Object, tagEdgeService);
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
    public async Task GetDiagramData_ReturnsNodesEdgesAndItems()
    {
        // Arrange
        var controller = CreateController();
        var tag = new Tag { Id = 1, Name = "Tag 1", OwnerId = "user-1" };
        var edge = new TagEdge { Id = 10, SourceTagId = 1, TargetTagId = 2, OwnerId = "user-1" };
        var item = new Item { Id = 100, Content = "Item 100", OwnerId = "user-1" };

        _diagramDataMock.Setup(d => d.LoadAllTagsAsync()).ReturnsAsync([tag]);
        _diagramDataMock.Setup(d => d.LoadAllEdgesAsync()).ReturnsAsync([edge]);
        _diagramDataMock.Setup(d => d.GetContextItemsAsync(It.IsAny<IEnumerable<int>>())).ReturnsAsync([item]);

        // Act
        ActionResult<TagDiagramResponse> result = await controller.GetDiagramData("100", CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<TagDiagramResponse>(okResult.Value);
        Assert.Single(response.Nodes);
        Assert.Equal("Tag 1", response.Nodes[0].Name);
        Assert.Single(response.Edges);
        Assert.Equal(10, response.Edges[0].Id);
        Assert.Single(response.ContextItems);
        Assert.Equal(100, response.ContextItems[0].Id);
    }

    [Fact]
    public async Task CreateEdge_WhenUnauthorized_ReturnsUnauthorized()
    {
        // Arrange
        var controller = CreateController(user: null, tagEdgeService: _tagEdgeServiceMock.Object);

        // Act
        ActionResult<TagDiagramEdgeDto> result =
            await controller.CreateEdge(new CreateTagEdgeRequest(1, 2));

        // Assert
        Assert.IsType<UnauthorizedObjectResult>(result.Result);
    }

    [Fact]
    public async Task CreateEdge_WhenAuthorized_CreatesEdgeSuccessfully()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "u1")], "test"));
        var createdEdge = new TagEdge { Id = 101, SourceTagId = 1, TargetTagId = 2, OwnerId = "u1", Owner = new ApplicationUser { Id = "u1", UserName = "Alice" } };
        _tagEdgeServiceMock
            .Setup(s => s.CreateEdgeAsync(1, 2, "u1"))
            .ReturnsAsync(new Success<TagEdge>(createdEdge));

        var controller = CreateController(user: user, tagEdgeService: _tagEdgeServiceMock.Object);

        // Act
        ActionResult<TagDiagramEdgeDto> result =
            await controller.CreateEdge(new CreateTagEdgeRequest(1, 2));

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<TagDiagramEdgeDto>(okResult.Value);
        Assert.Equal(101, dto.Id);
        Assert.Equal(1, dto.SourceTagId);
        Assert.Equal(2, dto.TargetTagId);
        Assert.Equal("Alice", dto.OwnerUserName);
    }

    [Fact]
    public async Task DeleteEdge_WhenAuthorized_DeletesEdgeSuccessfully()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "u1")], "test"));
        _tagEdgeServiceMock
            .Setup(s => s.DeleteEdgeAsync(101, "u1"))
            .ReturnsAsync(new Success<bool>(true));

        var controller = CreateController(user: user, tagEdgeService: _tagEdgeServiceMock.Object);

        // Act
        IActionResult result = await controller.DeleteEdge(101);

        // Assert
        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task GetAttachmentsForEdge_ReturnsAttachmentList()
    {
        // Arrange
        var attachment = new TagEdgeTagAttachment
        {
            Id = 50,
            TagEdgeId = 101,
            TagId = 5,
            Tag = new Tag { Id = 5, Name = "Cause", OwnerId = "u1" },
            Weight = 2,
            OwnerId = "u1",
            Owner = new ApplicationUser { Id = "u1", UserName = "Bob" },
            CreatedDate = DateTime.UtcNow
        };
        _tagEdgeServiceMock
            .Setup(s => s.GetAttachmentsForEdgeAsync(101))
            .ReturnsAsync([attachment]);

        var controller = CreateController(tagEdgeService: _tagEdgeServiceMock.Object);

        // Act
        ActionResult<IReadOnlyList<TagEdgeAttachmentDto>> result =
            await controller.GetAttachmentsForEdge(101);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dtos = Assert.IsAssignableFrom<IReadOnlyList<TagEdgeAttachmentDto>>(okResult.Value);
        Assert.Single(dtos);
        Assert.Equal(50, dtos[0].Id);
        Assert.Equal("Cause", dtos[0].TagName);
        Assert.Equal("Bob", dtos[0].OwnerUserName);
    }

    [Fact]
    public async Task AttachTagToEdge_WhenAuthorized_AttachesTagSuccessfully()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "u1")], "test"));
        var attachment = new TagEdgeTagAttachment
        {
            Id = 51,
            TagEdgeId = 101,
            TagId = 5,
            Tag = new Tag { Id = 5, Name = "Relation", OwnerId = "u1" },
            Weight = 1,
            OwnerId = "u1",
            Owner = new ApplicationUser { Id = "u1", UserName = "Alice" },
            CreatedDate = DateTime.UtcNow
        };
        _tagEdgeServiceMock
            .Setup(s => s.AttachTagToEdgeAsync(101, 5, 200, "u1", 1))
            .ReturnsAsync(new Success<TagEdgeTagAttachment>(attachment));

        var controller = CreateController(user: user, tagEdgeService: _tagEdgeServiceMock.Object);

        // Act
        ActionResult<TagEdgeAttachmentDto> result =
            await controller.AttachTagToEdge(101, new AttachTagToEdgeRequest(5, 200, 1));

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<TagEdgeAttachmentDto>(okResult.Value);
        Assert.Equal(51, dto.Id);
        Assert.Equal("Relation", dto.TagName);
    }

    [Fact]
    public async Task DetachTagFromEdge_WhenAuthorized_DetachesSuccessfully()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "u1")], "test"));
        _tagEdgeServiceMock
            .Setup(s => s.DetachTagFromEdgeAsync(51, "u1"))
            .ReturnsAsync(new Success<bool>(true));

        var controller = CreateController(user: user, tagEdgeService: _tagEdgeServiceMock.Object);

        // Act
        IActionResult result = await controller.DetachTagFromEdge(51);

        // Assert
        Assert.IsType<NoContentResult>(result);
    }
}