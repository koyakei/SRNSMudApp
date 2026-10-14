namespace SRNSMudApp.Tests.Client;

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using SRNSMudApp.Client.Models;
using SRNSMudApp.Client.Models.Api;
using SRNSMudApp.Client.Services;

using Xunit;

public class ItemListApiClientTests
{
    private sealed class MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(handler(request));
        }
    }

    [Fact]
    public async Task SearchTagsAsync_ReturnsSuggestions()
    {
        // Arrange
        var suggestions = new List<TagSuggestion>
        {
            new(1, "test-tag", "alice")
        };

        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            Assert.Contains("api/item/search-tags?q=test", req.RequestUri!.ToString());

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(suggestions), System.Text.Encoding.UTF8, "application/json")
            };
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        IReadOnlyList<TagSuggestion> result = await apiClient.SearchTagsAsync("test");

        // Assert
        Assert.Single(result);
        Assert.Equal("test-tag", result[0].TagName);
        Assert.Equal("alice", result[0].UserName);
    }

    [Fact]
    public async Task QueryItemsAsync_PostsRequest_AndReturnsResponse()
    {
        // Arrange
        var expectedResponse = new ItemListQueryResponse(
            Items:
            [
                new ItemDto(
                    Id: 1,
                    Content: "Test content",
                    OwnerUserName: "bob",
                    UpdatedDate: DateTimeOffset.UtcNow,
                    IsPrivate: false,
                    TagRelations: [],
                    ItemKindJson: "{}"
                )
            ],
            Tags:
            [
                new TagDto(1, "tag1", "desc", "bob")
            ],
            TotalCount: 1
        );

        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal("https://example.com/api/item/query", req.RequestUri!.ToString());

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(expectedResponse), System.Text.Encoding.UTF8, "application/json")
            };
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        var request = new ItemListQueryRequest(
            Filters: [new ItemListFilterDto(TagId: 1)],
            Sorts: [new ItemListSortDto(1, true)],
            Skip: 0,
            Take: 10
        );

        // Act
        ItemListQueryResponse result = await apiClient.QueryItemsAsync(request);

        // Assert
        Assert.Equal(1, result.TotalCount);
        Assert.Single(result.Items);
        Assert.Equal("Test content", result.Items[0].Content);
        Assert.Single(result.Tags);
        Assert.Equal("tag1", result.Tags[0].Name);
    }

    [Fact]
    public async Task QueryItemsAsync_DeserializesExtendedProperties()
    {
        // Arrange
        var expectedResponse = new ItemListQueryResponse(
            Items:
            [
                new ItemDto(
                    Id: 10,
                    Content: "Item with rich data",
                    OwnerUserName: "alice",
                    UpdatedDate: DateTimeOffset.UtcNow,
                    IsPrivate: true,
                    TagRelations: [new TagRelationDto(1, "tag1", 5, "alice")],
                    ItemKindJson: "{}",
                    ParentItemId: null,
                    RootItemId: null,
                    QuotedItemId: 5,
                    OwnerUserId: "user-alice",
                    Score: 12,
                    ShinjiScore: 3,
                    ZenScore: 4,
                    BiScore: 5,
                    ReplyCount: 7,
                    QuoteCount: 2,
                    QuotedItemContent: "Quoted text",
                    QuotedItemOwnerUserName: "bob",
                    TargetUserGroupName: "TeamA"
                )
            ],
            Tags:
            [
                new TagDto(
                    Id: 1,
                    Name: "tag1",
                    Content: "Tag desc",
                    OwnerUserName: "alice",
                    OwnerUserId: "user-alice",
                    Score: 8,
                    IsPrivate: false,
                    TargetUserGroupName: "TeamA",
                    TargetTagRelations: [new TagRelationDto(2, "tag2", 3, "bob")]
                )
            ],
            TotalCount: 1
        );

        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(expectedResponse), System.Text.Encoding.UTF8, "application/json")
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        ItemListQueryResponse result = await apiClient.QueryItemsAsync(new ItemListQueryRequest());

        // Assert
        ItemDto item = Assert.Single(result.Items);
        Assert.Equal(12, item.Score);
        Assert.Equal(3, item.ShinjiScore);
        Assert.Equal(4, item.ZenScore);
        Assert.Equal(5, item.BiScore);
        Assert.Equal(7, item.ReplyCount);
        Assert.Equal(2, item.QuoteCount);
        Assert.Equal("user-alice", item.OwnerUserId);
        Assert.Equal("Quoted text", item.QuotedItemContent);
        Assert.Equal("bob", item.QuotedItemOwnerUserName);
        Assert.Equal("TeamA", item.TargetUserGroupName);

        TagDto tag = Assert.Single(result.Tags);
        Assert.Equal(8, tag.Score);
        Assert.Equal("user-alice", tag.OwnerUserId);
        Assert.NotNull(tag.TargetTagRelations);
        Assert.Single(tag.TargetTagRelations);
        Assert.Equal("tag2", tag.TargetTagRelations[0].TagName);
    }

    [Fact]
    public async Task ExportItemsAsync_PostsItemIds_AndReturnsJsonString()
    {
        // Arrange
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal("https://example.com/api/item/export", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[{\"id\": 1}]", System.Text.Encoding.UTF8, "application/json")
            };
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        string result = await apiClient.ExportItemsAsync([1, 2]);

        // Assert
        Assert.Equal("[{\"id\": 1}]", result);
    }

    [Fact]
    public async Task VoteItemAsync_PostsVote_AndReturnsSuccess()
    {
        // Arrange
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal("https://example.com/api/item/10/vote", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        var result = await apiClient.VoteItemAsync(10, true);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task ReactItemAsync_PostsReaction_AndReturnsSuccess()
    {
        // Arrange
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal("https://example.com/api/item/10/reaction", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        var result = await apiClient.ReactItemAsync(10, "真実");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task CreateItemAsync_PostsCreate_AndReturnsCreatedItem()
    {
        // Arrange
        var created = new ItemDto(100, "Created item", "author", DateTimeOffset.UtcNow, false, [], "{}");
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal("https://example.com/api/item/create", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(created), System.Text.Encoding.UTF8, "application/json")
            };
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        ItemDto? result = await apiClient.CreateItemAsync(new CreateItemRequest("Created item"));

        // Assert
        Assert.NotNull(result);
        Assert.Equal(100, result.Id);
        Assert.Equal("Created item", result.Content);
    }

    [Fact]
    public async Task GetRepliesAsync_ReturnsReplies()
    {
        // Arrange
        var replies = new List<ItemDto>
        {
            new(101, "Reply", "author", DateTimeOffset.UtcNow, false, [], "{}")
        };
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            Assert.Equal("https://example.com/api/item/1/replies", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(replies), System.Text.Encoding.UTF8, "application/json")
            };
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        IReadOnlyList<ItemDto> result = await apiClient.GetRepliesAsync(1);

        // Assert
        Assert.Single(result);
        Assert.Equal(101, result[0].Id);
    }

    [Fact]
    public async Task AddReplyAsync_PostsReply_ReturnsCreated()
    {
        // Arrange
        var created = new ItemDto(102, "New Reply", "author", DateTimeOffset.UtcNow, false, [], "{}");
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal("https://example.com/api/item/1/replies", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(created), System.Text.Encoding.UTF8, "application/json")
            };
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        ItemDto? result = await apiClient.AddReplyAsync(1, new CreateReplyRequest("New Reply"));

        // Assert
        Assert.NotNull(result);
        Assert.Equal(102, result.Id);
    }

    [Fact]
    public async Task GetQuotesAsync_ReturnsQuotes()
    {
        // Arrange
        var quotes = new List<ItemDto>
        {
            new(201, "Quote", "author", DateTimeOffset.UtcNow, false, [], "{}")
        };
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            Assert.Equal("https://example.com/api/item/1/quotes", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(quotes), System.Text.Encoding.UTF8, "application/json")
            };
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        IReadOnlyList<ItemDto> result = await apiClient.GetQuotesAsync(1);

        // Assert
        Assert.Single(result);
        Assert.Equal(201, result[0].Id);
    }

    [Fact]
    public async Task UpdateItemAsync_PutsUpdate_ReturnsTrue()
    {
        // Arrange
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Put, req.Method);
            Assert.Equal("https://example.com/api/item/1", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        var result = await apiClient.UpdateItemAsync(1, new UpdateItemRequest("Updated"));

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task DeleteItemAsync_Deletes_ReturnsTrue()
    {
        // Arrange
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Delete, req.Method);
            Assert.Equal("https://example.com/api/item/1", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        var result = await apiClient.DeleteItemAsync(1);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task AddTagToItemAsync_PostsTag_ReturnsTrue()
    {
        // Arrange
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal("https://example.com/api/item/1/tags", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        var result = await apiClient.AddTagToItemAsync(1, new AddTagRelationRequest(10));

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task RemoveTagFromItemAsync_DeletesTag_ReturnsTrue()
    {
        // Arrange
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Delete, req.Method);
            Assert.Equal("https://example.com/api/item/1/tags/5", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        var result = await apiClient.RemoveTagFromItemAsync(1, 5);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task GetUserGroupsAsync_ReturnsGroups()
    {
        // Arrange
        var groups = new List<UserGroupDto>
        {
            new(1, "Group 1", "Desc")
        };
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            Assert.Equal("https://example.com/api/item/user-groups", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(groups), System.Text.Encoding.UTF8, "application/json")
            };
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        IReadOnlyList<UserGroupDto> result = await apiClient.GetUserGroupsAsync();

        // Assert
        Assert.Single(result);
        Assert.Equal("Group 1", result[0].Name);
    }

    [Fact]
    public async Task VoteTagAsync_PostsVote_ReturnsTrue()
    {
        // Arrange
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal("https://example.com/api/tag/5/vote", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        var result = await apiClient.VoteTagAsync(5, true);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task CreateTagAsync_PostsCreate_ReturnsCreated()
    {
        // Arrange
        var created = new TagDto(10, "NewTag", "Desc", "user-1");
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal("https://example.com/api/tag/create", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(created), System.Text.Encoding.UTF8, "application/json")
            };
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        TagDto? result = await apiClient.CreateTagAsync(new CreateTagRequest("NewTag", "Desc"));

        // Assert
        Assert.NotNull(result);
        Assert.Equal(10, result.Id);
        Assert.Equal("NewTag", result.Name);
    }

    [Fact]
    public async Task DeleteTagAsync_DeletesTag_ReturnsTrue()
    {
        // Arrange
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Delete, req.Method);
            Assert.Equal("https://example.com/api/tag/10", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        var result = await apiClient.DeleteTagAsync(10);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task GetTagDiagramDataAsync_ReturnsDiagramResponse()
    {
        // Arrange
        var response = new TagDiagramResponse(
            Nodes: [new TagDto(1, "Tag1", null, "user-1")],
            Edges: [new TagDiagramEdgeDto(10, 1, 2, "user-1")],
            ContextItems: []
        );
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            Assert.Contains("api/tag-diagram/data?itemIds=1", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(response), System.Text.Encoding.UTF8, "application/json")
            };
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        TagDiagramResponse? result = await apiClient.GetTagDiagramDataAsync("1");

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.Nodes);
        Assert.Equal("Tag1", result.Nodes[0].Name);
    }

    [Fact]
    public async Task GetUserGroupDetailAsync_ReturnsDetail()
    {
        // Arrange
        var detail = new UserGroupDetailDto(
            Id: 5,
            Name: "DevTeam",
            Description: "Developers",
            OwnerUserId: "owner-1",
            OwnerUserName: "alice",
            Members: [new UserGroupMemberDto("user-2", "bob", DateTimeOffset.UtcNow)]
        );
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            Assert.Equal("https://example.com/api/usergroup/5", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(detail), System.Text.Encoding.UTF8, "application/json")
            };
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        UserGroupDetailDto? result = await apiClient.GetUserGroupDetailAsync(5);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(5, result.Id);
        Assert.Equal("DevTeam", result.Name);
        Assert.Single(result.Members);
        Assert.Equal("bob", result.Members[0].UserName);
    }

    [Fact]
    public async Task UpdateUserGroupAsync_PutsUpdate_ReturnsTrue()
    {
        // Arrange
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Put, req.Method);
            Assert.Equal("https://example.com/api/usergroup/5", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        bool result = await apiClient.UpdateUserGroupAsync(5, new UpdateUserGroupRequest("NewName", "NewDesc"));

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task DeleteUserGroupAsync_Deletes_ReturnsTrue()
    {
        // Arrange
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Delete, req.Method);
            Assert.Equal("https://example.com/api/usergroup/5", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        bool result = await apiClient.DeleteUserGroupAsync(5);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task AddUserGroupMemberAsync_PostsMember_ReturnsTrue()
    {
        // Arrange
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal("https://example.com/api/usergroup/5/members", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        bool result = await apiClient.AddUserGroupMemberAsync(5, new AddGroupMemberRequest("user-99"));

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task RemoveUserGroupMemberAsync_DeletesMember_ReturnsTrue()
    {
        // Arrange
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Delete, req.Method);
            Assert.Equal("https://example.com/api/usergroup/5/members/user-99", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        bool result = await apiClient.RemoveUserGroupMemberAsync(5, "user-99");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task SearchUsersAsync_WithQuery_ReturnsUsers()
    {
        // Arrange
        var users = new List<UserDto> { new("u-1", "Alice") };
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            Assert.Contains("api/user/search?q=ali", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(users), System.Text.Encoding.UTF8, "application/json")
            };
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        IReadOnlyList<UserDto> result = await apiClient.SearchUsersAsync("ali");

        // Assert
        Assert.Single(result);
        Assert.Equal("Alice", result[0].UserName);
    }

    [Fact]
    public async Task SearchUsersAsync_WithoutQuery_CallsSearchWithoutQueryParam()
    {
        // Arrange
        var users = new List<UserDto> { new("u-1", "Alice"), new("u-2", "Bob") };
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            Assert.Equal("https://example.com/api/user/search", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(users), System.Text.Encoding.UTF8, "application/json")
            };
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        IReadOnlyList<UserDto> result = await apiClient.SearchUsersAsync();

        // Assert
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GetTagTreeAsync_ReturnsTagTree()
    {
        // Arrange
        var tree = new List<TagTreeNodeDto>
        {
            new(1, "Root", "desc", null, 10, "bob", [new(2, "Child", null, 1, 5, "bob", [])])
        };
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            Assert.Equal("https://example.com/api/tag/tree", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(tree), System.Text.Encoding.UTF8, "application/json")
            };
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        IReadOnlyList<TagTreeNodeDto> result = await apiClient.GetTagTreeAsync();

        // Assert
        Assert.Single(result);
        Assert.Equal("Root", result[0].Name);
        Assert.Single(result[0].Children);
        Assert.Equal("Child", result[0].Children[0].Name);
    }

    [Fact]
    public async Task GetItemByIdAsync_ReturnsItemDto()
    {
        // Arrange
        var itemDto = new ItemDto(
            Id: 42,
            Content: "Detail Content",
            OwnerUserName: "alice",
            UpdatedDate: DateTimeOffset.UtcNow,
            IsPrivate: false,
            TagRelations: [new TagRelationDto(1, "Tag1", 1, "alice")],
            ItemKindJson: "{}"
        );
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            Assert.Equal("https://example.com/api/item/42", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(itemDto), System.Text.Encoding.UTF8, "application/json")
            };
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        ItemDto? result = await apiClient.GetItemByIdAsync(42);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(42, result.Id);
        Assert.Equal("Detail Content", result.Content);
        Assert.Single(result.TagRelations);
    }

    [Fact]
    public async Task UpdateTagWeightAsync_SendsPutRequest_ReturnsTrue()
    {
        // Arrange
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Put, req.Method);
            Assert.Equal("https://example.com/api/item/10/tags/5/weight", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        bool result = await apiClient.UpdateTagWeightAsync(10, 5, 1);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task GetQuotedByItemsAsync_ReturnsItems()
    {
        // Arrange
        var items = new List<ItemDto>
        {
            new(10, "Quote Item", "user", DateTimeOffset.UtcNow, false, [], "{}")
        };
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            Assert.Equal("https://example.com/api/item/5/quoted-by", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(items), System.Text.Encoding.UTF8, "application/json")
            };
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        IReadOnlyList<ItemDto> result = await apiClient.GetQuotedByItemsAsync(5);

        // Assert
        Assert.Single(result);
        Assert.Equal(10, result[0].Id);
    }

    [Fact]
    public async Task ImportCsvAsync_PostsCsvContent_ReturnsResponse()
    {
        // Arrange
        var responseDto = new ImportCsvResponse(3, "3件インポートしました");
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal("https://example.com/api/item/import", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(responseDto), System.Text.Encoding.UTF8, "application/json")
            };
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        ImportCsvResponse? result = await apiClient.ImportCsvAsync(new ImportCsvRequest("item,tag"));

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.ImportedCount);
    }

    [Fact]
    public async Task GetManagedUserGroupsAsync_ReturnsGroups()
    {
        // Arrange
        var groups = new List<UserGroupDto> { new(1, "Managed Group", "Desc") };
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            Assert.Equal("https://example.com/api/usergroup/managed", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(groups), System.Text.Encoding.UTF8, "application/json")
            };
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        IReadOnlyList<UserGroupDto> result = await apiClient.GetManagedUserGroupsAsync();

        // Assert
        Assert.Single(result);
        Assert.Equal("Managed Group", result[0].Name);
    }

    [Fact]
    public async Task ChangeItemTagAsync_SendsPutRequest_ReturnsTrue()
    {
        // Arrange
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Put, req.Method);
            Assert.Equal("https://example.com/api/item/1/tags/5/change", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        bool result = await apiClient.ChangeItemTagAsync(1, 5, new ChangeItemTagRequest(20));

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task GetTaggingRequestsAsync_ReturnsRequests()
    {
        // Arrange
        var requests = new List<TaggingRequestDto>
        {
            new(10, 1, 2, "test-tag", "u1", "u1name", "u2", 1, "Pending", "AddTag")
        };
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            Assert.Equal("https://example.com/api/item/1/tag-requests", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(requests), System.Text.Encoding.UTF8, "application/json")
            };
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        IReadOnlyList<TaggingRequestDto> result = await apiClient.GetTaggingRequestsAsync(1);

        // Assert
        Assert.Single(result);
        Assert.Equal(10, result[0].Id);
        Assert.Equal("test-tag", result[0].RequestedTagName);
    }

    [Fact]
    public async Task ApproveTaggingRequestAsync_SendsPostRequest_ReturnsTrue()
    {
        // Arrange
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal("https://example.com/api/item/tag-requests/10/approve", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        bool result = await apiClient.ApproveTaggingRequestAsync(10);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task RejectTaggingRequestAsync_SendsPostRequest_ReturnsTrue()
    {
        // Arrange
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal("https://example.com/api/item/tag-requests/10/reject", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        bool result = await apiClient.RejectTaggingRequestAsync(10, new RejectTaggingRequest("Not suitable"));

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task GetTagDetailAsync_ReturnsTagDetail()
    {
        // Arrange
        var detail = new TagDetailDto(
            Id: 5,
            Name: "TestTag",
            Content: "Description",
            OwnerUserId: "user-1",
            OwnerUserName: "alice",
            Score: 12,
            IsPrivate: false,
            TargetUserGroupName: null,
            IsFollowing: true,
            AutoAcceptIncomingTaggingRequests: false,
            IsLocked: false,
            RelatedItems: [],
            RelatedTags: [],
            PendingRequests: []
        );

        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            Assert.Equal("https://example.com/api/tag/5", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(detail), System.Text.Encoding.UTF8, "application/json")
            };
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        TagDetailDto? result = await apiClient.GetTagDetailAsync(5);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(5, result.Id);
        Assert.Equal("TestTag", result.Name);
        Assert.True(result.IsFollowing);
    }

    [Fact]
    public async Task ToggleTagFollowAsync_ReturnsNewStatus()
    {
        // Arrange
        var response = new ToggleFollowResponse(true);
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal("https://example.com/api/tag/5/follow", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(response), System.Text.Encoding.UTF8, "application/json")
            };
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        bool result = await apiClient.ToggleTagFollowAsync(5);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task GetNotificationsAsync_ReturnsNotifications()
    {
        // Arrange
        var notifications = new List<NotificationItemDto>
        {
            new(1, "ItemReply", "リプライが届きました", DateTimeOffset.UtcNow, false, "alice", 10, null, null)
        };
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            Assert.Equal("https://example.com/api/notification", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(notifications), System.Text.Encoding.UTF8, "application/json")
            };
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        IReadOnlyList<NotificationItemDto> result = await apiClient.GetNotificationsAsync();

        // Assert
        Assert.Single(result);
        Assert.Equal(1, result[0].SourceId);
        Assert.Equal("ItemReply", result[0].SourceType);
    }

    [Fact]
    public async Task GetUnreadNotificationCountAsync_ReturnsCount()
    {
        // Arrange
        var response = new UnreadCountResponse(5);
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            Assert.Equal("https://example.com/api/notification/unread-count", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(response), System.Text.Encoding.UTF8, "application/json")
            };
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        int result = await apiClient.GetUnreadNotificationCountAsync();

        // Assert
        Assert.Equal(5, result);
    }

    [Fact]
    public async Task MarkNotificationAsReadAsync_SendsPostRequest_ReturnsTrue()
    {
        // Arrange
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal("https://example.com/api/notification/1/read", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        bool result = await apiClient.MarkNotificationAsReadAsync(1, "ItemReply");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task MarkAllNotificationsAsReadAsync_SendsPostRequest_ReturnsTrue()
    {
        // Arrange
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal("https://example.com/api/notification/read-all", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        bool result = await apiClient.MarkAllNotificationsAsReadAsync();

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task GetTagListAsync_ReturnsTags()
    {
        // Arrange
        var tags = new List<TagDto>
        {
            new(1, "TestTag", "Description", "alice", "alice-id", 10, false, null)
        };
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            Assert.Equal("https://example.com/api/tag/list?q=test", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(tags), System.Text.Encoding.UTF8, "application/json")
            };
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        IReadOnlyList<TagDto> result = await apiClient.GetTagListAsync("test");

        // Assert
        Assert.Single(result);
        Assert.Equal("TestTag", result[0].Name);
    }

    [Fact]
    public async Task GetUserDetailAsync_ReturnsUserDetail()
    {
        // Arrange
        var userDetail = new UserDetailDto("u1", "alice", false, true, 5, 10, [], []);
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            Assert.Equal("https://example.com/api/user/u1", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(userDetail), System.Text.Encoding.UTF8, "application/json")
            };
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        UserDetailDto? result = await apiClient.GetUserDetailAsync("u1");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("u1", result.UserId);
        Assert.Equal("alice", result.UserName);
        Assert.True(result.IsFollowing);
    }

    [Fact]
    public async Task ToggleUserFollowAsync_ReturnsNewStatus()
    {
        // Arrange
        var response = new ToggleFollowResponse(true);
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal("https://example.com/api/user/u1/follow", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(response), System.Text.Encoding.UTF8, "application/json")
            };
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        bool result = await apiClient.ToggleUserFollowAsync("u1");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task GetBountiesAsync_ReturnsBountyBoardResponse()
    {
        // Arrange
        var response = new BountyBoardResponse(
            Bounties: [new BountyDto(1, "u1", "alice", 10, "tag1", 100, "content", "rewardTag", 5, DateTimeOffset.UtcNow, false, true)],
            TotalCount: 1
        );
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            Assert.Equal("https://example.com/api/bounty", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(response), System.Text.Encoding.UTF8, "application/json")
            };
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        BountyBoardResponse? result = await apiClient.GetBountiesAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.Bounties);
        Assert.Equal("tag1", result.Bounties[0].RequestedTagName);
    }

    [Fact]
    public async Task CreateBountyAsync_ReturnsTrueOnSuccess()
    {
        // Arrange
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal("https://example.com/api/bounty", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        bool result = await apiClient.CreateBountyAsync(new CreateBountyRequest(100, 10));

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task FulfillBountyAsync_ReturnsTrueOnSuccess()
    {
        // Arrange
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal("https://example.com/api/bounty/1/fulfill", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        bool result = await apiClient.FulfillBountyAsync(1, new FulfillBountyRequest());

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task GetPublicOffersAsync_ReturnsOffers()
    {
        // Arrange
        var offers = new List<PublicOfferDto>
        {
            new(1, 10, "Tag1", 0, "u1", "alice", DateTimeOffset.UtcNow, false)
        };
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            Assert.Equal("https://example.com/api/public-offer", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(offers), System.Text.Encoding.UTF8, "application/json")
            };
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        IReadOnlyList<PublicOfferDto> result = await apiClient.GetPublicOffersAsync();

        // Assert
        Assert.Single(result);
        Assert.Equal("Tag1", result[0].OfferedTagName);
    }

    [Fact]
    public async Task CreatePublicOfferAsync_ReturnsTrueOnSuccess()
    {
        // Arrange
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal("https://example.com/api/public-offer", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        bool result = await apiClient.CreatePublicOfferAsync(new CreatePublicOfferRequest(10, 0));

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task DeactivatePublicOfferAsync_ReturnsTrueOnSuccess()
    {
        // Arrange
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Delete, req.Method);
            Assert.Equal("https://example.com/api/public-offer/5", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        bool result = await apiClient.DeactivatePublicOfferAsync(5);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task TriggerPublicOfferAsync_ReturnsTrueOnSuccess()
    {
        // Arrange
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal("https://example.com/api/public-offer/5/trigger", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        bool result = await apiClient.TriggerPublicOfferAsync(5, new TriggerPublicOfferRequest(100));

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task GetContractsAsync_ReturnsContractPageResponse()
    {
        // Arrange
        var response = new ContractPageResponse(
            IncomingContracts: [new ContractDto(1, "Gratis", "Proposed", 10, "Tag1", 100, "content", "u1", "alice", "u2", "bob", DateTimeOffset.UtcNow, false)],
            OutgoingContracts: []
        );
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            Assert.Equal("https://example.com/api/contract", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(response), System.Text.Encoding.UTF8, "application/json")
            };
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        ContractPageResponse? result = await apiClient.GetContractsAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.IncomingContracts);
        Assert.Equal("Tag1", result.IncomingContracts[0].RequestedTagName);
    }

    [Fact]
    public async Task AcceptContractAsync_ReturnsTrueOnSuccess()
    {
        // Arrange
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal("https://example.com/api/contract/1/accept", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        bool result = await apiClient.AcceptContractAsync(1);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task RejectContractAsync_ReturnsTrueOnSuccess()
    {
        // Arrange
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal("https://example.com/api/contract/1/reject", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        bool result = await apiClient.RejectContractAsync(1);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task CancelContractAsync_ReturnsTrueOnSuccess()
    {
        // Arrange
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal("https://example.com/api/contract/1/cancel", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        bool result = await apiClient.CancelContractAsync(1);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task GetTopTagsWithRightAssetsAsync_ReturnsSummaries()
    {
        // Arrange
        var summaries = new List<TagRightAssetSummaryDto>
        {
            new(1, "Tag1", "Desc1", 100, 2),
            new(2, "Tag2", null, 50, 1)
        };
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            Assert.Equal("https://example.com/api/right-asset/top-tags?count=5", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(summaries), System.Text.Encoding.UTF8, "application/json")
            };
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        IReadOnlyList<TagRightAssetSummaryDto> result = await apiClient.GetTopTagsWithRightAssetsAsync(5);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal("Tag1", result[0].TagName);
        Assert.Equal(100, result[0].TotalAmount);
    }

    [Fact]
    public async Task GetRightAssetOverviewAsync_ReturnsOverview()
    {
        // Arrange
        var overview = new RightAssetOverviewResponse(
            TagId: 10,
            TagName: "TagAlpha",
            TagContent: "Alpha Content",
            TagWeight: 5,
            IsSystem: false,
            TotalHoldersCount: 1,
            TotalActiveAmount: 50,
            TotalActiveAssetsCount: 2,
            TotalBurnedAmount: 0,
            Holders: [new RightAssetHolderDto("u1", "user1", 50, 2, 0, 0, null)],
            Assets: [new RightAssetItemDto(1, "u1", "user1", 50, false, "有効", DateTime.UtcNow, DateTime.UtcNow)]
        );

        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            Assert.Equal("https://example.com/api/right-asset/overview/10", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(overview), System.Text.Encoding.UTF8, "application/json")
            };
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        RightAssetOverviewResponse? result = await apiClient.GetRightAssetOverviewAsync(10);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(10, result.TagId);
        Assert.Equal("TagAlpha", result.TagName);
        Assert.Equal(50, result.TotalActiveAmount);
        Assert.Single(result.Holders);
        Assert.Single(result.Assets);
    }

    [Fact]
    public async Task GetRightAssetOverviewAsync_WhenNotFound_ReturnsNull()
    {
        // Arrange
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            Assert.Equal("https://example.com/api/right-asset/overview/999", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        RightAssetOverviewResponse? result = await apiClient.GetRightAssetOverviewAsync(999);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task CreateReportAsync_ReturnsReportDtoOnSuccess()
    {
        // Arrange
        var request = new CreateReportRequest("Item", 1, null, "Spam", "Detail");
        var reportDto = new ContentReportDto(
            Id: 10,
            TargetType: "Item",
            ItemId: 1,
            TagId: null,
            TargetContentSnapshot: "Snapshot",
            Reason: "Spam",
            Detail: "Detail",
            Status: "Pending",
            ResolutionNote: null,
            ReporterUserId: "user1",
            ReporterUserName: "UserOne",
            HandledByAdminId: null,
            HandledByAdminName: null,
            CreatedDate: DateTime.UtcNow,
            HandledDate: null
        );

        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal("https://example.com/api/report", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(reportDto), System.Text.Encoding.UTF8, "application/json")
            };
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        ContentReportDto? result = await apiClient.CreateReportAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(10, result.Id);
        Assert.Equal("Spam", result.Reason);
    }

    [Fact]
    public async Task GetReportsAsync_SendsFilterParams_ReturnsList()
    {
        // Arrange
        var reports = new List<ContentReportDto>
        {
            new(1, "Item", 5, null, "Snap", "Abuse", "", "Pending", null, "u1", "User1", null, null, DateTime.UtcNow, null)
        };

        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            Assert.Contains("status=Pending", req.RequestUri!.ToString());
            Assert.Contains("targetType=Item", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(reports), System.Text.Encoding.UTF8, "application/json")
            };
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        IReadOnlyList<ContentReportDto> result = await apiClient.GetReportsAsync("Pending", "Item");

        // Assert
        Assert.Single(result);
        Assert.Equal(1, result[0].Id);
    }

    [Fact]
    public async Task UpdateReportStatusAsync_ReturnsTrueOnSuccess()
    {
        // Arrange
        var request = new UpdateReportStatusRequest("Reviewed", "Note");
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Put, req.Method);
            Assert.Equal("https://example.com/api/report/1/status", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        bool result = await apiClient.UpdateReportStatusAsync(1, request);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task ResolveReportAsync_ReturnsTrueOnSuccess()
    {
        // Arrange
        var request = new ResolveReportRequest(DeleteTarget: false, HideTarget: true, ResolutionNote: "Hidden");
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal("https://example.com/api/report/1/resolve", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        bool result = await apiClient.ResolveReportAsync(1, request);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task GetManagementUsersAsync_ReturnsUserList()
    {
        // Arrange
        var users = new List<ManagedUserDto>
        {
            new("u1", "alice", true, false, true),
            new("u2", "bob", false, true, false)
        };

        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            Assert.Equal("https://example.com/api/user/management", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(users), System.Text.Encoding.UTF8, "application/json")
            };
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        IReadOnlyList<ManagedUserDto> result = await apiClient.GetManagementUsersAsync();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal("alice", result[0].UserName);
        Assert.True(result[0].IsAdmin);
        Assert.True(result[1].IsBanned);
    }

    [Fact]
    public async Task SetUserAdminAsync_ReturnsTrueOnSuccess()
    {
        // Arrange
        var request = new SetUserAdminRequest(true);
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Put, req.Method);
            Assert.Equal("https://example.com/api/user/u1/admin", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        bool result = await apiClient.SetUserAdminAsync("u1", request);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task SetUserBanAsync_ReturnsTrueOnSuccess()
    {
        // Arrange
        var request = new SetUserBanRequest(true);
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Put, req.Method);
            Assert.Equal("https://example.com/api/user/u2/ban", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        bool result = await apiClient.SetUserBanAsync("u2", request);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task GetInvitationsAsync_ReturnsList()
    {
        // Arrange
        var invitations = new List<InvitationDto>
        {
            new(1, "test@example.com", "codeA", DateTime.UtcNow.AddDays(7), false, "Pending", "admin1", DateTime.UtcNow)
        };

        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            Assert.Equal("https://example.com/api/admin/invitation", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(invitations), System.Text.Encoding.UTF8, "application/json")
            };
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        IReadOnlyList<InvitationDto> result = await apiClient.GetInvitationsAsync();

        // Assert
        Assert.Single(result);
        Assert.Equal("test@example.com", result[0].Email);
        Assert.Equal("codeA", result[0].InvitationCode);
    }

    [Fact]
    public async Task CreateInvitationAsync_ReturnsInvitationOnSuccess()
    {
        // Arrange
        var request = new CreateInvitationRequest("new@example.com");
        var created = new InvitationDto(2, "new@example.com", "codeB", DateTime.UtcNow.AddDays(7), false, "Pending", "admin1", DateTime.UtcNow);

        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal("https://example.com/api/admin/invitation", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(created), System.Text.Encoding.UTF8, "application/json")
            };
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        InvitationDto? result = await apiClient.CreateInvitationAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Id);
        Assert.Equal("codeB", result.InvitationCode);
    }

    [Fact]
    public async Task DeleteInvitationAsync_ReturnsTrueOnSuccess()
    {
        // Arrange
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Delete, req.Method);
            Assert.Equal("https://example.com/api/admin/invitation/2", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var apiClient = new ItemListApiClient(client);

        // Act
        bool result = await apiClient.DeleteInvitationAsync(2);

        // Assert
        Assert.True(result);
    }
}