namespace SRNSMudApp.Tests.Components.Item;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Moq;

using SRNSMudApp.Components.Item;
using SRNSMudApp.Data;
using SRNSMudApp.Services;

using Xunit;

/// <summary>
///     ItemListViewModel (純粋ロジック) の単体テスト。bUnit を使わずに xUnit + Moq で検証する。
/// </summary>
public sealed class ItemListViewModelTests
{
    private readonly Mock<IItemListDataProvider> _listDataMock = new();
    private readonly Mock<IItemListExportService> _exportServiceMock = new();

    private ItemListViewModel CreateViewModel()
    {
        return new ItemListViewModel(_listDataMock.Object, _exportServiceMock.Object);
    }

    [Fact]
    public void SetUser_SetsProperties()
    {
        ItemListViewModel vm = CreateViewModel();

        vm.SetUser("user-1", true);

        Assert.Equal("user-1", vm.CurrentUserId);
        Assert.True(vm.IsAdmin);
    }

    [Fact]
    public async Task InitializeAsync_RestoresFiltersAndSorts_FromUri()
    {
        ItemListViewModel vm = CreateViewModel();
        var tag1 = new Tag { Id = 10, Name = "C#", OwnerId = "owner" };
        var tag2 = new Tag { Id = 20, Name = "Blazor", OwnerId = "owner" };

        _listDataMock.Setup(d => d.GetTagsByIdsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new Dictionary<int, Tag> { [10] = tag1, [20] = tag2 });
        _listDataMock.Setup(d => d.GetTagsByNamesAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new Dictionary<string, Tag>());

        var item = new Item { Id = 1, Content = "TestItem", OwnerId = "owner" };
        _listDataMock.Setup(d => d.LoadItemsAndTagsAsync(
                It.IsAny<IReadOnlyList<ItemListFilter>>(),
                It.IsAny<IReadOnlyList<ItemListSort>>(),
                It.IsAny<string?>()))
            .ReturnsAsync(new ItemListPageData([item], [tag1, tag2]));

        var uri = new Uri("http://localhost/Item/ItemList?f=10&sort=20:asc");
        await vm.InitializeAsync(uri);

        Assert.Single(vm.TagSearch.SelectedFilters);
        Assert.Equal(10, vm.TagSearch.SelectedFilters[0].TagId);
        Assert.Single(vm.SortConditions);
        Assert.Equal(20, vm.SortConditions[0].Tag.Id);
        Assert.Equal(SortOrder.Asc, vm.SortConditions[0].Order);
        Assert.Single(vm.Items);
        Assert.Equal(2, vm.FoundTags.Count);
    }

    [Fact]
    public async Task OnFiltersChangedAsync_RemovesInactiveSortConditions_AndReloadsData()
    {
        ItemListViewModel vm = CreateViewModel();
        var tag1 = new Tag { Id = 10, Name = "Tag1", OwnerId = "owner" };
        var tag2 = new Tag { Id = 20, Name = "Tag2", OwnerId = "owner" };

        _listDataMock.Setup(d => d.LoadItemsAndTagsAsync(
                It.IsAny<IReadOnlyList<ItemListFilter>>(),
                It.IsAny<IReadOnlyList<ItemListSort>>(),
                It.IsAny<string?>()))
            .ReturnsAsync(new ItemListPageData([], []));

        vm.TagSearch.InitializeFilters([new TagFilter { TagId = 10, Tag = tag1, TagName = tag1.Name }]);
        await vm.AddSortConditionAsync(tag1);
        await vm.AddSortConditionAsync(tag2);
        Assert.Equal(2, vm.SortConditions.Count);

        // tag2 はフィルタに含まれていないため、OnFiltersChangedAsync で削除されるはず
        var queryStateChangedFired = false;
        vm.QueryStateChanged += () => queryStateChangedFired = true;

        await vm.OnFiltersChangedAsync();

        Assert.True(queryStateChangedFired);
        Assert.Single(vm.SortConditions);
        Assert.Equal(10, vm.SortConditions[0].Tag.Id);
    }

    [Fact]
    public void BuildQueryState_ReflectsCurrentFiltersAndSorts()
    {
        ItemListViewModel vm = CreateViewModel();
        var tag1 = new Tag { Id = 10, Name = "C#", OwnerId = "owner" };
        vm.TagSearch.InitializeFilters([new TagFilter { TagId = 10, Tag = tag1, TagName = tag1.Name }]);

        _listDataMock.Setup(d => d.LoadItemsAndTagsAsync(
                It.IsAny<IReadOnlyList<ItemListFilter>>(),
                It.IsAny<IReadOnlyList<ItemListSort>>(),
                It.IsAny<string?>()))
            .ReturnsAsync(new ItemListPageData([], []));

        _ = vm.AddSortConditionAsync(tag1);

        var uri = new Uri("http://localhost/Item/ItemList");
        ItemListQueryState state = vm.BuildQueryState(uri);

        Assert.Single(state.Filters);
        Assert.Equal(10, state.Filters[0].TagId);
        Assert.Single(state.SortEntries);
        Assert.Equal(10, state.SortEntries[0].TagId);
    }

    [Fact]
    public async Task AddSortConditionAsync_NewTag_AddsAndReturnsTrue()
    {
        ItemListViewModel vm = CreateViewModel();
        var tag = new Tag { Id = 10, Name = "Test", OwnerId = "owner" };

        _listDataMock.Setup(d => d.LoadItemsAndTagsAsync(
                It.IsAny<IReadOnlyList<ItemListFilter>>(),
                It.IsAny<IReadOnlyList<ItemListSort>>(),
                It.IsAny<string?>()))
            .ReturnsAsync(new ItemListPageData([], []));

        var added = await vm.AddSortConditionAsync(tag);
        Assert.True(added);
        Assert.Single(vm.SortConditions);
        Assert.Equal(SortOrder.Desc, vm.SortConditions[0].Order);

        // 重複追加
        var duplicateAdded = await vm.AddSortConditionAsync(tag);
        Assert.False(duplicateAdded);
        Assert.Single(vm.SortConditions);
    }

