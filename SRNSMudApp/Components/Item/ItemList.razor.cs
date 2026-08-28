// CA1508: union 型 (TagSearchQuery, ItemListFilter) の網羅的パターンマッチにおける解析器の誤検知のため抑制する。
#pragma warning disable CA1508

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

using SRNSMudApp.Models;
using SRNSMudApp.Services;

namespace SRNSMudApp.Components.Item;

/// <summary>
///     ItemList ページのコードビハインド。
///     マークアップ (.razor) 側は表示のみを担い、状態保持とサービス呼び出しはこちらに集約する。
/// </summary>
public partial class ItemList
{
    private IEnumerable<Data.Item> _items = [];
    private IEnumerable<Data.Tag> _foundTags = [];

    // ===== タグ検索フィルタ用ステート =====
    private readonly List<TagFilter> _selectedFilters = [];
    private string _tagSearchText = "";

    // ===== ソート用ステート =====
    private readonly List<SortCondition> _sortConditions = [];

    [Inject] private IItemListDataProvider ListData { get; set; } = null!;
    [Inject] private IJSRuntime JS { get; set; } = null!;
    [Inject] private NavigationManager NavigationManager { get; set; } = null!;
    [Inject] private IItemListExportService ExportService { get; set; } = null!;

    protected override async Task OnInitializedAsync()
    {
        // URL 復元は ItemListQueryState に一元化。
        // TagId 指定と TagName 指定の両方のエントリを復元する
        ItemListQueryState state = ItemListQueryState.ParseFromUri(new Uri(NavigationManager.Uri));
        IEnumerable<int> filterTagIds = state.Filters.Where(f => f.TagId.HasValue).Select(f => f.TagId!.Value);
        IEnumerable<int> sortTagIds = state.SortEntries.Select(e => e.TagId);
        Dictionary<int, Data.Tag> tagsById = await ListData.GetTagsByIdsAsync(filterTagIds.Concat(sortTagIds));

        List<string> nameFilters = state.Filters
            .Where(f => !f.TagId.HasValue && !string.IsNullOrWhiteSpace(f.TagName))
            .Select(f => f.TagName!)
            .ToList();
        Dictionary<string, Data.Tag> tagsByName = await ListData.GetTagsByNamesAsync(nameFilters);

        foreach (FilterEntry filter in state.Filters)
        {
            if (filter.TagId.HasValue)
            {
                if (tagsById.TryGetValue(filter.TagId.Value, out Data.Tag? tag))
                {
                    _selectedFilters.Add(new TagFilter
                    {
                        TagId = tag.Id,
                        Tag = tag,
                        TagName = tag.Name,
                        UserName = filter.UserName
                    });
                }
            }
            else if (!string.IsNullOrWhiteSpace(filter.TagName))
            {
                _ = tagsByName.TryGetValue(filter.TagName, out Data.Tag? tag);
                _selectedFilters.Add(new TagFilter
                {
                    TagName = filter.TagName,
                    Tag = tag,
                    UserName = filter.UserName
                });
            }
        }

        foreach (SortEntry entry in state.SortEntries)
        {
            if (tagsById.TryGetValue(entry.TagId, out Data.Tag? tag))
            {
                _sortConditions.Add(new SortCondition { Tag = tag, Order = entry.Order });
            }
        }

        await LoadDataAsync();
    }

    // ===== タグ検索メソッド =====

    private async Task<IEnumerable<string>> SearchTagsAndUsersAsync(string? value, CancellationToken token)
    {
        return TagSearchQuery.Parse(value) switch
        {
            EmptySearch => [],
            TagWithUserSearch tagWithUserSearch => await ListData.SearchTagUserNamesAsync(
                                tagWithUserSearch.TagName, tagWithUserSearch.UserName, token),
            _ => await ListData.SearchTagNameSuggestionsAsync(value ?? "", token),
        };
    }

    private async Task OnSearchTextChangedAsync(string? value)
    {
        _tagSearchText = value ?? string.Empty;

        // タグ名だけ選択された状態では検索を実行せず、次の入力を待つ
        switch (TagSearchQuery.Parse(_tagSearchText))
        {
            case TagWithUserSearch tagWithUserSearch:
                await ExecuteSearch(tagWithUserSearch.TagName, tagWithUserSearch.UserName);
                break;
            default:
                break;
        }
    }

