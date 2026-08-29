using Bunit;

using Microsoft.Extensions.DependencyInjection;

using MudBlazor.Services;

using SRNSMudApp.Components.Tag;
using SRNSMudApp.Tests.TestSupport;

using TagEntity = SRNSMudApp.Data.Tag;

namespace SRNSMudApp.Tests.Components.Tag;

public sealed class TagTreePopoverContentTests : IAsyncLifetime
{
    private readonly BunitContext _ctx = new();

    public TagTreePopoverContentTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices().AddMockSrnsServices();
        _ = _ctx.Services.AddAuth("user-1");
    }

    public Task InitializeAsync() => Task.CompletedTask;

    [Fact]
    public void TagTreePopoverContent_RendersScrollableContainerStyles()
    {
        // Arrange
        var rootTag = new TagEntity { Id = 1, Name = "Root", OwnerId = "user-1" };
        var child1 = new TagEntity { Id = 2, Name = "Child1", ParentTagId = 1, OwnerId = "user-1" };
        var child2 = new TagEntity { Id = 3, Name = "Child2", ParentTagId = 1, OwnerId = "user-1" };
        List<TagEntity> allTags = [rootTag, child1, child2];

        // Act
        IRenderedComponent<TagTreePopoverContent> cut = _ctx.Render<TagTreePopoverContent>(parameters => parameters
            .Add(p => p.TargetTag, child1)
            .Add(p => p.AllTags, allTags));

        // Assert: スクロール制御用スタイルが MudPaper に適用されていること
        Assert.Contains("overflow-y: auto", cut.Markup);
        Assert.Contains("overscroll-behavior: contain", cut.Markup);
        Assert.Contains("max-height: min(300px, 60vh)", cut.Markup);
        Assert.Contains("box-shadow", cut.Markup);

        // ツリーのすべての行が表示されていること（親・自身・兄弟）
        Assert.Contains("Root", cut.Markup);
        Assert.Contains("Child1", cut.Markup);
        Assert.Contains("Child2", cut.Markup);
    }

    [Fact]
    public void TagTreePopoverContent_SupportsCustomMaxHeightAndStyle()
    {
        // Arrange
        var rootTag = new TagEntity { Id = 1, Name = "Root", OwnerId = "user-1" };

        // Act
        IRenderedComponent<TagTreePopoverContent> cut = _ctx.Render<TagTreePopoverContent>(parameters => parameters
            .Add(p => p.TargetTag, rootTag)
            .Add(p => p.AllTags, new List<TagEntity> { rootTag })
            .Add(p => p.MaxHeight, "200px")
            .Add(p => p.Style, "min-width: 250px;"));

        // Assert
        Assert.Contains("max-height: 200px", cut.Markup);
        Assert.Contains("min-width: 250px", cut.Markup);
        Assert.Contains("overflow-y: auto", cut.Markup);
    }

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }
}