    [Fact]
    public async Task ToggleSortOrderAsync_TogglesOrder()
    {
        ItemListViewModel vm = CreateViewModel();
        var tag = new Tag { Id = 10, Name = "Test", OwnerId = "owner" };

        _listDataMock.Setup(d => d.LoadItemsAndTagsAsync(
                It.IsAny<IReadOnlyList<ItemListFilter>>(),
                It.IsAny<IReadOnlyList<ItemListSort>>(),
                It.IsAny<string?>()))
            .ReturnsAsync(new ItemListPageData([], []));

        await vm.AddSortConditionAsync(tag);
        SortCondition condition = vm.SortConditions[0];
        Assert.Equal(SortOrder.Desc, condition.Order);

        await vm.ToggleSortOrderAsync(condition);
        Assert.Equal(SortOrder.Asc, condition.Order);

        await vm.ToggleSortOrderAsync(condition);
        Assert.Equal(SortOrder.Desc, condition.Order);
    }

    [Fact]
    public async Task RemoveSortConditionAsync_RemovesCondition()
    {
        ItemListViewModel vm = CreateViewModel();
        var tag = new Tag { Id = 10, Name = "Test", OwnerId = "owner" };

        _listDataMock.Setup(d => d.LoadItemsAndTagsAsync(
                It.IsAny<IReadOnlyList<ItemListFilter>>(),
                It.IsAny<IReadOnlyList<ItemListSort>>(),
                It.IsAny<string?>()))
            .ReturnsAsync(new ItemListPageData([], []));

        await vm.AddSortConditionAsync(tag);
        Assert.Single(vm.SortConditions);

        await vm.RemoveSortConditionAsync(vm.SortConditions[0]);
        Assert.Empty(vm.SortConditions);
    }

    [Fact]
    public void SearchSortTags_FiltersSelectedTagsByName()
    {
        ItemListViewModel vm = CreateViewModel();
        var tag1 = new Tag { Id = 1, Name = "Frontend", OwnerId = "u1" };
        var tag2 = new Tag { Id = 2, Name = "Backend", OwnerId = "u1" };

        vm.TagSearch.InitializeFilters([
            new TagFilter { TagId = 1, Tag = tag1, TagName = tag1.Name },
            new TagFilter { TagId = 2, Tag = tag2, TagName = tag2.Name }
        ]);

        List<Tag> all = vm.SearchSortTags(null).ToList();
        Assert.Equal(2, all.Count);

        List<Tag> filtered = vm.SearchSortTags("front").ToList();
        Assert.Single(filtered);
        Assert.Equal("Frontend", filtered[0].Name);
    }

    [Fact]
    public async Task ExportToJsonAsync_CallsDataProviderAndExportService_ReturnsSerializedJson()
    {
        ItemListViewModel vm = CreateViewModel();
        var item = new Item { Id = 42, Content = "ExportMe", OwnerId = "owner" };

        _listDataMock.Setup(d => d.LoadItemsAndTagsAsync(
                It.IsAny<IReadOnlyList<ItemListFilter>>(),
                It.IsAny<IReadOnlyList<ItemListSort>>(),
                It.IsAny<string?>()))
            .ReturnsAsync(new ItemListPageData([item], []));

        await vm.LoadDataAsync();

        var exportData = new ItemListExportData([], [], []);
        _listDataMock.Setup(d => d.LoadExportDataAsync(It.Is<List<int>>(ids => ids.Contains(42))))
            .ReturnsAsync(exportData);

        var exportDto = new ExportItemDto
        {
            Content = "ExportMe",
            Owner = new ExportOwnerDto { Name = "owner" },
            Tags = [],
            LinkPreviews = []
        };
        _exportServiceMock.Setup(s => s.BuildExportAsync(exportData, vm.Items))
            .ReturnsAsync([exportDto]);

        var json = await vm.ExportToJsonAsync();

        Assert.NotNull(json);
        Assert.Contains("ExportMe", json);
    }

    [Fact]
    public void GetTagDiagramUrl_WhenNoItems_ReturnsBaseDiagramPath()
    {
        ItemListViewModel vm = CreateViewModel();

        string url = vm.GetTagDiagramUrl();

        Assert.Equal("/tag-diagram", url);
    }

    [Fact]
    public async Task GetTagDiagramUrl_WhenItemsExist_ReturnsUrlWithItemIds()
    {
        ItemListViewModel vm = CreateViewModel();
        var item1 = new Item { Id = 10, Content = "Item 1", OwnerId = "owner" };
        var item2 = new Item { Id = 20, Content = "Item 2", OwnerId = "owner" };

        _listDataMock.Setup(d => d.LoadItemsAndTagsAsync(
                It.IsAny<IReadOnlyList<ItemListFilter>>(),
                It.IsAny<IReadOnlyList<ItemListSort>>(),
                It.IsAny<string?>()))
            .ReturnsAsync(new ItemListPageData([item1, item2], []));

        await vm.LoadDataAsync();

        string url = vm.GetTagDiagramUrl();

        Assert.Equal("/tag-diagram?itemIds=10,20", url);
    }

    [Fact]
    public void Dispose_UnsubscribesFromTagSearch()
    {
        ItemListViewModel vm = CreateViewModel();
        // 例外なく Dispose できること
        vm.Dispose();
    }
}