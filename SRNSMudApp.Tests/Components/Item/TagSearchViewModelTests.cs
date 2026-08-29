using Moq;

using SRNSMudApp.Components.Item;
using SRNSMudApp.Models;
using SRNSMudApp.Services;

namespace SRNSMudApp.Tests.Components.Item;

public sealed class TagSearchViewModelTests
{
    private readonly Mock<IItemListDataProvider> _dataProviderMock = new();

    [Fact]
    public async Task SearchSuggestionsAsync_CallsDataProviderWithTrimmedText()
    {
        var vm = new TagSearchViewModel(_dataProviderMock.Object);
        var expected = new List<TagSuggestion> { new(1, "TestTag", "user1") };

        _ = _dataProviderMock
            .Setup(d => d.SearchTagNameSuggestionsAsync("TestTag", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        IEnumerable<TagSuggestion> actual = await vm.SearchSuggestionsAsync("  TestTag  ");

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task SearchSuggestionsAsync_ReturnsEmpty_WhenSearchTextIsNullOrWhitespace()
    {
        var vm = new TagSearchViewModel(_dataProviderMock.Object);

        IEnumerable<TagSuggestion> actual = await vm.SearchSuggestionsAsync("   ");

        Assert.Empty(actual);
        _dataProviderMock.Verify(d => d.SearchTagNameSuggestionsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AddFilterFromSuggestionAsync_WithSpecificTagId_AddsTagIdFilterAndFiresEvent()
    {
        var vm = new TagSearchViewModel(_dataProviderMock.Object);
        var tag = new SRNSMudApp.Data.Tag { Id = 42, Name = "UniqueTag" };
        var eventFired = false;
        vm.FiltersChanged += () =>
        {
            eventFired = true;
            return Task.CompletedTask;
        };

        _ = _dataProviderMock
            .Setup(d => d.GetTagsByIdsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new Dictionary<int, SRNSMudApp.Data.Tag> { [42] = tag });

        var added = await vm.AddFilterFromSuggestionAsync(new TagSuggestion(42, "UniqueTag", "user1"));

        Assert.True(added);
        Assert.True(eventFired);
        var filter = Assert.Single(vm.SelectedFilters);
        Assert.Equal(42, filter.TagId);
        Assert.Equal("UniqueTag", filter.TagName);
        Assert.Equal("user1", filter.UserName);
        Assert.Equal(tag, filter.Tag);
    }

    [Fact]
    public async Task AddFilterFromSuggestionAsync_WithMultipleUsersTag_AddsTagNameOnlyFilter()
    {
        var vm = new TagSearchViewModel(_dataProviderMock.Object);
        var tag = new SRNSMudApp.Data.Tag { Id = 100, Name = "PopularTag" };

        _ = _dataProviderMock
            .Setup(d => d.FindTagByNameAsync("PopularTag"))
            .ReturnsAsync(tag);

        var added = await vm.AddFilterFromSuggestionAsync(new TagSuggestion(null, "PopularTag", null));

        Assert.True(added);
        var filter = Assert.Single(vm.SelectedFilters);
        Assert.Null(filter.TagId);
        Assert.Equal("PopularTag", filter.TagName);
        Assert.Null(filter.UserName);
        Assert.Equal(tag, filter.Tag);
    }

    [Fact]
    public async Task AddFilterFromSuggestionAsync_Duplicate_ReturnsFalseAndDoesNotAdd()
    {
        var vm = new TagSearchViewModel(_dataProviderMock.Object);
        _ = _dataProviderMock
            .Setup(d => d.GetTagsByIdsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new Dictionary<int, SRNSMudApp.Data.Tag>());

        _ = await vm.AddFilterFromSuggestionAsync(new TagSuggestion(1, "Tag1", "user1"));
        var addedSecondTime = await vm.AddFilterFromSuggestionAsync(new TagSuggestion(1, "Tag1", "user1"));

        Assert.False(addedSecondTime);
        Assert.Single(vm.SelectedFilters);
    }

    [Fact]
    public async Task AddFilterFromTextAsync_AddsTagNameFilter()
    {
        var vm = new TagSearchViewModel(_dataProviderMock.Object);
        var tag = new SRNSMudApp.Data.Tag { Id = 5, Name = "CustomTag" };

        _ = _dataProviderMock
            .Setup(d => d.FindTagByNameAsync("CustomTag"))
            .ReturnsAsync(tag);

        var added = await vm.AddFilterFromTextAsync(" CustomTag ");

        Assert.True(added);
        var filter = Assert.Single(vm.SelectedFilters);
        Assert.Equal("CustomTag", filter.TagName);
        Assert.Null(filter.UserName);
        Assert.Equal(tag, filter.Tag);
    }

    [Fact]
    public async Task RemoveFilterAsync_RemovesFilterAndFiresEvent()
    {
        var vm = new TagSearchViewModel(_dataProviderMock.Object);
        var eventFired = false;
        vm.FiltersChanged += () =>
        {
            eventFired = true;
            return Task.CompletedTask;
        };

        var filter = new TagFilter { TagName = "Test" };
        vm.InitializeFilters([filter]);

        await vm.RemoveFilterAsync(filter);

        Assert.True(eventFired);
        Assert.Empty(vm.SelectedFilters);
    }

    [Fact]
    public async Task ClearFiltersAsync_ClearsAllFiltersAndFiresEvent()
    {
        var vm = new TagSearchViewModel(_dataProviderMock.Object);
        var eventFired = false;
        vm.FiltersChanged += () =>
        {
            eventFired = true;
            return Task.CompletedTask;
        };

        vm.InitializeFilters([new TagFilter { TagName = "Test1" }, new TagFilter { TagName = "Test2" }]);

        await vm.ClearFiltersAsync();

        Assert.True(eventFired);
        Assert.Empty(vm.SelectedFilters);
    }
}

