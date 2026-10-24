namespace SRNSMudApp.Tests.Client;

using System;
using System.Threading;
using System.Threading.Tasks;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Client.Components;
using SRNSMudApp.Client.Models.Api;
using SRNSMudApp.Client.Services;

using Xunit;

public class TagTreeViewTests : IAsyncDisposable
{
    private readonly BunitContext _ctx = new();
    private readonly Mock<IItemListApiClient> _apiClientMock = new();
    private readonly Mock<ISnackbar> _snackbarMock = new();

    public TagTreeViewTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddMudServices();

        _ctx.Services.AddScoped(_ => _apiClientMock.Object);
        _ctx.Services.AddScoped(_ => _snackbarMock.Object);
    }

    [Fact]
    public void WhenNoTags_RendersEmptyMessage()
    {
        // Arrange
        _apiClientMock
            .Setup(c => c.GetTagTreeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        IRenderedComponent<TagTreeView> cut = _ctx.Render<TagTreeView>();

        // Assert
        Assert.Contains("タグツリーデータがありません", cut.Markup);
    }

    [Fact]
    public void WhenTreeDataExists_RendersTreeNodes()
    {
        // Arrange
        var tree = new TagTreeNodeDto[]
        {
            new(1, "RootNode", "Root description", null, 15, "alice",
            [
                new(2, "ChildNode", "Child description", 1, 8, "bob", [])
            ])
        };

        _apiClientMock
            .Setup(c => c.GetTagTreeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(tree);

        // Act
        IRenderedComponent<TagTreeView> cut = _ctx.Render<TagTreeView>();

        // Assert
        Assert.Contains("RootNode", cut.Markup);
        Assert.Contains("スコア: 15", cut.Markup);
    }

    [Fact]
    public async Task WhenNodeSelected_RendersDetailsAndRelatedItems()
    {
        // Arrange
        var tree = new TagTreeNodeDto[]
        {
            new(1, "RootNode", "Root description", null, 15, "alice", [])
        };
        var item = new ItemDto(100, "関連アイテム本文", "alice", DateTimeOffset.UtcNow, false, [], "{}");
        var queryResp = new ItemListQueryResponse([item], [], 1);

        _apiClientMock
            .Setup(c => c.GetTagTreeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(tree);
        _apiClientMock
            .Setup(c => c.QueryItemsAsync(It.IsAny<ItemListQueryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(queryResp);

        // Act
        IRenderedComponent<TagTreeView> cut = _ctx.Render<TagTreeView>();
        await cut.InvokeAsync(() => cut.Instance.SelectNode(tree[0]));

        // Assert
        Assert.Contains("RootNode", cut.Markup);
        Assert.Contains("関連アイテム本文", cut.Markup);
        Assert.Contains("タグを編集", cut.Markup);
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}