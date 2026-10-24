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

    private TagController CreateController(
        ClaimsPrincipal? user = null,
        ITagSimilarityService? tagSimilarityService = null,
        ITagHierarchyService? tagHierarchyService = null)
    {
        var controller = new TagController(
            _tagCommandMock.Object,
            _tagCardDataMock.Object,
            _homeDataMock.Object,
            null,
            _tagDetailDataMock.Object,
            _tagSearchQueryMock.Object,
            _tagSuggestionMock.Object,
            tagSimilarityService,
            tagHierarchyService);

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
            .Setup(c => c.UpdateTagAsync(10, "NewName", "NewContent", false, null, false, false, null, "user-abc"))
            .ReturnsAsync(true);

        // Act
        ActionResult result = await controller.UpdateTag(10, new UpdateTagRequest("NewName", "NewContent"), CancellationToken.None);

        // Assert
        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task UpdateTag_WhenUpdateFails_ReturnsBadRequest()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "attacker-user")], "test"));
        var controller = CreateController(user);

        _tagCommandMock
            .Setup(c => c.UpdateTagAsync(10, "NewName", "NewContent", false, null, false, false, null, "attacker-user"))
            .ReturnsAsync(false);

        // Act
        ActionResult result = await controller.UpdateTag(10, new UpdateTagRequest("NewName", "NewContent"), CancellationToken.None);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("タグの更新に失敗しました。", badRequestResult.Value);
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

    [Fact]
    public async Task GetTagLedger_WhenTagExists_ReturnsLedgers()
    {
        // Arrange
        var controller = CreateController();
        var tag = new Tag { Id = 10, Name = "TagName", OwnerId = "u1" };
        var ledgers = new List<TagWeightLedger>
        {
            new()
            {
                Id = 1,
                TagId = 10,
                Tag = tag,
                OwnerId = "u1",
                Owner = new ApplicationUser { Id = "u1", UserName = "Alice" },
                Reason = "ItemTagged",
                SourceType = "Item",
                PreviousWeight = 0,
                Delta = 5,
                NewWeight = 5,
                CreatedDate = DateTime.UtcNow
            }
        };

        var pageData = new TagDetailPageData(
            Tag: tag,
            IsFollowing: false,
            RelatedItems: [],
            RelatedTags: [],
            WeightLedgers: ledgers,
            PublicOffers: [],
            PendingRequests: []
        );

        _tagDetailDataMock
            .Setup(d => d.GetTagDetailAsync(10, It.IsAny<string?>(), It.IsAny<bool>()))
            .ReturnsAsync(pageData);

        // Act
        ActionResult<IReadOnlyList<TagWeightLedgerDto>> result =
            await controller.GetTagLedger(10, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dtos = Assert.IsAssignableFrom<IReadOnlyList<TagWeightLedgerDto>>(okResult.Value);
        Assert.Single(dtos);
        Assert.Equal(10, dtos[0].TagId);
        Assert.Equal("TagName", dtos[0].TagName);
        Assert.Equal(5, dtos[0].Delta);
        Assert.Equal("Alice", dtos[0].OwnerUserName);
    }

    [Fact]
    public async Task GetTagLedger_WhenTagNotFound_ReturnsNotFound()
    {
        // Arrange
        var controller = CreateController();
        _tagDetailDataMock
            .Setup(d => d.GetTagDetailAsync(999, It.IsAny<string?>(), It.IsAny<bool>()))
            .ReturnsAsync((TagDetailPageData?)null);

        // Act
        ActionResult<IReadOnlyList<TagWeightLedgerDto>> result =
            await controller.GetTagLedger(999, CancellationToken.None);

        // Assert
        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task FindSimilarTags_WhenServiceNotConfigured_ReturnsNotImplemented()
    {
        // Arrange
        var controller = CreateController();

        // Act
        ActionResult<IReadOnlyList<SimilarTagCandidateDto>> result =
            await controller.FindSimilarTags("test", 0.5f, 5, CancellationToken.None);

        // Assert
        var statusResult = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(StatusCodes.Status501NotImplemented, statusResult.StatusCode);
    }

    [Fact]
    public async Task FindSimilarTags_WhenServiceReturnsCandidates_ReturnsCandidates()
    {
        // Arrange
        var mockSimilarity = new Mock<ITagSimilarityService>();
        var candidateTag = new Tag { Id = 1, Name = "C#", OwnerId = "u1" };
        mockSimilarity
            .Setup(s => s.FindSimilarTags(It.IsAny<IEnumerable<Tag>>(), "CSharp", 0.6f, 3))
            .Returns([new SimilarTagCandidate(candidateTag, 0.95f)]);

        _tagSearchQueryMock
            .Setup(s => s.GetAllTagsAsync(It.IsAny<string?>(), It.IsAny<bool>()))
            .ReturnsAsync([candidateTag]);

        var controller = CreateController(tagSimilarityService: mockSimilarity.Object);

        // Act
        ActionResult<IReadOnlyList<SimilarTagCandidateDto>> result =
            await controller.FindSimilarTags("CSharp", 0.6f, 3, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dtos = Assert.IsAssignableFrom<IReadOnlyList<SimilarTagCandidateDto>>(okResult.Value);
        Assert.Single(dtos);
        Assert.Equal(1, dtos[0].TagId);
        Assert.Equal("C#", dtos[0].TagName);
        Assert.Equal(0.95f, dtos[0].Similarity);
    }

    [Fact]
    public async Task SuggestParentTag_WhenServiceNotConfigured_ReturnsNotImplemented()
    {
        // Arrange
        var controller = CreateController();

        // Act
        ActionResult<TagDto?> result =
            await controller.SuggestParentTag(new SuggestParentTagRequest("Test"), CancellationToken.None);

        // Assert
        var statusResult = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(StatusCodes.Status501NotImplemented, statusResult.StatusCode);
    }

    [Fact]
    public async Task SuggestParentTag_WhenServiceConfigured_ReturnsHierarchyCandidate()
    {
        // Arrange
        var mockService = new Mock<ITagHierarchyService>();
        var sampleTag = new Tag { Id = 10, Name = "Backend", Content = "Backend desc", OwnerId = "u1" };
        mockService
            .Setup(s => s.SuggestParentTagAsync("ASP.NET", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sampleTag);

        var controller = CreateController(tagHierarchyService: mockService.Object);

        // Act
        ActionResult<TagDto?> result =
            await controller.SuggestParentTag(new SuggestParentTagRequest("ASP.NET"), CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<TagDto>(okResult.Value);
        Assert.Equal(10, dto.Id);
        Assert.Equal("Backend", dto.Name);
    }
}