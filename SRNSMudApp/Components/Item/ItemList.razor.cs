using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

using SRNSMudApp.Models;
using SRNSMudApp.Services;

namespace SRNSMudApp.Components.Item;

using Item = SRNSMudApp.Data.Item;
// 兄弟名前空間 SRNSMudApp.Components.Tag より先に Data.Tag 型を解決させるため、
// using を名前空間の内側に置く。namespace Item も同名型と衝突する
using Tag = SRNSMudApp.Data.Tag;

/// <summary>
///     ItemList ページのコードビハインド。
///     マークアップ (.razor) 側は表示のみを担い、状態保持とサービス呼び出しはこちらに集約する。
/// </summary>
public partial class ItemList
{
    private IEnumerable<Item> _items = [];
    private IEnumerable<Tag> _foundTags = [];

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
        // URL 復元は ItemListQueryState に一元化。タグ ID だけでは表示名が必要なため
        // フィルタ + ソートで参照される ID を一括取得して Tag 実体へ解決する
        var state = ItemListQueryState.ParseFromUri(new Uri(NavigationManager.Uri));
        Dictionary<int, Tag> tagsById = await ListData.GetTagsByIdsAsync(
            state.Filters.Select(f => f.TagId).Concat(state.SortEntries.Select(e => e.TagId)));

        foreach (FilterEntry filter in state.Filters)
        {
            if (tagsById.TryGetValue(filter.TagId, out Tag? tag))
            {
                _selectedFilters.Add(new TagFilter { Tag = tag, UserName = filter.UserName });
            }
        }

        foreach (SortEntry entry in state.SortEntries)
        {
            if (tagsById.TryGetValue(entry.TagId, out Tag? tag))
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
        }
    }

    private async Task OnSearchKeyDown(KeyboardEventArgs e)
    {
        switch (e.Key == "Enter")
        {
            case true:
                await ExecuteSearch();
                break;
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
        }
    }

    private async Task ExecuteSearch(string tagName, string? userName)
    {
        Tag? tag = await ListData.FindTagByNameAsync(tagName);

        switch (tag)
        {
            case not null:
                switch (!_selectedFilters.Any(f => f.Tag.Id == tag.Id && f.UserName == userName))
                {
                    case true:
                        _selectedFilters.Add(new TagFilter { Tag = tag, UserName = userName });
                        break;
                }
                break;
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
            Filters = [.. _selectedFilters.Select(f => new FilterEntry(
                f.Tag.Id,
                string.IsNullOrWhiteSpace(f.UserName) ? null : f.UserName))],
            SortEntries = [.. _sortConditions.Select(c => new SortEntry(c.Tag.Id, c.Order))]
        };

        var newUri = NavigationManager.GetUriWithQueryParameters(updated.BuildParameters());
        NavigationManager.NavigateTo(newUri, replace: true);
    }

    private async Task RemoveTagFilter(TagFilter filter)
    {
        _selectedFilters.Remove(filter);
        _sortConditions.RemoveAll(c => c.Tag.Id == filter.Tag.Id);
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
        List<ItemListFilter> filters =
        [.. _selectedFilters.Select(f => new ItemListFilter(f.Tag.Id, string.IsNullOrWhiteSpace(f.UserName) ? null : f.UserName))];
        List<ItemListSort> sorts =
        [.. _sortConditions.Select(c => new ItemListSort(c.Tag.Id, c.Order == SortOrder.Asc))];

        ItemListPageData page = await ListData.LoadItemsAndTagsAsync(filters, sorts);
        _items = page.Items;
        _foundTags = page.Tags;
    }

    private async Task OnSortTargetTagAdded(Tag? tag)
    {
        switch (tag != null && _sortConditions.All(c => c.Tag.Id != tag.Id))
        {
            case true:
                _sortConditions.Add(new SortCondition { Tag = tag, Order = SortOrder.Desc });
                UpdateUrlQuery();
                await LoadDataAsync();
                break;
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
        _sortConditions.Remove(condition);
        UpdateUrlQuery();
        await LoadDataAsync();
    }

    private Task<IEnumerable<Tag>> SearchSortTagsAsync(string? value, CancellationToken token)
    {
        IEnumerable<Tag> source = _selectedFilters.Select(f => f.Tag);

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