using Blazor.Diagrams;
using Blazor.Diagrams.Core.Geometry;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

using MudBlazor.Services;

using SRNSMudApp.Components.Diagram;
using SRNSMudApp.Data;

using TagEntity = SRNSMudApp.Data.Tag;

namespace SRNSMudApp.Tests.Components.Diagram;

public class TagDiagramCanvasTests : IAsyncDisposable
{
    private readonly BunitContext _ctx = new();

    public TagDiagramCanvasTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices();
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
}
