namespace SRNSMudApp.Tests.Components.UI;

using System;
using System.Threading.Tasks;

using Moq;

using SRNSMudApp.Components.UI;
using SRNSMudApp.Data;
using SRNSMudApp.Services;

using Xunit;

public class QuotedItemPreviewViewModelTests
{
    private readonly Mock<IItemQuoteService> _quoteServiceMock = new();

    private QuotedItemPreviewViewModel CreateViewModel()
    {
        return new QuotedItemPreviewViewModel(_quoteServiceMock.Object);
    }

    [Fact]
    public async Task UpdateParametersAsync_WhenQuotedItemProvided_UsesProvidedItemWithoutCallingService()
    {
        var vm = CreateViewModel();
        var item = new Item { Id = 42, OwnerId = "u1", Content = "Test Item" };

        await vm.UpdateParametersAsync(42, item);

        Assert.Equal(42, vm.QuotedItemId);
        Assert.Same(item, vm.TargetItem);
        Assert.False(vm.IsLoading);
        _quoteServiceMock.Verify(s => s.GetQuotedItemAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task UpdateParametersAsync_WhenQuotedItemNullAndIdPositive_FetchesFromService()
    {
        var item = new Item { Id = 10, OwnerId = "u1", Content = "Fetched Item" };
        _quoteServiceMock.Setup(s => s.GetQuotedItemAsync(10))
            .ReturnsAsync(item);

        var vm = CreateViewModel();

        await vm.UpdateParametersAsync(10, null);

        Assert.Equal(10, vm.QuotedItemId);
        Assert.Same(item, vm.TargetItem);
        Assert.False(vm.IsLoading);
        _quoteServiceMock.Verify(s => s.GetQuotedItemAsync(10), Times.Once);
    }

    [Fact]
    public async Task UpdateParametersAsync_WhenQuotedItemNullAndIdZeroOrNegative_DoesNotFetch()
    {
        var vm = CreateViewModel();

        await vm.UpdateParametersAsync(0, null);

        Assert.Equal(0, vm.QuotedItemId);
        Assert.Null(vm.TargetItem);
        Assert.False(vm.IsLoading);
        _quoteServiceMock.Verify(s => s.GetQuotedItemAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task UpdateParametersAsync_WhenParametersUnchanged_SkipsReload()
    {
        var item = new Item { Id = 5, OwnerId = "u1", Content = "Cached" };
        _quoteServiceMock.Setup(s => s.GetQuotedItemAsync(5))
            .ReturnsAsync(item);

        var vm = CreateViewModel();

        await vm.UpdateParametersAsync(5, null);
        _quoteServiceMock.Verify(s => s.GetQuotedItemAsync(5), Times.Once);

        // 同じパラメータで再呼び出し
        await vm.UpdateParametersAsync(5, null);
        _quoteServiceMock.Verify(s => s.GetQuotedItemAsync(5), Times.Once); // 2回目は呼ばれない
    }

    [Fact]
    public async Task NavigationUrl_WhenQuotedItemIdPositive_ReturnsValidUrl()
    {
        var vm = CreateViewModel();
        await vm.UpdateParametersAsync(15, null);

        Assert.Equal("/ItemDetail/15", vm.NavigationUrl);
    }

    [Fact]
    public void NavigationUrl_WhenQuotedItemIdZero_ReturnsNull()
    {
        var vm = CreateViewModel();

        Assert.Null(vm.NavigationUrl);
    }

    [Fact]
    public async Task UpdateParametersAsync_WhenQuotedItemHasUrl_ParsesSegmentsCorrectly()
    {
        var vm = CreateViewModel();
        var item = new Item { Id = 42, OwnerId = "u1", Content = "前テキスト /ItemDetail/99 後テキスト" };

        await vm.UpdateParametersAsync(42, item);

        Assert.Equal(3, vm.Segments.Count);
        Assert.False(vm.Segments[0].IsUrl);
        Assert.Equal("前テキスト ", vm.Segments[0].Text);
        Assert.True(vm.Segments[1].IsUrl);
        Assert.Equal("/ItemDetail/99", vm.Segments[1].Text);
        Assert.False(vm.Segments[2].IsUrl);
        Assert.Equal(" 後テキスト", vm.Segments[2].Text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UpdateParametersAsync_WhenContentNullOrWhitespace_SegmentsIsEmpty(string? content)
    {
        var vm = CreateViewModel();
        var item = new Item { Id = 42, OwnerId = "u1", Content = content! };

        await vm.UpdateParametersAsync(42, item);

        Assert.Empty(vm.Segments);
    }

    [Fact]
    public async Task UpdateParametersAsync_WhenFetchedFromService_ParsesSegments()
    {
        var item = new Item { Id = 10, OwnerId = "u1", Content = "アイテム本文 /ItemDetail/20" };
        _quoteServiceMock.Setup(s => s.GetQuotedItemAsync(10))
            .ReturnsAsync(item);

        var vm = CreateViewModel();

        await vm.UpdateParametersAsync(10, null);

        Assert.Equal(2, vm.Segments.Count);
        Assert.False(vm.Segments[0].IsUrl);
        Assert.Equal("アイテム本文 ", vm.Segments[0].Text);
        Assert.True(vm.Segments[1].IsUrl);
        Assert.Equal("/ItemDetail/20", vm.Segments[1].Text);
    }
}