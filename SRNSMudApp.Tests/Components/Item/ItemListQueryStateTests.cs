using SRNSMudApp.Components.Item;

namespace SRNSMudApp.Tests.Components.Item;

/// <summary>
///     ItemListQueryState の単体テスト。
///     URL クエリパラメータのパースと再構築ロジックを高速に検証する。
///     以前は ItemListTagSearchE2ETests / ItemListFocusE2ETests で Playwright を
///     使って間接的にしか検証できなかった内容。
/// </summary>
public class ItemListQueryStateTests
{
    // ────────────────────────────────────────────────────────────
    // ParseFromUri — tags
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void ParseFromUri_WithTags_ParsesTagIds()
    {
        var state = ItemListQueryState.ParseFromUri("http://localhost/Item/ItemList?tags=3&tags=7");

        Assert.Equal([3, 7], state.TagIds);
    }

    [Fact]
    public void ParseFromUri_WithNoTags_ReturnsEmptyTagIds()
    {
        var state = ItemListQueryState.ParseFromUri("http://localhost/Item/ItemList");

        Assert.Empty(state.TagIds);
    }

    [Fact]
    public void ParseFromUri_WithInvalidTagId_IgnoresInvalidValues()
    {
        var state = ItemListQueryState.ParseFromUri("http://localhost/Item/ItemList?tags=5&tags=abc");

        Assert.Equal([5], state.TagIds);
    }

    // ────────────────────────────────────────────────────────────
    // ParseFromUri — sort
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void ParseFromUri_WithSortDesc_ParsesSortEntry()
    {
        var state = ItemListQueryState.ParseFromUri("http://localhost/Item/ItemList?sort=3:desc");

        Assert.Single(state.SortEntries);
        Assert.Equal(3, state.SortEntries[0].TagId);
        Assert.Equal(SortOrder.Desc, state.SortEntries[0].Order);
    }

    [Fact]
    public void ParseFromUri_WithSortAsc_ParsesSortEntry()
    {
        var state = ItemListQueryState.ParseFromUri("http://localhost/Item/ItemList?sort=5:asc");

        Assert.Single(state.SortEntries);
        Assert.Equal(SortOrder.Asc, state.SortEntries[0].Order);
    }

    [Fact]
    public void ParseFromUri_WithMultipleSortEntries_ParseesAll()
    {
        var state = ItemListQueryState.ParseFromUri("http://localhost/Item/ItemList?sort=1:desc,2:asc");

        Assert.Equal(2, state.SortEntries.Count);
        Assert.Equal(1, state.SortEntries[0].TagId);
        Assert.Equal(2, state.SortEntries[1].TagId);
    }

    // ────────────────────────────────────────────────────────────
    // ParseFromUri — focusItem
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void ParseFromUri_WithFocusItem_ParsesFocusItemId()
    {
        var state = ItemListQueryState.ParseFromUri("http://localhost/Item/ItemList?focusItem=42");

        Assert.Equal(42, state.FocusItemId);
    }

    [Fact]
    public void ParseFromUri_WithNoFocusItem_ReturnNullFocusItemId()
    {
        var state = ItemListQueryState.ParseFromUri("http://localhost/Item/ItemList");

        Assert.Null(state.FocusItemId);
    }

    // ────────────────────────────────────────────────────────────
    // ParseFromUri — 複合パラメータ
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void ParseFromUri_WithTagsAndFocusItem_ParseesBoth()
    {
        var state = ItemListQueryState.ParseFromUri(
            "http://localhost/Item/ItemList?tags=10&focusItem=99");

        Assert.Equal([10], state.TagIds);
        Assert.Equal(99, state.FocusItemId);
    }

    // ────────────────────────────────────────────────────────────
    // BuildParameters
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void BuildParameters_WithTags_SetTagsKey()
    {
        var state = new ItemListQueryState { TagIds = [3, 7], SortEntries = [] };

        Dictionary<string, object?> parameters = state.BuildParameters();

        Assert.True(parameters.ContainsKey("tags"));
        var tagArray = (int[])parameters["tags"]!;
        Assert.Equal([3, 7], tagArray);
    }

    [Fact]
    public void BuildParameters_WithNoTags_SetsTagsToNull()
    {
        var state = new ItemListQueryState { TagIds = [], SortEntries = [] };

        Dictionary<string, object?> parameters = state.BuildParameters();

        Assert.Null(parameters["tags"]);
    }

    [Fact]
    public void BuildParameters_WithSortDesc_SetsSortParam()
    {
        var state = new ItemListQueryState
        {
            TagIds = [], SortEntries = [new SortEntry(5, SortOrder.Desc)]
        };

        Dictionary<string, object?> parameters = state.BuildParameters();

        Assert.Equal("5:desc", parameters["sort"]);
    }

    [Fact]
    public void BuildParameters_WithNoSort_SetsSortToNull()
    {
        var state = new ItemListQueryState { TagIds = [], SortEntries = [] };

        Dictionary<string, object?> parameters = state.BuildParameters();

        Assert.Null(parameters["sort"]);
    }

    [Fact]
    public void BuildParameters_RoundTrip_PreservesTagsAndSort()
    {
        // ParseFromUri → BuildParameters でパラメータが保持されることを確認
        var original = ItemListQueryState.ParseFromUri(
            "http://localhost/Item/ItemList?tags=3&tags=7&sort=3:desc,7:asc");
        Dictionary<string, object?> parameters = original.BuildParameters();

        var tagArray = (int[])parameters["tags"]!;
        Assert.Equal([3, 7], tagArray);
        Assert.Equal("3:desc,7:asc", parameters["sort"]);
    }
}