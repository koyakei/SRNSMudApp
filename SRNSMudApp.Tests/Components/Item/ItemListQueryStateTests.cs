using SRNSMudApp.Components.Item;

namespace SRNSMudApp.Tests.Components.Item;

/// <summary>
///     ItemListQueryState の単体テスト。
///     URL クエリパラメータのパースと再構築ロジックを高速に検証する。
///     クエリ形式:
///       f=&lt;tagId&gt; / f=&lt;tagId&gt;@&lt;userName&gt; / f=name:&lt;tagName&gt; / f=name:&lt;tagName&gt;@&lt;userName&gt; (繰り返し可)
///       sort=&lt;tagId&gt;:&lt;asc|desc&gt; (繰り返し可、出現順 = 優先度)
///       item=&lt;itemId&gt; (繰り返し可) / focus=&lt;itemId&gt; / focusTag=&lt;tagId&gt;
/// </summary>
public class ItemListQueryStateTests
{
    // ────────────────────────────────────────────────────────────
    // ParseFromUri — f (フィルタ)
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void ParseFromUri_WithFilters_ParsesTagIds()
    {
        var state = ItemListQueryState.ParseFromUri(new Uri("http://localhost/Item/ItemList?f=3&f=7"));

        Assert.Equal(2, state.Filters.Count);
        Assert.Equal((3, null), (state.Filters[0].TagId, state.Filters[0].UserName));
        Assert.Equal((7, null), (state.Filters[1].TagId, state.Filters[1].UserName));
    }

    [Fact]
    public void ParseFromUri_WithTagNameFilters_ParsesTagNames()
    {
        var state = ItemListQueryState.ParseFromUri(new Uri("http://localhost/Item/ItemList?f=name:CSharp&f=name:React@alice"));

        Assert.Equal(2, state.Filters.Count);
        Assert.Equal(("CSharp", null), (state.Filters[0].TagName, state.Filters[0].UserName));
        Assert.Equal(("React", "alice"), (state.Filters[1].TagName, state.Filters[1].UserName));
    }

    [Fact]
    public void ParseFromUri_WithNoFilters_ReturnsEmpty()
    {
        var state = ItemListQueryState.ParseFromUri(new Uri("http://localhost/Item/ItemList"));

        Assert.Empty(state.Filters);
    }

    [Fact]
    public void ParseFromUri_WithInvalidFilter_IgnoresInvalidValues()
    {
        var state = ItemListQueryState.ParseFromUri(new Uri("http://localhost/Item/ItemList?f=5&f=0&f=-1"));

        Assert.Single(state.Filters);
        Assert.Equal(5, state.Filters[0].TagId);
    }

    [Fact]
    public void ParseFromUri_WithUserFilter_ParsesUserName()
    {
        var state = ItemListQueryState.ParseFromUri(new Uri("http://localhost/Item/ItemList?f=12@alice"));

        FilterEntry filter = Assert.Single(state.Filters);
        Assert.Equal(12, filter.TagId);
        Assert.Equal("alice", filter.UserName);
    }

    [Fact]
    public void ParseFromUri_WithAtInUserName_SplitsAtFirstAtOnly()
    {
        var state = ItemListQueryState.ParseFromUri(new Uri("http://localhost/Item/ItemList?f=12@alice@example.com"));

        FilterEntry filter = Assert.Single(state.Filters);
        Assert.Equal(12, filter.TagId);
        Assert.Equal("alice@example.com", filter.UserName);
    }

    // ────────────────────────────────────────────────────────────
    // ParseFromUri — sort
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void ParseFromUri_WithSortDesc_ParsesSortEntry()
    {
        var state = ItemListQueryState.ParseFromUri(new Uri("http://localhost/Item/ItemList?sort=3:desc"));

        SortEntry entry = Assert.Single(state.SortEntries);
        Assert.Equal(3, entry.TagId);
        Assert.Equal(SortOrder.Desc, entry.Order);
    }

    [Fact]
    public void ParseFromUri_WithSortAsc_ParsesSortEntry()
    {
        var state = ItemListQueryState.ParseFromUri(new Uri("http://localhost/Item/ItemList?sort=5:asc"));

        SortEntry entry = Assert.Single(state.SortEntries);
        Assert.Equal(SortOrder.Asc, entry.Order);
    }

    [Fact]
    public void ParseFromUri_WithMultipleSortKeys_PreservesPriorityOrder()
    {
        var state = ItemListQueryState.ParseFromUri(
            new Uri("http://localhost/Item/ItemList?sort=1:desc&sort=2:asc"));

        Assert.Equal(2, state.SortEntries.Count);
        Assert.Equal(1, state.SortEntries[0].TagId);
        Assert.Equal(2, state.SortEntries[1].TagId);
    }

    [Theory]
    [InlineData("sort=3")]
    [InlineData("sort=abc:desc")]
    [InlineData("sort=3:")]
    public void ParseFromUri_WithMalformedSort_IgnoresValue(string sortQuery)
    {
        var state = ItemListQueryState.ParseFromUri(new Uri($"http://localhost/Item/ItemList?{sortQuery}"));

        Assert.Empty(state.SortEntries);
    }

