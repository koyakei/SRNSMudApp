using System.Diagnostics.CodeAnalysis;

using SRNSMudApp.Models;
using SRNSMudApp.Services;

namespace SRNSMudApp.Components.Item;

/// <summary>
///     タグ検索バー (TagSearchBar) の状態管理と検索ロジックを担う ViewModel。
///     UI に依存しないため、単体テストを容易に行える。
/// </summary>
public sealed class TagSearchViewModel(IItemListDataProvider dataProvider)
{
    private readonly List<TagFilter> _selectedFilters = [];

    public IReadOnlyList<TagFilter> SelectedFilters => _selectedFilters;

    [SuppressMessage("Design", "CA1003:Use generic event handler instances", Justification = "Async callback for Blazor components")]
    public event Func<Task>? FiltersChanged;

    public void InitializeFilters(IEnumerable<TagFilter> initialFilters)
    {
        _selectedFilters.Clear();
        _selectedFilters.AddRange(initialFilters);
    }

    public async Task<IEnumerable<TagSuggestion>> SearchSuggestionsAsync(string? searchText, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return [];
        }

        try
        {
            return await dataProvider.SearchTagNameSuggestionsAsync(searchText.Trim(), token);
        }
        catch (OperationCanceledException)
        {
            return [];
        }
    }

    public async Task<bool> AddFilterFromSuggestionAsync(TagSuggestion? suggestion)
    {
        if (suggestion is null || string.IsNullOrWhiteSpace(suggestion.TagName))
        {
            return false;
        }

        var tagName = suggestion.TagName.Trim();
        var userName = string.IsNullOrWhiteSpace(suggestion.UserName) ? null : suggestion.UserName.Trim();

        var alreadyExists = suggestion.TagId.HasValue
            ? _selectedFilters.Any(f => f.TagId == suggestion.TagId)
            : _selectedFilters.Any(f => f.TagName.Equals(tagName, StringComparison.OrdinalIgnoreCase) && f.UserName == userName);

        if (alreadyExists)
        {
            return false;
        }

        Data.Tag? tag = null;
        if (suggestion.TagId.HasValue)
        {
            Dictionary<int, Data.Tag> tagsById = await dataProvider.GetTagsByIdsAsync([suggestion.TagId.Value]);
            _ = tagsById.TryGetValue(suggestion.TagId.Value, out tag);
        }
        else
        {
            tag = await dataProvider.FindTagByNameAsync(tagName);
        }

        _selectedFilters.Add(new TagFilter
        {
            TagId = suggestion.TagId,
            TagName = tagName,
            UserName = userName,
            Tag = tag
        });

        if (FiltersChanged != null)
        {
            await FiltersChanged.Invoke();
        }

        return true;
    }

    public async Task<bool> AddFilterFromTextAsync(string? searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return false;
        }

        var trimmed = searchText.Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        if (_selectedFilters.Any(f => f.TagName.Equals(trimmed, StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(f.UserName)))
        {
            return false;
        }

        Data.Tag? tag = await dataProvider.FindTagByNameAsync(trimmed);
        _selectedFilters.Add(new TagFilter
        {
            TagName = trimmed,
            Tag = tag,
            UserName = null
        });

        if (FiltersChanged != null)
        {
            await FiltersChanged.Invoke();
        }

        return true;
    }

    public async Task RemoveFilterAsync(TagFilter filter)
    {
        if (_selectedFilters.Remove(filter))
        {
            if (FiltersChanged != null)
            {
                await FiltersChanged.Invoke();
            }
        }
    }

    public async Task ClearFiltersAsync()
    {
        if (_selectedFilters.Count != 0)
        {
            _selectedFilters.Clear();
            if (FiltersChanged != null)
            {
                await FiltersChanged.Invoke();
            }
        }
    }
}