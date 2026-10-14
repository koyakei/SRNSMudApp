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
    private readonly Mock<IItemListExportService> _exportServiceMock = new();
    private readonly Mock<IHomeDataProvider> _homeDataMock = new();
    private readonly Mock<IItemReactionService> _itemReactionMock = new();
    private readonly Mock<IItemCardDataProvider> _itemCardDataMock = new();
    private readonly Mock<IItemReplyService> _itemReplyMock = new();
    private readonly Mock<IItemQuoteService> _itemQuoteMock = new();
    private readonly Mock<IItemTagService> _itemTagMock = new();
    private readonly Mock<IUserGroupDataProvider> _userGroupMock = new();
    private readonly Mock<IAdminDataProvider> _adminDataMock = new();
    private readonly Mock<ITaggingRequestActions> _taggingRequestActionsMock = new();
    private readonly Mock<ITagRequestRejectionService> _tagRequestRejectionMock = new();

    private ItemController CreateController(ClaimsPrincipal? user = null)
    {
        var controller = new ItemController(
            _dataProviderMock.Object,
            _exportServiceMock.Object,
            _homeDataMock.Object,
            _itemReactionMock.Object,
            _itemCardDataMock.Object,
            _itemReplyMock.Object,
            _itemQuoteMock.Object,
            _itemTagMock.Object,
            _userGroupMock.Object,
            _adminDataMock.Object,
            _taggingRequestActionsMock.Object,
            _tagRequestRejectionMock.Object);
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

    [Fact]
    public async Task ExportItems_ValidRequest_ReturnsSerializedJson()
    {
        // Arrange
        var controller = CreateController();
        var item = new Item { Id = 1, Content = "Content 1", OwnerId = "u1" };
        var exportData = new ItemListExportData(new Dictionary<int, Tag>(), [], []);

        _dataProviderMock
            .Setup(p => p.LoadExportDataAsync(It.IsAny<IReadOnlyList<int>>()))
            .ReturnsAsync(exportData);
        _dataProviderMock
            .Setup(p => p.LoadItemsAndTagsAsync(It.IsAny<IReadOnlyList<ItemListFilter>>(), It.IsAny<IReadOnlyList<ItemListSort>>(), It.IsAny<string?>(), It.IsAny<bool>()))
            .ReturnsAsync(new ItemListPageData([item], []));
        _exportServiceMock
            .Setup(s => s.BuildExportAsync(exportData, It.IsAny<IEnumerable<Item>>()))
            .ReturnsAsync([new ExportItemDto { Content = "Content 1", Owner = new ExportOwnerDto { Name = "u1" } }]);

        // Act
        ActionResult<string> result = await controller.ExportItems(new ItemListExportRequest([1]), CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var json = Assert.IsType<string>(okResult.Value);
        Assert.Contains("Content 1", json);
    }

    [Fact]
    public async Task VoteItem_UnauthorizedWhenNoUser_ReturnsUnauthorized()
    {
        // Arrange (no user)
        var controller = CreateController();

        // Act
        ActionResult<ItemVoteResult> result = await controller.VoteItem(1, new ItemVoteRequest(true), CancellationToken.None);

        // Assert
        Assert.IsType<UnauthorizedObjectResult>(result.Result);
    }

    [Fact]
    public async Task VoteItem_AuthorizedUser_ExecutesToggleVote()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-abc")], "test"));
        var controller = CreateController(user);

        _homeDataMock
            .Setup(h => h.EnsureSystemTagsAsync("user-abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemTagsResult(GoodTagId: 10, BadTagId: 11, ShinjiTagId: 12, ZenTagId: 13, BiTagId: 14, Created: false));

        _itemReactionMock
            .Setup(r => r.ToggleItemVoteAsync(1, "user-abc", 10, 1, null))
            .ReturnsAsync(new ItemVoteResult(ItemVoteAction.Added, 100, 1));

        // Act
        ActionResult<ItemVoteResult> result = await controller.VoteItem(1, new ItemVoteRequest(true), CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var voteResult = Assert.IsType<ItemVoteResult>(okResult.Value);
        Assert.Equal(ItemVoteAction.Added, voteResult.Action);
        Assert.Equal(100, voteResult.RelationId);
    }

    [Fact]
    public async Task ReactItem_AuthorizedUser_ExecutesToggleReaction()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-abc")], "test"));
        var controller = CreateController(user);

        var reactionTag = new Tag { Id = 50, Name = "真実", OwnerId = "user-abc" };
        _itemReactionMock
            .Setup(r => r.EnsureReactionTagAsync("user-abc", "真実"))
            .ReturnsAsync(reactionTag);

        _itemReactionMock
            .Setup(r => r.ToggleItemReactionAsync(1, "user-abc", 50, 1, null))
            .ReturnsAsync(new ItemVoteResult(ItemVoteAction.Added, 101, 1));

        // Act
        ActionResult<ItemVoteResult> result = await controller.ReactItem(1, new ItemReactionRequest("真実"), CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var reactionResult = Assert.IsType<ItemVoteResult>(okResult.Value);
        Assert.Equal(ItemVoteAction.Added, reactionResult.Action);
    }

    [Fact]
    public async Task CreateItem_AuthorizedUser_CreatesAndReturnsItem()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-abc")], "test"));
        var controller = CreateController(user);

        Item? capturedItem = null;
        _itemCardDataMock
            .Setup(d => d.CreateItemAsync(It.IsAny<Item>(), It.IsAny<IReadOnlyCollection<int>?>()))
            .Callback<Item, IReadOnlyCollection<int>?>((item, _) =>
            {
                capturedItem = item;
                item.Id = 999;
            })
            .Returns(Task.CompletedTask);

        _itemCardDataMock
            .Setup(d => d.GetItemByIdAsync(999))
            .ReturnsAsync(() => capturedItem);

        // Act
        ActionResult<ItemDto> result = await controller.CreateItem(new CreateItemRequest("New item content", false, [1, 2]), CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var itemDto = Assert.IsType<ItemDto>(okResult.Value);
        Assert.Equal(999, itemDto.Id);
        Assert.Equal("New item content", itemDto.Content);
        Assert.Equal("user-abc", itemDto.OwnerUserId);
    }

    [Fact]
    public async Task GetReplies_ReturnsReplies()
    {
        // Arrange
        var controller = CreateController();
        var replyItem = new Item { Id = 101, Content = "Reply 1", ParentItemId = 1, OwnerId = "user-abc" };
        _itemReplyMock.Setup(r => r.GetItemRepliesAsync(1)).ReturnsAsync([replyItem]);

        // Act
        ActionResult<IReadOnlyList<ItemDto>> result = await controller.GetReplies(1, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var replies = Assert.IsAssignableFrom<IReadOnlyList<ItemDto>>(okResult.Value);
        Assert.Single(replies);
        Assert.Equal(101, replies[0].Id);
    }

    [Fact]
    public async Task AddReply_AuthorizedUser_CreatesAndReturnsReply()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-abc")], "test"));
        var controller = CreateController(user);
        var parent = new Item { Id = 1, Content = "Parent", OwnerId = "user-abc" };
        var createdReply = new Item { Id = 102, Content = "New Reply", ParentItemId = 1, OwnerId = "user-abc" };

        _itemCardDataMock.Setup(d => d.GetItemByIdAsync(1)).ReturnsAsync(parent);
        _itemReplyMock
            .Setup(r => r.AddItemReplyAsync(1, "New Reply", "user-abc", null, false, null))
            .ReturnsAsync(createdReply);

        // Act
        ActionResult<ItemDto> result = await controller.AddReply(1, new CreateReplyRequest("New Reply"), CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var replyDto = Assert.IsType<ItemDto>(okResult.Value);
        Assert.Equal(102, replyDto.Id);
        Assert.Equal("New Reply", replyDto.Content);
    }

    [Fact]
    public async Task GetQuotes_ReturnsQuotes()
    {
        // Arrange
        var controller = CreateController();
        var quoteItem = new Item { Id = 201, Content = "Quote 1", QuotedItemId = 1, OwnerId = "user-abc" };
        _itemQuoteMock.Setup(q => q.GetQuotedByItemsAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync([quoteItem]);

        // Act
        ActionResult<IReadOnlyList<ItemDto>> result = await controller.GetQuotes(1, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var quotes = Assert.IsAssignableFrom<IReadOnlyList<ItemDto>>(okResult.Value);
        Assert.Single(quotes);
        Assert.Equal(201, quotes[0].Id);
    }

    [Fact]
    public async Task UpdateItem_Owner_UpdatesContent()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-abc")], "test"));
        var controller = CreateController(user);
        var existing = new Item { Id = 1, Content = "Old", OwnerId = "user-abc" };

        _itemCardDataMock.Setup(d => d.GetItemByIdAsync(1)).ReturnsAsync(existing);
        _itemCardDataMock.Setup(d => d.UpdateItemContentAsync(1, "Updated")).ReturnsAsync(true);

        // Act
        ActionResult result = await controller.UpdateItem(1, new UpdateItemRequest("Updated"), CancellationToken.None);

        // Assert
        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task DeleteItem_Owner_DeletesItem()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-abc")], "test"));
        var controller = CreateController(user);
        var existing = new Item { Id = 1, OwnerId = "user-abc" };

        _itemCardDataMock.Setup(d => d.GetItemByIdAsync(1)).ReturnsAsync(existing);
        _itemCardDataMock.Setup(d => d.DeleteItemAsync(1)).Returns(Task.CompletedTask);

        // Act
        ActionResult result = await controller.DeleteItem(1, CancellationToken.None);

        // Assert
        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task AddTag_Valid_AddsTag()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-abc")], "test"));
        var controller = CreateController(user);

        _itemTagMock.Setup(t => t.AddTagToItemAsync(1, 10, "user-abc", null)).ReturnsAsync((string?)null);

        // Act
        ActionResult result = await controller.AddTag(1, new AddTagRelationRequest(10), CancellationToken.None);

        // Assert
        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task RemoveTag_Valid_RemovesTag()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-abc")], "test"));
        var controller = CreateController(user);

        _itemTagMock.Setup(t => t.RemoveTagRelationAsync(5, "user-abc")).ReturnsAsync((string?)null);

        // Act
        ActionResult result = await controller.RemoveTag(1, 5, CancellationToken.None);

        // Assert
        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task ChangeItemTag_Unauthorized_ReturnsUnauthorized()
    {
        // Arrange
        var controller = CreateController(); // 未認証

        // Act
        var result = await controller.ChangeItemTag(1, 5, new ChangeItemTagRequest(20), CancellationToken.None);

        // Assert
        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task ChangeItemTag_Success_ReturnsOk()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-abc")], "test"));
        var controller = CreateController(user);
        _itemTagMock.Setup(t => t.ChangeItemTagAsync(5, 20, 1, "user-abc")).ReturnsAsync((string?)null);

        // Act
        var result = await controller.ChangeItemTag(1, 5, new ChangeItemTagRequest(20), CancellationToken.None);

        // Assert
        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task ChangeItemTag_Error_ReturnsBadRequest()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-abc")], "test"));
        var controller = CreateController(user);
        _itemTagMock.Setup(t => t.ChangeItemTagAsync(5, 20, 1, "user-abc")).ReturnsAsync("エラーが発生しました");

        // Act
        var result = await controller.ChangeItemTag(1, 5, new ChangeItemTagRequest(20), CancellationToken.None);

        // Assert
        var badResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("エラーが発生しました", badResult.Value);
    }

    [Fact]
    public async Task GetUserGroups_Authorized_ReturnsGroups()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-abc")], "test"));
        var controller = CreateController(user);
        var group = new UserGroup { Id = 1, Name = "Test Group", OwnerId = "user-abc" };

        _userGroupMock.Setup(g => g.GetUserGroupsForUserAsync("user-abc", It.IsAny<CancellationToken>())).ReturnsAsync([group]);

        // Act
        ActionResult<IReadOnlyList<UserGroupDto>> result = await controller.GetUserGroups(CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dtos = Assert.IsAssignableFrom<IReadOnlyList<UserGroupDto>>(okResult.Value);
        Assert.Single(dtos);
        Assert.Equal("Test Group", dtos[0].Name);
    }

    [Fact]
    public async Task GetItemById_NotFound_ReturnsNotFound()
    {
        // Arrange
        var controller = CreateController();
        _itemCardDataMock.Setup(m => m.GetItemByIdAsync(999)).ReturnsAsync((Item?)null);

        // Act
        var result = await controller.GetItemById(999, CancellationToken.None);

        // Assert
        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetItemById_PrivateOtherUser_ReturnsForbid()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-abc")], "test"));
        var controller = CreateController(user);
        var item = new Item { Id = 1, Content = "Secret", IsPrivate = true, OwnerId = "other-user", ItemKindJson = "{}" };
        _itemCardDataMock.Setup(m => m.GetItemByIdAsync(1)).ReturnsAsync(item);

        // Act
        var result = await controller.GetItemById(1, CancellationToken.None);

        // Assert
        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task GetItemById_Existing_ReturnsItemDto()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-abc")], "test"));
        var controller = CreateController(user);
        var item = new Item
        {
            Id = 1,
            Content = "Hello",
            IsPrivate = false,
            OwnerId = "user-abc",
            ItemKindJson = "{}",
            TagRelations = [new TagRelation { TagId = 10, Weight = 2, OwnerId = "user-abc", Tag = new Tag { Id = 10, Name = "C#", OwnerId = "user-abc" } }]
        };
        _itemCardDataMock.Setup(m => m.GetItemByIdAsync(1)).ReturnsAsync(item);
        _itemReplyMock.Setup(m => m.GetItemReplyCountAsync(1)).ReturnsAsync(3);
        _itemQuoteMock.Setup(m => m.GetQuoteCountAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(2);

        // Act
        var result = await controller.GetItemById(1, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<ItemDto>(okResult.Value);
        Assert.Equal(1, dto.Id);
        Assert.Equal("Hello", dto.Content);
        Assert.Equal(3, dto.ReplyCount);
        Assert.Equal(2, dto.QuoteCount);
        Assert.Single(dto.TagRelations);
    }

    [Fact]
    public async Task UpdateTagWeight_Unauthorized_ReturnsUnauthorized()
    {
        // Arrange
        var controller = CreateController(); // 未認証

        // Act
        var result = await controller.UpdateTagWeight(1, 10, new UpdateTagWeightRequest(1), CancellationToken.None);

        // Assert
        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task UpdateTagWeight_Success_ReturnsOk()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-abc")], "test"));
        var controller = CreateController(user);
        _itemTagMock.Setup(m => m.UpdateTagWeightAsync(10, 1, "user-abc")).ReturnsAsync(UpdateWeightResult.Success);

        // Act
        var result = await controller.UpdateTagWeight(1, 10, new UpdateTagWeightRequest(1), CancellationToken.None);

        // Assert
        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task UpdateTagWeight_NotFound_ReturnsNotFound()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-abc")], "test"));
        var controller = CreateController(user);
        _itemTagMock.Setup(m => m.UpdateTagWeightAsync(10, 1, "user-abc")).ReturnsAsync(UpdateWeightResult.NotFound);

        // Act
        var result = await controller.UpdateTagWeight(1, 10, new UpdateTagWeightRequest(1), CancellationToken.None);

        // Assert
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task UpdateTagWeight_NoPermission_ReturnsForbid()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-abc")], "test"));
        var controller = CreateController(user);
        _itemTagMock.Setup(m => m.UpdateTagWeightAsync(10, 1, "user-abc")).ReturnsAsync(UpdateWeightResult.NoPermission);

        // Act
        var result = await controller.UpdateTagWeight(1, 10, new UpdateTagWeightRequest(1), CancellationToken.None);

        // Assert
        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task GetQuotedByItems_ReturnsQuotedItems()
    {
        // Arrange
        var controller = CreateController();
        var item = new Item
        {
            Id = 101,
            Content = "Quoting item",
            OwnerId = "user-abc",
            ItemKindJson = "{}",
            QuotedItemId = 1
        };
        _itemQuoteMock.Setup(m => m.GetQuotedByItemsAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync([item]);

        // Act
        var result = await controller.GetQuotedByItems(1, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dtos = Assert.IsAssignableFrom<IReadOnlyList<ItemDto>>(okResult.Value);
        Assert.Single(dtos);
        Assert.Equal(101, dtos[0].Id);
    }

    [Fact]
    public async Task ImportCsv_Unauthorized_ReturnsUnauthorized()
    {
        // Arrange
        var controller = CreateController(); // 未認証

        // Act
        var result = await controller.ImportCsv(new ImportCsvRequest("content,tag"), CancellationToken.None);

        // Assert
        Assert.IsType<UnauthorizedObjectResult>(result.Result);
    }

    [Fact]
    public async Task ImportCsv_Valid_ReturnsImportedCount()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-abc")], "test"));
        var controller = CreateController(user);
        _adminDataMock.Setup(m => m.ImportItemsWithTagsAsync("user-abc", It.IsAny<IReadOnlyList<string[]>>()))
            .ReturnsAsync(2);

        // Act
        var result = await controller.ImportCsv(new ImportCsvRequest("item1,tag1\nitem2,tag2"), CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var res = Assert.IsType<ImportCsvResponse>(okResult.Value);
        Assert.Equal(2, res.ImportedCount);
    }

    [Fact]
    public async Task GetTaggingRequests_ReturnsMappedDtos()
    {
        // Arrange
        var controller = CreateController();
        var requests = new List<TaggingRequestEntity>
        {
            new()
            {
                Id = 10,
                OwnerId = "user-1",
                TargetItemId = 1,
                RequestedTagId = 2,
                RequestedTag = new Tag { Name = "test-tag", OwnerId = "user-2" },
                RequesterUserId = "user-1",
                TagOwnerUserId = "user-2",
                ProposedWeight = 1,
                RequestType = TaggingRequestType.Add
            }
        };
        _itemTagMock.Setup(m => m.GetTaggingRequestsForItemAsync(1))
            .ReturnsAsync(requests);

        // Act
        var result = await controller.GetTaggingRequests(1, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dtos = Assert.IsAssignableFrom<IReadOnlyList<TaggingRequestDto>>(okResult.Value);
        Assert.Single(dtos);
        Assert.Equal(10, dtos[0].Id);
        Assert.Equal("test-tag", dtos[0].RequestedTagName);
    }

    [Fact]
    public async Task ApproveTaggingRequest_Unauthorized_ReturnsUnauthorized()
    {
        // Arrange
        var controller = CreateController(); // 未認証

        // Act
        var result = await controller.ApproveTaggingRequest(10, CancellationToken.None);

        // Assert
        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task ApproveTaggingRequest_Authorized_CallsService_ReturnsOk()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-abc")], "test"));
        var controller = CreateController(user);
        _taggingRequestActionsMock.Setup(m => m.ApproveAsync(10, "user-abc"))
            .ReturnsAsync(true);

        // Act
        var result = await controller.ApproveTaggingRequest(10, CancellationToken.None);

        // Assert
        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task RejectTaggingRequest_Unauthorized_ReturnsUnauthorized()
    {
        // Arrange
        var controller = CreateController(); // 未認証

        // Act
        var result = await controller.RejectTaggingRequest(10, new RejectTaggingRequest("Not suitable"), CancellationToken.None);

        // Assert
        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task RejectTaggingRequest_Authorized_CallsService_ReturnsOk()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-abc")], "test"));
        var controller = CreateController(user);
        _tagRequestRejectionMock.Setup(m => m.RejectRequestAsync(10, "user-abc", "Not suitable"))
            .Returns(Task.CompletedTask);

        // Act
        var result = await controller.RejectTaggingRequest(10, new RejectTaggingRequest("Not suitable"), CancellationToken.None);

        // Assert
        Assert.IsType<OkResult>(result);
    }
}