    // ────────────────────────────────────────────────────────────
    // ParseFromUri — item / focus / focusTag
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void ParseFromUri_WithSelectedItems_ParsesAllIdsInOrder()
    {
        var state = ItemListQueryState.ParseFromUri(
            new Uri("http://localhost/Item/ItemList?item=101&item=205&item=abc"));

        Assert.Equal([101, 205], state.SelectedItemIds);
    }

    [Fact]
    public void ParseFromUri_WithFocus_ParsesFocusItemId()
    {
        var state = ItemListQueryState.ParseFromUri(new Uri("http://localhost/Item/ItemList?focus=42"));

        Assert.Equal(42, state.FocusItemId);
    }

    [Fact]
    public void ParseFromUri_WithNoFocus_ReturnsNullFocus()
    {
        var state = ItemListQueryState.ParseFromUri(new Uri("http://localhost/Item/ItemList"));

        Assert.Null(state.FocusItemId);
    }

    [Fact]
    public void ParseFromUri_WithFocusTag_ParsesFocusTagId()
    {
        var state = ItemListQueryState.ParseFromUri(new Uri("http://localhost/Item/ItemList?focusTag=9"));

        Assert.Equal(9, state.FocusTagId);
    }

    // ────────────────────────────────────────────────────────────
    // ParseFromUri — 複合パラメータ
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void ParseFromUri_WithAllParameters_ParsesAll()
    {
        var state = ItemListQueryState.ParseFromUri(
            new Uri("http://localhost/Item/ItemList?f=10@bob&sort=10:desc&item=55&focus=99&focusTag=7"));

        FilterEntry filter = Assert.Single(state.Filters);
        Assert.Equal((10, "bob"), (filter.TagId, filter.UserName));
        Assert.Equal(SortOrder.Desc, state.SortEntries.Single().Order);
        Assert.Equal([55], state.SelectedItemIds);
        Assert.Equal(99, state.FocusItemId);
        Assert.Equal(7, state.FocusTagId);
    }

    // ────────────────────────────────────────────────────────────
    // BuildParameters
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void BuildParameters_WithFilters_EncodesUserSuffix()
    {
        var state = new ItemListQueryState
        {
            Filters = [FilterEntry.FromId(12, "alice"), FilterEntry.FromId(3, null), FilterEntry.FromName("React", "bob")]
        };

        Dictionary<string, object?> parameters = state.BuildParameters();

        Assert.Equal(["12@alice", "3", "name:React@bob"], (string[])parameters["f"]!);
    }

    [Fact]
    public void BuildParameters_WithNoFilters_SetsKeyToNull()
    {
        Dictionary<string, object?> parameters = new ItemListQueryState().BuildParameters();

        Assert.Null(parameters["f"]);
        Assert.Null(parameters["sort"]);
        Assert.Null(parameters["item"]);
        Assert.Null(parameters["focus"]);
        Assert.Null(parameters["focusTag"]);
    }

    [Fact]
    public void BuildParameters_WithSortEntries_EncodesEachEntry()
    {
        var state = new ItemListQueryState
        {
            SortEntries = [new SortEntry(5, SortOrder.Desc), new SortEntry(8, SortOrder.Asc)]
        };

        Dictionary<string, object?> parameters = state.BuildParameters();

        Assert.Equal(["5:desc", "8:asc"], (string[])parameters["sort"]!);
    }

    [Fact]
    public void BuildParameters_WithSelectedItemIds_EncodesArray()
    {
        var state = new ItemListQueryState { SelectedItemIds = [101, 205] };

        Dictionary<string, object?> parameters = state.BuildParameters();

        Assert.Equal([101, 205], (int[])parameters["item"]!);
    }

    [Fact]
    public void BuildParameters_RoundTrip_PreservesAllParameters()
    {
        var original = ItemListQueryState.ParseFromUri(
            new Uri("http://localhost/Item/ItemList?f=3&f=7@alice&f=name:React@bob&sort=3:desc&sort=7:asc&item=11&item=22&focus=99&focusTag=6"));
        Dictionary<string, object?> parameters = original.BuildParameters();

        var query = string.Join("&",
            parameters.SelectMany(kv => kv.Value switch
            {
                null => [],
                System.Collections.IEnumerable list when kv.Value is not string =>
                    list.Cast<object>().Select(v => $"{kv.Key}={v}"),
                _ => [$"{kv.Key}={kv.Value}"]
            }));

        var reparsed = ItemListQueryState.ParseFromUri(new Uri($"http://localhost/Item/ItemList?{query}"));

        Assert.Equivalent(original.Filters, reparsed.Filters);
        Assert.Equivalent(original.SortEntries, reparsed.SortEntries);
        Assert.Equivalent(original.SelectedItemIds, reparsed.SelectedItemIds);
        Assert.Equal(original.FocusItemId, reparsed.FocusItemId);
        Assert.Equal(original.FocusTagId, reparsed.FocusTagId);
    }
}