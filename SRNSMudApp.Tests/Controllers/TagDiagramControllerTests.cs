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
using SRNSMudApp.Services;

using Xunit;

public class TagDiagramControllerTests
{
    private readonly Mock<ITagDiagramDataProvider> _diagramDataMock = new();

    private TagDiagramController CreateController()
    {
        return new TagDiagramController(_diagramDataMock.Object);
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
}