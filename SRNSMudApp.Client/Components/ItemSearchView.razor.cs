using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Components;

using MudBlazor;

using SRNSMudApp.Client.Models;
using SRNSMudApp.Client.Models.Api;
using SRNSMudApp.Client.Services;

namespace SRNSMudApp.Client.Components;

/// <summary>
/// WebAssembly (WASM) 上で動作するアイテム・タグの検索および一覧表示コンポーネント。
/// <see cref="IItemListApiClient"/> を通じてサーバー API を非同期に呼び出します。
/// </summary>
public sealed partial class ItemSearchView : ComponentBase
{
    [Inject]
    private IItemListApiClient ApiClient { get; set; } = null!;

    /// <summary>
    /// アイテムクリック時に通知されるコールバック。
    /// </summary>
    [Parameter]
    public EventCallback<int> OnItemClick { get; set; }

    private MudAutocomplete<TagSuggestion>? _autocomplete;
    private string _searchText = string.Empty;
    private readonly List<TagSuggestion> _selectedFilters = [];
    private bool _isLoading;
    private string? _errorMessage;

    private IReadOnlyList<ItemDto> _items = [];
    private IReadOnlyList<TagDto> _tags = [];
    private int _totalCount;

    protected override async Task OnInitializedAsync()
    {
        await LoadDataAsync();
    }

    private async Task<IEnumerable<TagSuggestion>> SearchSuggestionsAsync(string? text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        try
        {
            return await ApiClient.SearchTagsAsync(text.Trim(), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return [];
        }
    }

    private async Task OnSuggestionSelected(TagSuggestion? suggestion)
    {
        if (suggestion is null || string.IsNullOrWhiteSpace(suggestion.TagName))
        {
            return;
        }

        var alreadyExists = suggestion.TagId.HasValue
            ? _selectedFilters.Any(f => f.TagId == suggestion.TagId)
            : _selectedFilters.Any(f => f.TagName.Equals(suggestion.TagName, StringComparison.OrdinalIgnoreCase) && f.UserName == suggestion.UserName);

        if (!alreadyExists)
        {
            _selectedFilters.Add(suggestion);
            _searchText = string.Empty;
            if (_autocomplete != null)
            {
                await _autocomplete.ClearAsync();
            }
            await LoadDataAsync();
        }
    }

    private async Task OnAdornmentClick()
    {
        if (!string.IsNullOrWhiteSpace(_searchText))
        {
            var text = _searchText.Trim();
            if (!_selectedFilters.Any(f => f.TagName.Equals(text, StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(f.UserName)))
            {
                _selectedFilters.Add(new TagSuggestion(null, text, null));
                _searchText = string.Empty;
                if (_autocomplete != null)
                {
                    await _autocomplete.ClearAsync();
                }
                await LoadDataAsync();
            }
        }
    }

    private async Task RemoveFilterAsync(TagSuggestion filter)
    {
        if (_selectedFilters.Remove(filter))
        {
            await LoadDataAsync();
        }
    }

    private async Task ClearFiltersAsync()
    {
        if (_selectedFilters.Count > 0)
        {
            _selectedFilters.Clear();
            await LoadDataAsync();
        }
    }

    /// <summary>
    /// 現在の検索条件に基づいてサーバー API からアイテム・タグデータを非同期に読み込みます。
    /// </summary>
    /// <returns>非同期タスク。</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "UI 表示向けにエラーメッセージへ集約するため")]
    public async Task LoadDataAsync()
    {
        _isLoading = true;
        _errorMessage = null;
        StateHasChanged();

        try
        {
            var filterDtos = _selectedFilters
                .Select(f => new ItemListFilterDto(
                    TagId: f.TagId,
                    TagName: f.TagId.HasValue ? null : f.TagName,
                    UserName: f.UserName
                ))
                .ToList();

            var request = new ItemListQueryRequest(
                Filters: filterDtos,
                Sorts: [],
                Skip: 0,
                Take: 50
            );

            ItemListQueryResponse response = await ApiClient.QueryItemsAsync(request);
            _items = response.Items;
            _tags = response.Tags;
            _totalCount = response.TotalCount;
        }
        catch (Exception ex)
        {
            _errorMessage = $"データの取得に失敗しました: {ex.Message}";
        }
        finally
        {
            _isLoading = false;
            StateHasChanged();
        }
    }
}