namespace SRNSMudApp.Components.Item;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using SRNSMudApp.Data;
using SRNSMudApp.Services;

/// <summary>
///     ItemList ページにおけるタグ検索・フィルタ・ソート・データロード・エクスポートロジックを集約する ViewModel。
///     bUnit を介さずに直接単体テスト可能。
/// </summary>
public sealed class ItemListViewModel : IDisposable
{
    private readonly IItemListDataProvider _listData;
    private readonly IItemListExportService _exportService;
    private readonly List<SortCondition> _sortConditions = [];

    public ItemListViewModel(IItemListDataProvider listData, IItemListExportService exportService)
    {
        _listData = listData;
        _exportService = exportService;
        TagSearch = new TagSearchViewModel(listData);
        TagSearch.FiltersChanged += OnFiltersChangedAsync;
    }

    public TagSearchViewModel TagSearch { get; }
    public IReadOnlyList<Item> Items { get; private set; } = [];
    public IReadOnlyList<Data.Tag> FoundTags { get; private set; } = [];
    public IReadOnlyList<SortCondition> SortConditions => _sortConditions;

    public string? CurrentUserId { get; private set; }
    public bool IsAdmin { get; private set; }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1003:Use generic event handler instances", Justification = "Action callback for Blazor components")]
    public event Action? StateChanged;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1003:Use generic event handler instances", Justification = "Action callback for Blazor components")]
    public event Action? QueryStateChanged;

    public void SetUser(string? currentUserId, bool isAdmin)
    {
        CurrentUserId = currentUserId;
        IsAdmin = isAdmin;
    }

    /// <summary>
    ///     URI のクエリ文字列からフィルタとソートを復元し、初期データを読み込む。
    /// </summary>
    public async Task InitializeAsync(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        ItemListQueryState state = ItemListQueryState.ParseFromUri(uri);
        IEnumerable<int> filterTagIds = state.Filters.Where(f => f.TagId.HasValue).Select(f => f.TagId!.Value);
        IEnumerable<int> sortTagIds = state.SortEntries.Select(e => e.TagId);
        Dictionary<int, Data.Tag> tagsById = await _listData.GetTagsByIdsAsync(filterTagIds.Concat(sortTagIds));

        List<string> nameFilters = [.. state.Filters
            .Where(f => !f.TagId.HasValue && !string.IsNullOrWhiteSpace(f.TagName))
            .Select(f => f.TagName!)];
        Dictionary<string, Data.Tag> tagsByName = await _listData.GetTagsByNamesAsync(nameFilters);

        // フィルタエントリを TagFilter に射影（TagId または TagName に基づくマッピング）
        var initialFilters = state.Filters
            .Select(filter => filter switch
            {
                { TagId: { } tagId } when tagsById.TryGetValue(tagId, out Data.Tag? tag) => new TagFilter
                {
                    TagId = tag.Id,
                    Tag = tag,
                    TagName = tag.Name,
                    UserName = filter.UserName
                },
                { TagId: null, TagName: { } name } when !string.IsNullOrWhiteSpace(name) => new TagFilter
                {
                    TagName = name,
                    Tag = tagsByName.GetValueOrDefault(name),
                    UserName = filter.UserName
                },
                _ => null
            })
            .OfType<TagFilter>()
            .ToList();

        TagSearch.InitializeFilters(initialFilters);

        _sortConditions.Clear();
        // ソートエントリを SortCondition に射影し AddRange で一括登録
        _sortConditions.AddRange(state.SortEntries
            .Select(entry => tagsById.TryGetValue(entry.TagId, out Data.Tag? tag)
                ? new SortCondition { Tag = tag, Order = entry.Order }
                : null)
            .OfType<SortCondition>());

        await LoadDataAsync();
    }

