using Blazor.Diagrams;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;

using Bunit;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.Diagram;

using ItemEntity = SRNSMudApp.Data.Item;
using TagEntity = SRNSMudApp.Data.Tag;

namespace SRNSMudApp.Tests.Components.Diagram;

public class ItemNodeTests : IAsyncDisposable
{
    private readonly BunitContext _ctx = new();

    public ItemNodeTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices();
        _ = _ctx.Render<MudPopoverProvider>();
        _ctx.JSInterop.Setup<Rectangle>(invocation => invocation.Identifier.Contains("getBoundingClientRect"))
            .SetResult(new Rectangle(0, 0, 800, 600));
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }

    [Fact]
    public void ItemNode_Constructor_InitializesPropertiesAndPorts()
    {
        // Arrange
        var item = new ItemEntity { Id = 10, Content = "Test item content", OwnerId = "user1" };
        var linkedItem = new ItemEntity { Id = 20, Content = "Linked item", OwnerId = "user1" };
        var contextItems = new List<ItemEntity> { item, linkedItem };
        var position = new Point(100, 200);

        // Act
        var node = new ItemNode(item, contextItems, position);

        // Assert
        Assert.Same(item, node.Item);
        Assert.Equal(2, node.AllContextItems.Count);
        Assert.Equal(100, node.Position.X);
        Assert.Equal(200, node.Position.Y);
        Assert.False(node.IsExpanded);

        // 4つのポート (Left, Right, Top, Bottom) が登録されていること
        Assert.Equal(4, node.Ports.Count);
        Assert.NotNull(node.GetPort(PortAlignment.Left));
        Assert.NotNull(node.GetPort(PortAlignment.Right));
        Assert.NotNull(node.GetPort(PortAlignment.Top));
        Assert.NotNull(node.GetPort(PortAlignment.Bottom));
    }

    [Fact]
    public void ItemNode_ToggleIsExpanded_UpdatesState()
    {
        // Arrange
        var item = new ItemEntity { Id = 10, Content = "Test", OwnerId = "user1" };
        var node = new ItemNode(item, [item]);

        // Act & Assert
        Assert.False(node.IsExpanded);
        node.IsExpanded = true;
        Assert.True(node.IsExpanded);
        node.IsExpanded = false;
        Assert.False(node.IsExpanded);
    }

    [Fact]
    public void TagRelationLink_Constructor_SetsSourceAndTargetNodes()
    {
        // Arrange
        var item = new ItemEntity { Id = 10, Content = "Item", OwnerId = "user1" };
        var tag = new TagEntity { Id = 5, Name = "TagA", OwnerId = "user1" };
        var itemNode = new ItemNode(item, [item]);
        var tagNode = new TagNode(tag, new Point(0, 0));

        // Act
        var link = new TagRelationLink(itemNode, tagNode);

        // Assert
        Assert.Same(itemNode, link.Source.Model);
        Assert.Same(tagNode, link.Target.Model);
    }

    [Fact]
    public void TagRelationLink_RenderedInCanvas_RegistersAndRendersWithoutError()
    {
        // Arrange
        var diagram = new BlazorDiagram();
        diagram.RegisterComponent<TagNode, TagNodeWidget>();
        diagram.RegisterComponent<ItemNode, ItemNodeWidget>();
        diagram.RegisterComponent<TagRelationLink, TagRelationLinkWidget>();

        var item = new ItemEntity { Id = 10, Content = "Item", OwnerId = "user1" };
        var tag = new TagEntity { Id = 5, Name = "TagA", OwnerId = "user1" };
        var itemNode = new ItemNode(item, [item], new Point(0, 0));
        var tagNode = new TagNode(tag, new Point(200, 0));
        diagram.Nodes.Add(itemNode);
        diagram.Nodes.Add(tagNode);

        var link = new TagRelationLink(itemNode, tagNode);
        diagram.Links.Add(link);

        // Act
        var cut = _ctx.Render<TagDiagramCanvas>(parameters => parameters.Add(p => p.Diagram, diagram));

        // Assert
        Assert.NotNull(cut.Markup);
        Assert.Contains("tag-node", cut.Markup);
        Assert.Contains("item-node", cut.Markup);
    }
}