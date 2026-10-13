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
}