    /// <summary>
    ///     フィルタ変更時のハンドラ。非アクティブになったソート条件を除去してデータを再読み込みする。
    /// </summary>
    public async Task OnFiltersChangedAsync()
    {
        var activeTagIds = TagSearch.SelectedFilters
            .Where(f => f.TagId.HasValue)
            .Select(f => f.TagId!.Value)
            .ToHashSet();

        var activeTagNames = TagSearch.SelectedFilters
            .Where(f => !f.TagId.HasValue)
            .Select(f => f.TagName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        _ = _sortConditions.RemoveAll(c => !activeTagIds.Contains(c.Tag.Id) && !activeTagNames.Contains(c.Tag.Name));

        QueryStateChanged?.Invoke();
        await LoadDataAsync();
        StateChanged?.Invoke();
    }

    /// <summary>
    ///     現在の状態から更新後のクエリパラメータを生成するための ItemListQueryState を構築する。
    /// </summary>
    public ItemListQueryState BuildQueryState(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        return ItemListQueryState.ParseFromUri(uri) with
        {
            Filters = [.. TagSearch.SelectedFilters.Select(f => f.TagId.HasValue
                ? FilterEntry.FromId(f.TagId.Value, string.IsNullOrWhiteSpace(f.UserName) ? null : f.UserName)
                : FilterEntry.FromName(f.TagName, string.IsNullOrWhiteSpace(f.UserName) ? null : f.UserName))],
            SortEntries = [.. _sortConditions.Select(c => new SortEntry(c.Tag.Id, c.Order))]
        };
    }

    /// <summary>
    ///     アイテムおよびタグのリストを読み込む。
    /// </summary>
    public async Task LoadDataAsync()
    {
        List<ItemListFilter> filters = [.. TagSearch.SelectedFilters.Select(f => f.TagId.HasValue
            ? new ItemListFilter(new TagIdFilter(f.TagId.Value, string.IsNullOrWhiteSpace(f.UserName) ? null : f.UserName))
            : new ItemListFilter(new TagNameFilter(f.TagName, string.IsNullOrWhiteSpace(f.UserName) ? null : f.UserName)))];

        List<ItemListSort> sorts =
        [.. _sortConditions.Select(c => new ItemListSort(c.Tag.Id, c.Order == SortOrder.Asc))];

        ItemListPageData? page = IsAdmin
            ? await _listData.LoadItemsAndTagsAsync(filters, sorts, CurrentUserId, IsAdmin)
            : await _listData.LoadItemsAndTagsAsync(filters, sorts, CurrentUserId);

        Items = page?.Items ?? [];
        FoundTags = page?.Tags ?? [];
        StateChanged?.Invoke();
    }

    /// <summary>
    ///     ソート対象タグを追加する。
    /// </summary>
    public async Task<bool> AddSortConditionAsync(Data.Tag? tag)
    {
        if (tag != null && _sortConditions.All(c => c.Tag.Id != tag.Id))
        {
            _sortConditions.Add(new SortCondition { Tag = tag, Order = SortOrder.Desc });
            await LoadDataAsync();
            return true;
        }

        return false;
    }

    /// <summary>
    ///     ソート順序 (昇順/降順) を切り替える。
    /// </summary>
    public async Task ToggleSortOrderAsync(SortCondition condition)
    {
        ArgumentNullException.ThrowIfNull(condition);
        condition.Order = condition.Order == SortOrder.Desc ? SortOrder.Asc : SortOrder.Desc;
        await LoadDataAsync();
    }

    /// <summary>
    ///     ソート条件を削除する。
    /// </summary>
    public async Task RemoveSortConditionAsync(SortCondition condition)
    {
        ArgumentNullException.ThrowIfNull(condition);
        if (_sortConditions.Remove(condition))
        {
            await LoadDataAsync();
        }
    }

    /// <summary>
    ///     選択中フィルタからソート候補タグを検索する。
    /// </summary>
    public IEnumerable<Data.Tag> SearchSortTags(string? value)
    {
        IEnumerable<Data.Tag> source = TagSearch.SelectedFilters
            .Select(f => f.Tag)
            .Where(t => t != null)!;

        return string.IsNullOrWhiteSpace(value)
            ? source
            : source.Where(t => t.Name.Contains(value, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    ///     現在のアイテム一覧を JSON 文字列としてエクスポートする。
    /// </summary>
    public async Task<string> ExportToJsonAsync()
    {
        var itemIds = Items.Select(i => i.Id).ToList();
        ItemListExportData exportData = await _listData.LoadExportDataAsync(itemIds);
        IReadOnlyList<ExportItemDto> exportList = await _exportService.BuildExportAsync(exportData, Items);
        return ItemListExportService.Serialize(exportList);
    }

    /// <summary>
    ///     現在表示されているアイテム一覧を対象としたタグダイアグラムの URL を生成する。
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1055:UriReturnValuesShouldNotBeStrings", Justification = "NavigationManager URL pattern")]
    public string GetTagDiagramUrl()
    {
        if (Items.Count == 0)
        {
            return "/tag-diagram";
        }

        string itemIds = string.Join(",", Items.Select(i => i.Id));
        return $"/tag-diagram?itemIds={itemIds}";
    }

    public void Dispose()
    {
        TagSearch.FiltersChanged -= OnFiltersChangedAsync;
    }
}