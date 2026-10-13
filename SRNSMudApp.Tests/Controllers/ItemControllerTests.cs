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

        _dataProviderMock
            .Setup(p => p.GetReplyAndQuoteCountsAsync(
                It.IsAny<IReadOnlyList<int>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((new Dictionary<int, int> { [2] = 5 }, new Dictionary<int, int> { [2] = 2 }));

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
        Assert.Equal(5, response.Items[0].ReplyCount);
        Assert.Equal(2, response.Items[0].QuoteCount);
        Assert.Equal("user-1", response.Items[0].OwnerUserId);

        Assert.Single(response.Tags);
        Assert.Equal(10, response.Tags[0].Id);
        Assert.Equal("Tag 10", response.Tags[0].Name);
        Assert.Equal("user-1", response.Tags[0].OwnerUserId);
    }

    [Fact]
    public async Task QueryItems_CalculatesScoresAndMapsRelations()
    {
        // Arrange
        var controller = CreateController();

        var goodTag = new Tag { Id = 101, Name = "good", IsSystem = true, OwnerId = "user-1" };
        var badTag = new Tag { Id = 102, Name = "bad", IsSystem = true, OwnerId = "user-1" };
        var shinjiTag = new Tag { Id = 103, Name = ReactionTagNames.Shinji, OwnerId = "user-1" };
        var zenTag = new Tag { Id = 104, Name = ReactionTagNames.Zen, OwnerId = "user-1" };
        var normalTag = new Tag { Id = 105, Name = "nature", OwnerId = "user-1" };

        var item = new Item
        {
            Id = 42,
            OwnerId = "author-1",
            Content = "Test item",
            UpdatedDate = DateTime.UtcNow,
            QuotedItemId = 99,
            QuotedItem = new Item { Id = 99, OwnerId = "quoted-user-id", Content = "Quoted content", Owner = new ApplicationUser { UserName = "quoted-user" } },
            TargetUserGroup = new UserGroup { Name = "SpecialGroup", OwnerId = "author-1" },
            TagRelations =
            [
                new TagRelation { TagId = 101, Tag = goodTag, Weight = 3, OwnerId = "author-1" },
                new TagRelation { TagId = 103, Tag = shinjiTag, Weight = 2, OwnerId = "author-1" },
                new TagRelation { TagId = 104, Tag = zenTag, Weight = 1, OwnerId = "author-1" }
            ]
        };

        var tag = new Tag
        {
            Id = 200,
            Name = "custom-tag",
            Content = "Tag desc",
            OwnerId = "tag-author",
            IsPrivate = true,
            TargetTagRelations =
            [
                new TagRelationToTag { TagId = 101, Tag = goodTag, OwnerId = "tag-author" },
                new TagRelationToTag { TagId = 102, Tag = badTag, OwnerId = "tag-author" },
                new TagRelationToTag { TagId = 105, Tag = normalTag, Weight = 7, OwnerId = "tag-author" }
            ]
        };

        _dataProviderMock
            .Setup(p => p.LoadItemsAndTagsAsync(
                It.IsAny<IReadOnlyList<ItemListFilter>>(),
                It.IsAny<IReadOnlyList<ItemListSort>>(),
                It.IsAny<string?>(),
                It.IsAny<bool>()))
            .ReturnsAsync(new ItemListPageData([item], [tag]));

        _dataProviderMock
            .Setup(p => p.GetReplyAndQuoteCountsAsync(
                It.IsAny<IReadOnlyList<int>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((new Dictionary<int, int> { [42] = 4 }, new Dictionary<int, int> { [42] = 1 }));

        // Act
        ActionResult<ItemListQueryResponse> result = await controller.QueryItems(new ItemListQueryRequest(), CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ItemListQueryResponse>(okResult.Value);

        ItemDto itemDto = Assert.Single(response.Items);
        Assert.Equal(42, itemDto.Id);
        Assert.Equal(3, itemDto.Score);
        Assert.Equal(2, itemDto.ShinjiScore);
        Assert.Equal(1, itemDto.ZenScore);
        Assert.Equal(0, itemDto.BiScore);
        Assert.Equal(4, itemDto.ReplyCount);
        Assert.Equal(1, itemDto.QuoteCount);
        Assert.Equal("Quoted content", itemDto.QuotedItemContent);
        Assert.Equal("quoted-user", itemDto.QuotedItemOwnerUserName);
        Assert.Equal("SpecialGroup", itemDto.TargetUserGroupName);

        TagDto tagDto = Assert.Single(response.Tags);
        Assert.Equal(200, tagDto.Id);
        Assert.Equal(0, tagDto.Score); // 1 good - 1 bad = 0
        Assert.True(tagDto.IsPrivate);
        Assert.NotNull(tagDto.TargetTagRelations);
        Assert.Single(tagDto.TargetTagRelations); // only non-system tag
        Assert.Equal("nature", tagDto.TargetTagRelations[0].TagName);
        Assert.Equal(7, tagDto.TargetTagRelations[0].Weight);
    }
}