    private async Task OnSearchKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
        {
            await ExecuteSearch();
        }
    }

    private async Task ExecuteSearch()
    {
        var query = TagSearchQuery.Parse(_tagSearchText);
        switch (query)
        {
            case EmptySearch:
                break;
            case IncompleteSearch incompleteSearch:
                await ExecuteSearch(incompleteSearch.TagName, null);
                break;
            case TagNameSearch tagNameSearch:
                await ExecuteSearch(tagNameSearch.TagName, null);
                break;
            case TagWithUserSearch tagWithUserSearch:
                await ExecuteSearch(tagWithUserSearch.TagName, tagWithUserSearch.UserName);
                break;
            default:
                break;
        }
    }

    private async Task ExecuteSearch(string tagName, string? userName)
    {
        Data.Tag? tag = await ListData.FindTagByNameAsync(tagName);

        if (!_selectedFilters.Any(f => f.TagName.Equals(tagName, StringComparison.OrdinalIgnoreCase) && f.UserName == userName))
        {
            _selectedFilters.Add(new TagFilter
            {
                TagName = tagName,
                Tag = tag,
                UserName = userName
            });
        }

        _tagSearchText = "";
        UpdateUrlQuery();
        await LoadDataAsync();
    }

    private void UpdateUrlQuery()
    {
        // 現在の URL から状態を読み取り、フィルタ / ソートのみ差し替える。
        // focus / item などの他パラメータは ItemListQueryState が保持してくれる
        ItemListQueryState updated = ItemListQueryState.ParseFromUri(new Uri(NavigationManager.Uri)) with
        {
            Filters = [.. _selectedFilters.Select(f => f.TagId.HasValue
                ? FilterEntry.FromId(f.TagId.Value, string.IsNullOrWhiteSpace(f.UserName) ? null : f.UserName)
                : FilterEntry.FromName(f.TagName, string.IsNullOrWhiteSpace(f.UserName) ? null : f.UserName))],
            SortEntries = [.. _sortConditions.Select(c => new SortEntry(c.Tag.Id, c.Order))]
        };

        var newUri = NavigationManager.GetUriWithQueryParameters(updated.BuildParameters());
        NavigationManager.NavigateTo(newUri, replace: true);
    }

    private async Task RemoveTagFilter(TagFilter filter)
    {
        _ = _selectedFilters.Remove(filter);
        _ = filter.TagId.HasValue
            ? _sortConditions.RemoveAll(c => c.Tag.Id == filter.TagId.Value)
            : _sortConditions.RemoveAll(c => c.Tag.Name.Equals(filter.TagName, StringComparison.OrdinalIgnoreCase));
        UpdateUrlQuery();
        await LoadDataAsync();
    }

    private async Task ClearAllTagFilters()
    {
        _selectedFilters.Clear();
        _sortConditions.Clear();
        UpdateUrlQuery();
        await LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        List<ItemListFilter> filters = [.. _selectedFilters.Select(f => f.TagId.HasValue
            ? new ItemListFilter(new TagIdFilter(f.TagId.Value, string.IsNullOrWhiteSpace(f.UserName) ? null : f.UserName))
            : new ItemListFilter(new TagNameFilter(f.TagName, string.IsNullOrWhiteSpace(f.UserName) ? null : f.UserName)))];

        List<ItemListSort> sorts =
        [.. _sortConditions.Select(c => new ItemListSort(c.Tag.Id, c.Order == SortOrder.Asc))];

        ItemListPageData page = await ListData.LoadItemsAndTagsAsync(filters, sorts);
        _items = page.Items;
        _foundTags = page.Tags;
    }

    private async Task OnSortTargetTagAdded(Data.Tag? tag)
    {
        if (tag != null && _sortConditions.All(c => c.Tag.Id != tag.Id))
        {
            _sortConditions.Add(new SortCondition { Tag = tag, Order = SortOrder.Desc });
            UpdateUrlQuery();
            await LoadDataAsync();
        }
    }

    private async Task ToggleSortOrder(SortCondition condition)
    {
        condition.Order = condition.Order == SortOrder.Desc ? SortOrder.Asc : SortOrder.Desc;
        UpdateUrlQuery();
        await LoadDataAsync();
    }

    private async Task RemoveSortCondition(SortCondition condition)
    {
        _ = _sortConditions.Remove(condition);
        UpdateUrlQuery();
        await LoadDataAsync();
    }

    private Task<IEnumerable<Data.Tag>> SearchSortTagsAsync(string? value, CancellationToken _)
    {
        IEnumerable<Data.Tag> source = _selectedFilters
            .Select(f => f.Tag)
            .Where(t => t != null)!;

        return Task.FromResult(string.IsNullOrWhiteSpace(value)
            ? source
            : source.Where(t => t.Name.Contains(value, StringComparison.OrdinalIgnoreCase)));
    }

    private async Task ExportToJsonAsync()
    {
        var itemIds = _items.Select(i => i.Id).ToList();

        ItemListExportData exportData = await ListData.LoadExportDataAsync(itemIds);
        IReadOnlyList<ExportItemDto> exportList = await ExportService.BuildExportAsync(exportData, _items);
        var json = ItemListExportService.Serialize(exportList);

        try
        {
            await JS.InvokeVoidAsync("window.downloadFileFromText", "search_results.json", json);
        }
        catch (JSDisconnectedException)
        {
            // 回線切断後のダウンロード要求は無視する
        }
        catch (JSException)
        {
            // JS interop によるダウンロード失敗は無視する（ページ表示への影響を避ける）
        }
    }
}