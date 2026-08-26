using SRNSMudApp.Components.Item;

using Xunit;

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

    // --- Create + BuildParameters ラウンドトリップ ---

    [Fact]
    public void BuildParameters_ContainsTabAndRequestId()
    {
        Dictionary<string, object?> parameters =
            new ItemDetailQueryState
            {
                ActiveTab = "requests",
                SelectedRequestId = 7
            }
                .BuildParameters();

        Assert.Equal("requests", parameters["tab"]);
        Assert.Equal(7, parameters["requestId"]);
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
    }

    [Fact]
    public void RoundTrip_BuildThenParse_PreservesState()
    {
        ItemDetailQueryState original =
            ItemDetailQueryState.Create(tabIndex: 2, selectedRequestId: 55);

        Dictionary<string, object?> parameters = original.BuildParameters();
        var query = string.Join('&',
            parameters.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value?.ToString() ?? "")}"));

        var parsed = ItemDetailQueryState.ParseFromUri(
            new Uri($"http://localhost/ItemDetail/1?{query}"));

        Assert.Equal(original.ActiveTab, parsed.ActiveTab);
        Assert.Equal(original.SelectedRequestId, parsed.SelectedRequestId);

        // 正規化されたタブ文字列は同一インデックスへ逆変換できる
        Assert.Equal(2, ItemDetailQueryState.ToTabIndex(parsed.ActiveTab));
    }
}