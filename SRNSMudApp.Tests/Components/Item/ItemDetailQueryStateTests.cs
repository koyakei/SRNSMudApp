using SRNSMudApp.Components.Item;

namespace SRNSMudApp.Tests.Components.Item;

/// <summary>
///     ItemDetailQueryState の単体テスト。
///     URL→状態、状態→URL、不正な値のフォールバックを bUnit なしで検証する。
/// </summary>
public class ItemDetailQueryStateTests
{
    // --- ParseFromUri ---

    [Fact]
    public void ParseFromUri_WithFullQuery_ParsesBothValues()
    {
        var state = ItemDetailQueryState.ParseFromUri(
            new Uri("http://localhost/ItemDetail/5?tab=requests&requestId=42"));

        Assert.Equal("requests", state.ActiveTab);
        Assert.Equal(42, state.SelectedRequestId);
    }

    [Fact]
    public void ParseFromUri_WithEmptyQuery_ReturnsNulls()
    {
        var state = ItemDetailQueryState.ParseFromUri(new Uri("http://localhost/ItemDetail/5"));

        Assert.Null(state.ActiveTab);
        Assert.Null(state.SelectedRequestId);
    }

    [Fact]
    public void ParseFromUri_WithInvalidRequestId_IgnoresValue()
    {
        foreach (var query in new[] { "requestId=abc", "requestId=-1", "requestId=0" })
        {
            var state = ItemDetailQueryState.ParseFromUri(
                new Uri($"http://localhost/ItemDetail/5?{query}"));

            Assert.Null(state.SelectedRequestId);
        }
    }

    [Fact]
    public void ParseFromUri_WithUnknownTab_KeepsRawValue()
    {
        // 未知のタブ値は正規化せずそのまま保持する (ToTabIndex で既定タブへフォールバック)
        var state = ItemDetailQueryState.ParseFromUri(
            new Uri("http://localhost/ItemDetail/5?tab=garbage"));

        Assert.Equal("garbage", state.ActiveTab);
    }

    // --- ToTabIndex ---

    [Theory]
    [InlineData("requests", 1)]
    [InlineData("history", 2)]
    [InlineData("details", 0)]
    [InlineData("garbage", 0)]
    [InlineData(null, 0)]
    public void ToTabIndex_MapsKnownTabsAndFallsBackToDefault(string? tab, int expected)
    {
        Assert.Equal(expected, ItemDetailQueryState.ToTabIndex(tab));
    }

    // --- FromTabIndex ---

    [Theory]
    [InlineData(1, "requests")]
    [InlineData(2, "history")]
    [InlineData(0, "details")]
    [InlineData(99, "details")]
    public void FromTabIndex_MapsIndexToNormalizedTab(int index, string expected)
    {
        Assert.Equal(expected, ItemDetailQueryState.FromTabIndex(index));
    }

    [Fact]
    public void ParseFromUri_WithFilters_ParsesFiltersCorrectly()
    {
        var state = ItemDetailQueryState.ParseFromUri(
            new Uri("http://localhost/ItemDetail/5?f=name:tagA&f=10@userB"));

        Assert.Equal(2, state.Filters.Count);
        Assert.Equal("tagA", state.Filters[0].TagName);
        Assert.Null(state.Filters[0].UserName);
        Assert.Equal(10, state.Filters[1].TagId);
        Assert.Equal("userB", state.Filters[1].UserName);
    }

    [Fact]
    public void ParseFromUri_WithFallbackSearchQuery_ParsesFilters()
    {
        var state = ItemDetailQueryState.ParseFromUri(
            new Uri("http://localhost/ItemDetail/5?search=tagA%20%40userB"));

        Assert.Single(state.Filters);
        Assert.Equal("tagA", state.Filters[0].TagName);
        Assert.Equal("userB", state.Filters[0].UserName);
    }

    [Fact]
    public void ParseFromUri_WithFallbackQQuery_ParsesFilters()
    {
        var state = ItemDetailQueryState.ParseFromUri(
            new Uri("http://localhost/ItemDetail/5?q=tagA"));

        Assert.Single(state.Filters);
        Assert.Equal("tagA", state.Filters[0].TagName);
        Assert.Null(state.Filters[0].UserName);
    }

    // --- Create + BuildParameters ラウンドトリップ ---

    [Fact]
    public void BuildParameters_ContainsTabAndRequestIdAndFilters()
    {
        Dictionary<string, object?> parameters =
            new ItemDetailQueryState
            {
                ActiveTab = "requests",
                SelectedRequestId = 7,
                Filters = [FilterEntry.FromName("tagA", "userB")]
            }
                .BuildParameters();

        Assert.Equal("requests", parameters["tab"]);
        Assert.Equal(7, parameters["requestId"]);
        var filters = Assert.IsType<string[]>(parameters["f"]);
        Assert.Contains("name:tagA@userB", filters);
    }

    [Fact]
    public void BuildParameters_NullValues_ArePresentAsNullForRemoval()
    {
        Dictionary<string, object?> parameters =
            new ItemDetailQueryState().BuildParameters();

        // GetUriWithQueryParameters は null 値のキーを URL から削除するため、null のまま渡す
        Assert.True(parameters.ContainsKey("tab"));
        Assert.Null(parameters["tab"]);
        Assert.True(parameters.ContainsKey("requestId"));
        Assert.Null(parameters["requestId"]);
        Assert.True(parameters.ContainsKey("f"));
        Assert.Null(parameters["f"]);
    }

    [Fact]
    public void RoundTrip_BuildThenParse_PreservesState()
    {
        ItemDetailQueryState original =
            ItemDetailQueryState.Create(tabIndex: 2, selectedRequestId: 55, [FilterEntry.FromName("testTag", "userX")]);

        Dictionary<string, object?> parameters = original.BuildParameters();
        List<string> queryParts = [];
        foreach ((string key, object? value) in parameters)
        {
            if (value is string[] arr)
            {
                queryParts.AddRange(arr.Select(v => $"{key}={Uri.EscapeDataString(v)}"));
            }
            else if (value != null)
            {
                queryParts.Add($"{key}={Uri.EscapeDataString(value.ToString() ?? "")}");
            }
        }
        var query = string.Join('&', queryParts);

        var parsed = ItemDetailQueryState.ParseFromUri(
            new Uri($"http://localhost/ItemDetail/1?{query}"));

        Assert.Equal(original.ActiveTab, parsed.ActiveTab);
        Assert.Equal(original.SelectedRequestId, parsed.SelectedRequestId);
        Assert.Single(parsed.Filters);
        Assert.Equal("testTag", parsed.Filters[0].TagName);
        Assert.Equal("userX", parsed.Filters[0].UserName);

        // 正規化されたタブ文字列は同一インデックスへ逆変換できる
        Assert.Equal(2, ItemDetailQueryState.ToTabIndex(parsed.ActiveTab));
    }
}