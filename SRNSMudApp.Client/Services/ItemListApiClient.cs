namespace SRNSMudApp.Client.Services;

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using SRNSMudApp.Client.Models;
using SRNSMudApp.Client.Models.Api;

/// <summary>
/// ItemList 向け Web API クライアントインターフェース。
/// </summary>
public interface IItemListApiClient
{
    /// <summary>
    /// タグ名のサジェスト候補を検索します。
    /// </summary>
    Task<IReadOnlyList<TagSuggestion>> SearchTagsAsync(string query, CancellationToken cancellationToken = default);

    /// <summary>
    /// 特定タグに関連付けられたユーザー名一覧を検索します。
    /// </summary>
    Task<IReadOnlyList<string>> SearchTagUsersAsync(string tagName, string? userSearch = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// アイテム・タグ一覧および総件数をクエリ条件に基づき取得します。
    /// </summary>
    Task<ItemListQueryResponse> QueryItemsAsync(ItemListQueryRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// <see cref="IItemListApiClient"/> の HTTP 実装クラス。
/// </summary>
public sealed class ItemListApiClient(HttpClient httpClient) : IItemListApiClient
{
    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

    public async Task<IReadOnlyList<TagSuggestion>> SearchTagsAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var url = $"api/item/search-tags?q={Uri.EscapeDataString(query)}";
        List<TagSuggestion>? result = await _httpClient.GetFromJsonAsync<List<TagSuggestion>>(url, cancellationToken);
        return result ?? [];
    }

    public async Task<IReadOnlyList<string>> SearchTagUsersAsync(string tagName, string? userSearch = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tagName))
        {
            return [];
        }

        var url = $"api/item/tag-users?tagName={Uri.EscapeDataString(tagName)}";
        if (!string.IsNullOrWhiteSpace(userSearch))
        {
            url += $"&userSearch={Uri.EscapeDataString(userSearch)}";
        }

        List<string>? result = await _httpClient.GetFromJsonAsync<List<string>>(url, cancellationToken);
        return result ?? [];
    }

    public async Task<ItemListQueryResponse> QueryItemsAsync(ItemListQueryRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync("api/item/query", request, cancellationToken);
        _ = response.EnsureSuccessStatusCode();

        ItemListQueryResponse? result = await response.Content.ReadFromJsonAsync<ItemListQueryResponse>(cancellationToken);
        return result ?? new ItemListQueryResponse([], [], 0);
    }
}