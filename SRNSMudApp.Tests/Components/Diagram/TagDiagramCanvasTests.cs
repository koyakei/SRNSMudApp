using Blazor.Diagrams;
using Blazor.Diagrams.Core.Geometry;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.Diagram;
using SRNSMudApp.Components.Tag;
using SRNSMudApp.Data;

using TagEntity = SRNSMudApp.Data.Tag;

namespace SRNSMudApp.Tests.Components.Diagram;

public class TagDiagramCanvasTests : IAsyncDisposable
{
    private readonly BunitContext _ctx = new();
    private readonly IRenderedComponent<MudPopoverProvider> _popoverProvider;

    public TagDiagramCanvasTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices();
        _popoverProvider = _ctx.Render<MudPopoverProvider>();
        _ctx.JSInterop.Setup<Rectangle>(invocation => invocation.Identifier.Contains("getBoundingClientRect"))
            .SetResult(new Rectangle(0, 0, 800, 600));
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }

    [Fact]
    public void TagDiagramCanvas_RendersAndDisposesCleanly_WithoutJSInteropError()
    {
        var diagram = new BlazorDiagram();
        diagram.RegisterComponent<TagNode, TagNodeWidget>();

        var cut = _ctx.Render<TagDiagramCanvas>(parameters => parameters
            .Add(p => p.Diagram, diagram));

        Assert.NotNull(cut.Markup);

        // Component should dispose cleanly without throwing InvalidOperationException
        cut.Dispose();
    }

    [Fact]
    public void TagDiagramCanvas_SelectionChanged_InvokesCallbacks()
    {
        var diagram = new BlazorDiagram();
        diagram.RegisterComponent<TagNode, TagNodeWidget>();

        var tag = new TagEntity { Id = 1, Name = "Alpha", OwnerId = "u1" };
        var node = new TagNode(tag, new Point(10, 10));
        diagram.Nodes.Add(node);

        TagEntity? selectedTag = null;
        var cut = _ctx.Render<TagDiagramCanvas>(parameters => parameters
            .Add(p => p.Diagram, diagram)
            .Add(p => p.OnNodeSelected, (TagEntity? t) => selectedTag = t));

        diagram.SelectModel(node, unselectOthers: true);

        Assert.NotNull(selectedTag);
        Assert.Equal(1, selectedTag.Id);
    }

    [Fact]
    public void TagNodeWidget_RendersTagTreeButton_AndTogglesPopover()
    {
        var diagram = new BlazorDiagram();
        var tag = new TagEntity { Id = 10, Name = "TreeTargetTag", OwnerId = "user1" };
        var allTags = new List<TagEntity> { tag };
        var node = new TagNode(tag, new Point(20, 20))
        {
            AllTags = allTags
        };
        diagram.Nodes.Add(node);

        var cut = _ctx.Render<TagNodeWidget>(parameters => parameters
            .Add(p => p.Node, node)
            .AddCascadingValue(diagram));

        // Verify the AccountTree icon button is rendered
        var treeBtn = cut.Find("button.tag-tree-button");
        Assert.NotNull(treeBtn);

        // Click the tree button to toggle popover
        treeBtn.Click();

        // Popover content should be displayed
        Assert.Contains("TreeTargetTag", cut.Markup);
    }

    [Fact]
    public void TagNodeWidget_TreePopover_TagClick_InvokesRequestFocusTag()
    {
        var diagram = new BlazorDiagram();
        var parentTag = new TagEntity { Id = 10, Name = "ParentTag", OwnerId = "user1" };
        var targetTag = new TagEntity { Id = 20, Name = "TargetTag", ParentTagId = 10, OwnerId = "user1" };
        var allTags = new List<TagEntity> { parentTag, targetTag };
        int? requestedTagId = null;
        var node = new TagNode(targetTag, new Point(20, 20))
        {
            AllTags = allTags,
            RequestFocusTag = id => requestedTagId = id
        };
        diagram.Nodes.Add(node);

        var cut = _ctx.Render<TagNodeWidget>(parameters => parameters
            .Add(p => p.Node, node)
            .AddCascadingValue(diagram));

        // Open popover
        var treeBtn = cut.Find("button.tag-tree-button");
        treeBtn.Click();

        // Wait for popover provider to render the tree link
        _popoverProvider.WaitForState(() => _popoverProvider.FindAll("a.mud-link").Any(l => l.TextContent.Contains("ParentTag")));
        var parentLink = _popoverProvider.FindAll("a.mud-link").First(l => l.TextContent.Contains("ParentTag"));
        parentLink.Click();

        // Assert: RequestFocusTag was invoked with parentTag.Id
        Assert.Equal(10, requestedTagId);
    }

    [Fact]
    public void TagNodeWidget_RendersShowChildrenButton_AndInvokesCallback()
    {
        var diagram = new BlazorDiagram();
        var parentTag = new TagEntity { Id = 10, Name = "ParentTag", OwnerId = "user1" };
        var childTag = new TagEntity { Id = 20, Name = "ChildTag", ParentTagId = 10, OwnerId = "user1" };
        var allTags = new List<TagEntity> { parentTag, childTag };
        TagEntity? requestedParent = null;

        var node = new TagNode(parentTag, new Point(20, 20))
        {
            AllTags = allTags,
            RequestShowChildNodes = tag => requestedParent = tag
        };
        diagram.Nodes.Add(node);

        var cut = _ctx.Render<TagNodeWidget>(parameters => parameters
            .Add(p => p.Node, node)
            .AddCascadingValue(diagram));

        // Verify the SubdirectoryArrowRight button is rendered and enabled
        var showChildrenBtn = cut.Find("button.tag-show-children-button");
        Assert.NotNull(showChildrenBtn);
        Assert.False(showChildrenBtn.HasAttribute("disabled"));

        // Click the button
        showChildrenBtn.Click();

        // Assert: callback was invoked with parentTag
        Assert.NotNull(requestedParent);
        Assert.Equal(10, requestedParent.Id);
    }

    [Fact]
    public void TagNodeWidget_ShowChildrenButton_IsDisabled_WhenNoChildren()
    {
        var diagram = new BlazorDiagram();
        var leafTag = new TagEntity { Id = 10, Name = "LeafTag", OwnerId = "user1" };
        var allTags = new List<TagEntity> { leafTag };

        var node = new TagNode(leafTag, new Point(20, 20))
        {
            AllTags = allTags
        };
        diagram.Nodes.Add(node);

        var cut = _ctx.Render<TagNodeWidget>(parameters => parameters
            .Add(p => p.Node, node)
            .AddCascadingValue(diagram));

        // Verify the button is disabled
        var showChildrenBtn = cut.Find("button.tag-show-children-button");
        Assert.NotNull(showChildrenBtn);
        Assert.True(showChildrenBtn.HasAttribute("disabled"));
    }
}