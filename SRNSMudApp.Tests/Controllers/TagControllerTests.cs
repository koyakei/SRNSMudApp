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
using SRNSMudApp.Models;
using SRNSMudApp.Services;

using Xunit;

public class TagControllerTests
{
    private readonly Mock<ITagCommandService> _tagCommandMock = new();
    private readonly Mock<ITagCardDataProvider> _tagCardDataMock = new();
    private readonly Mock<IHomeDataProvider> _homeDataMock = new();
    private readonly Mock<ITagDetailDataProvider> _tagDetailDataMock = new();
    private readonly Mock<ITagSearchQueryService> _tagSearchQueryMock = new();
    private readonly Mock<ITagSuggestionService> _tagSuggestionMock = new();

    private TagController CreateController(ClaimsPrincipal? user = null)
    {
        var controller = new TagController(
            _tagCommandMock.Object,
            _tagCardDataMock.Object,
            _homeDataMock.Object,
            null,
            _tagDetailDataMock.Object,
            _tagSearchQueryMock.Object,
            _tagSuggestionMock.Object);

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
    public async Task CreateTag_AuthorizedUser_CreatesAndReturnsTag()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-abc")], "test"));
        var controller = CreateController(user);

        _tagCommandMock
            .Setup(c => c.CreateTagAsync(It.IsAny<Tag>(), false))
            .Callback<Tag, bool>((t, _) => t.Id = 10)
            .Returns(Task.CompletedTask);

        // Act
        ActionResult<TagDto> result = await controller.CreateTag(new CreateTagRequest("MyTag", "Desc"), CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var tagDto = Assert.IsType<TagDto>(okResult.Value);
        Assert.Equal(10, tagDto.Id);
        Assert.Equal("MyTag", tagDto.Name);
    }

    [Fact]
    public async Task UpdateTag_AuthorizedUser_UpdatesTag()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-abc")], "test"));
        var controller = CreateController(user);

        _tagCommandMock
            .Setup(c => c.UpdateTagAsync(10, "NewName", "NewContent", false, null, false, false, null))
            .ReturnsAsync(true);

        // Act
        ActionResult result = await controller.UpdateTag(10, new UpdateTagRequest("NewName", "NewContent"), CancellationToken.None);

        // Assert
        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task DeleteTag_AuthorizedUser_DeletesTag()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-abc")], "test"));
        var controller = CreateController(user);

        _tagCommandMock
            .Setup(c => c.DeleteTagAsync(10, "user-abc", false))
            .ReturnsAsync(TagDeleteOperationResult.Success);

        // Act
        ActionResult result = await controller.DeleteTag(10, CancellationToken.None);

        // Assert
        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task VoteTag_AuthorizedUser_CallsDataProvider()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-abc")], "test"));
        var controller = CreateController(user);

        _homeDataMock
            .Setup(h => h.EnsureSystemTagsAsync("user-abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemTagsResult(1, 2, 3, 4, 5));

        _tagCardDataMock
            .Setup(d => d.ToggleTagVoteAsync(10, "user-abc", 1, 2))
            .Returns(Task.CompletedTask);

        // Act
        ActionResult result = await controller.VoteTag(10, new TagVoteRequest(true), CancellationToken.None);

        // Assert
        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task AddParentTag_AuthorizedUser_CallsDataProvider()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-abc")], "test"));
        var controller = CreateController(user);

        _tagCardDataMock
            .Setup(d => d.AddTagToTagAsync(10, 20, "user-abc"))
            .ReturnsAsync(TagCardOperationResult.Success);

        // Act
        ActionResult result = await controller.AddParentTag(10, new AddParentTagRequest(20), CancellationToken.None);

        // Assert
        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task RemoveParentTag_AuthorizedUser_CallsDataProvider()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-abc")], "test"));
        var controller = CreateController(user);

        _tagCardDataMock
            .Setup(d => d.RemoveRelationAsync(15, "user-abc"))
            .ReturnsAsync(TagCardOperationResult.Success);

        // Act
        ActionResult result = await controller.RemoveParentTag(10, 15, CancellationToken.None);

        // Assert
        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task GetTagTree_ReturnsHierarchicalTree()
    {
        // Arrange
        var tagTreeDataMock = new Mock<ITagTreeDataProvider>();
        var rootTag = new Tag { Id = 1, Name = "Root", ParentTagId = null, OwnerId = "user-1" };
        var childTag = new Tag { Id = 2, Name = "Child", ParentTagId = 1, OwnerId = "user-1" };

        tagTreeDataMock
            .Setup(t => t.LoadTagsAsync(It.IsAny<string?>(), It.IsAny<bool>()))
            .ReturnsAsync([rootTag, childTag]);

        var controller = new TagController(
            _tagCommandMock.Object,
            _tagCardDataMock.Object,
            _homeDataMock.Object,
            tagTreeDataMock.Object);

        // Act
        ActionResult<IReadOnlyList<TagTreeNodeDto>> result = await controller.GetTagTree(CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var tree = Assert.IsAssignableFrom<IReadOnlyList<TagTreeNodeDto>>(okResult.Value);
        Assert.Single(tree);
        Assert.Equal("Root", tree[0].Name);
        Assert.Single(tree[0].Children);
        Assert.Equal("Child", tree[0].Children[0].Name);
    }

    [Fact]
    public async Task GetTagDetail_NotFound_ReturnsNotFound()
    {
        // Arrange
        var controller = CreateController();
        _tagDetailDataMock
            .Setup(d => d.GetTagDetailAsync(999, It.IsAny<string?>(), It.IsAny<bool>()))
            .ReturnsAsync((TagDetailPageData?)null);

        // Act
        var result = await controller.GetTagDetail(999, CancellationToken.None);

        // Assert
        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetTagDetail_Found_ReturnsMappedDto()
    {
        // Arrange
        var controller = CreateController();
        var tag = new Tag { Id = 1, Name = "TestTag", OwnerId = "user-1", Content = "Test content" };
        var pageData = new TagDetailPageData(
            Tag: tag,
            IsFollowing: true,
            RelatedItems: [],
            RelatedTags: [],
            WeightLedgers: [],
            PublicOffers: [],
            PendingRequests: []
        );
        _tagDetailDataMock
            .Setup(d => d.GetTagDetailAsync(1, It.IsAny<string?>(), It.IsAny<bool>()))
            .ReturnsAsync(pageData);

        // Act
        var result = await controller.GetTagDetail(1, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<TagDetailDto>(okResult.Value);
        Assert.Equal(1, dto.Id);
        Assert.Equal("TestTag", dto.Name);
        Assert.True(dto.IsFollowing);
    }

    [Fact]
    public async Task ToggleFollow_Unauthorized_ReturnsUnauthorized()
    {
        // Arrange
        var controller = CreateController(); // 未認証

        // Act
        var result = await controller.ToggleFollow(1, CancellationToken.None);

        // Assert
        Assert.IsType<UnauthorizedObjectResult>(result.Result);
    }

    [Fact]
    public async Task ToggleFollow_Authorized_CallsService_ReturnsNewStatus()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-abc")], "test"));
        var controller = CreateController(user);
        _tagDetailDataMock
            .Setup(d => d.ToggleFollowAsync(1, "user-abc"))
            .ReturnsAsync(true);

        // Act
        var result = await controller.ToggleFollow(1, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ToggleFollowResponse>(okResult.Value);
        Assert.True(response.IsFollowing);
    }

    [Fact]
    public async Task GetTagList_NoQuery_ReturnsAllTags()
    {
        // Arrange
        var controller = CreateController();
        var tags = new List<Tag>
        {
            new() { Id = 1, Name = "Tag1", OwnerId = "u1" },
            new() { Id = 2, Name = "Tag2", OwnerId = "u2" }
        };

        _tagSearchQueryMock
            .Setup(s => s.GetTagsWithDetailsAsync(It.IsAny<string?>(), It.IsAny<bool>()))
            .ReturnsAsync(tags);

        // Act
        ActionResult<IReadOnlyList<TagDto>> result = await controller.GetTagList(null, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dtos = Assert.IsAssignableFrom<IReadOnlyList<TagDto>>(okResult.Value);
        Assert.Equal(2, dtos.Count);
        Assert.Equal("Tag1", dtos[0].Name);
    }

    [Fact]
    public async Task GetTagList_WithQuery_ReturnsFilteredTags()
    {
        // Arrange
        var controller = CreateController();
        var tags = new List<Tag>
        {
            new() { Id = 1, Name = "SearchedTag", OwnerId = "u1" }
        };

        _tagSearchQueryMock
            .Setup(s => s.SearchTagsAsync("query", It.IsAny<string?>(), It.IsAny<bool>()))
            .ReturnsAsync(tags);

        // Act
        ActionResult<IReadOnlyList<TagDto>> result = await controller.GetTagList("query", CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dtos = Assert.IsAssignableFrom<IReadOnlyList<TagDto>>(okResult.Value);
        Assert.Single(dtos);
        Assert.Equal("SearchedTag", dtos[0].Name);
    }

    [Fact]
    public async Task SuggestTags_WhenEmptyContent_ReturnsEmptyList()
    {
        // Arrange
        var controller = CreateController();

        // Act
        ActionResult<IReadOnlyList<SuggestedTagDto>> result =
            await controller.SuggestTags(new TagSuggestionRequest("   "), CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dtos = Assert.IsAssignableFrom<IReadOnlyList<SuggestedTagDto>>(okResult.Value);
        Assert.Empty(dtos);
    }

    [Fact]
    public async Task SuggestTags_WhenValidContent_ReturnsSuggestions()
    {
        // Arrange
        var controller = CreateController();
        var suggestions = new List<SuggestedTag>
        {
            new(1, "推論タグ1", 0.85f)
        };

        _tagSuggestionMock
            .Setup(s => s.SuggestTagsAsync("本文テキスト", 0.5f, It.IsAny<CancellationToken>()))
            .ReturnsAsync(suggestions);

        // Act
        ActionResult<IReadOnlyList<SuggestedTagDto>> result =
            await controller.SuggestTags(new TagSuggestionRequest("本文テキスト", 0.5f), CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dtos = Assert.IsAssignableFrom<IReadOnlyList<SuggestedTagDto>>(okResult.Value);
        Assert.Single(dtos);
        Assert.Equal("推論タグ1", dtos[0].TagName);
        Assert.Equal(0.85f, dtos[0].Score);
    }
}