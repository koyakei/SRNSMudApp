namespace SRNSMudApp.Tests.Controllers;

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using Moq;

using SRNSMudApp.Client.Models;
using SRNSMudApp.Client.Models.Api;
using SRNSMudApp.Controllers;
using SRNSMudApp.Data;
using SRNSMudApp.Services;

using Xunit;

public class ItemControllerTests
{
    private readonly Mock<IItemListDataProvider> _dataProviderMock = new();

    private ItemController CreateController(ClaimsPrincipal? user = null)
    {
        var controller = new ItemController(_dataProviderMock.Object);
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
    public async Task SearchTags_EmptyQuery_ReturnsEmptyList()
    {
        // Arrange
        var controller = CreateController();

        // Act
        ActionResult<IReadOnlyList<TagSuggestion>> result = await controller.SearchTags(string.Empty, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var tags = Assert.IsAssignableFrom<IReadOnlyList<TagSuggestion>>(okResult.Value);
        Assert.Empty(tags);
    }

    [Fact]
    public async Task SearchTags_ValidQuery_ReturnsSuggestions()
    {
        // Arrange
        var expected = new List<TagSuggestion>
        {
            new(1, "test-tag", "alice")
        };
        _dataProviderMock
            .Setup(p => p.SearchTagNameSuggestionsAsync("test", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var controller = CreateController();

        // Act
        ActionResult<IReadOnlyList<TagSuggestion>> result = await controller.SearchTags("test", CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var tags = Assert.IsAssignableFrom<IReadOnlyList<TagSuggestion>>(okResult.Value);
        Assert.Single(tags);
        Assert.Equal("test-tag", tags[0].TagName);
    }

    [Fact]
    public async Task SearchTagUsers_EmptyTagName_ReturnsBadRequest()
    {
        // Arrange
        var controller = CreateController();

        // Act
        ActionResult<IReadOnlyList<string>> result = await controller.SearchTagUsers(string.Empty, null, CancellationToken.None);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task SearchTagUsers_ValidTagName_ReturnsUsers()
    {
        // Arrange
        var expected = new List<string> { "test @alice", "test @bob" };
        _dataProviderMock
            .Setup(p => p.SearchTagUserNamesAsync("test", "al", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var controller = CreateController();

        // Act
        ActionResult<IReadOnlyList<string>> result = await controller.SearchTagUsers("test", "al", CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var users = Assert.IsAssignableFrom<IReadOnlyList<string>>(okResult.Value);
        Assert.Equal(2, users.Count);
    }

    [Fact]
    public async Task QueryItems_MapsRequestToDomain_AndPaginatesResults()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "user-123")],
            "test"
        ));
        var controller = CreateController(user);

        var items = new List<Item>
        {
            new() { Id = 1, OwnerId = "user-1", Content = "Item 1", UpdatedDate = DateTime.UtcNow },
            new() { Id = 2, OwnerId = "user-1", Content = "Item 2", UpdatedDate = DateTime.UtcNow },
            new() { Id = 3, OwnerId = "user-1", Content = "Item 3", UpdatedDate = DateTime.UtcNow }
        };
        var tags = new List<Tag>
        {
            new() { Id = 10, OwnerId = "user-1", Name = "Tag 10", Content = "Tag Content" }
        };

        _dataProviderMock
            .Setup(p => p.LoadItemsAndTagsAsync(
                It.Is<IReadOnlyList<ItemListFilter>>(f => f.Count == 2),
                It.Is<IReadOnlyList<ItemListSort>>(s => s.Count == 1),
                "user-123",
                false))
            .ReturnsAsync(new ItemListPageData(items, tags));

        var request = new ItemListQueryRequest(
            Filters:
            [
                new ItemListFilterDto(TagId: 10, TagName: null, UserName: null),
                new ItemListFilterDto(TagId: null, TagName: "music", UserName: "alice")
            ],
            Sorts:
            [
                new ItemListSortDto(TagId: 10, Ascending: false)
            ],
            Skip: 1,
            Take: 1
        );

        // Act
        ActionResult<ItemListQueryResponse> result = await controller.QueryItems(request, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ItemListQueryResponse>(okResult.Value);

        Assert.Equal(3, response.TotalCount);
        Assert.Single(response.Items);
        Assert.Equal(2, response.Items[0].Id);
        Assert.Equal("Item 2", response.Items[0].Content);

        Assert.Single(response.Tags);
        Assert.Equal(10, response.Tags[0].Id);
        Assert.Equal("Tag 10", response.Tags[0].Name);
    }
}