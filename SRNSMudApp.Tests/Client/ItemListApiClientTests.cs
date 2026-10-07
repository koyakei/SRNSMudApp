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
}