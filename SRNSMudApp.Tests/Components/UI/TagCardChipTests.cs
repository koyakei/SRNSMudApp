using Bunit;

using Microsoft.AspNetCore.Components.Web;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.UI;
using SRNSMudApp.Data;

#pragma warning disable MUD0012

namespace SRNSMudApp.Tests.Components.UI;

using Tag = SRNSMudApp.Data.Tag;

public class TagCardChipTests : IAsyncDisposable
{
    private readonly BunitContext _ctx = new();

    public TagCardChipTests()
    {
        _ = _ctx.Services.AddMudServices().AddSrnsComponentServices();
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Render<MudPopoverProvider>();
    }

    public async ValueTask DisposeAsync() => await _ctx.DisposeAsync();

    [Fact]
    public void TagCardChip_WhenNotActive_OverlayIsNotVisible()
    {
        var tag = new Tag { Id = 1, Name = "TestTag", OwnerId = "u1" };
        var relation = new TagRelationToTag { Id = 10, TagId = 1, Tag = tag, TargetTagId = 99, OwnerId = "u1", Weight = 1 };
        var display = new TagCardChipDisplayInfo
        {
            BackgroundColor = "#fff",
            TextColor = "#000",
            DisplayWeight = "1",
            AddButtonColor = Color.Default,
            IsDeleted = false
        };

        IRenderedComponent<TagCardChip> cut = _ctx.Render<TagCardChip>(parameters => parameters
            .Add(p => p.Relation, relation)
            .Add(p => p.Display, display)
            .Add(p => p.IsActive, false)
        );

        var overlays = cut.FindComponents<MudOverlay>();
        Assert.All(overlays, o => Assert.False(o.Instance.Visible));
    }

    [Fact]
    public async Task TagCardChip_WhenActive_OverlayIsVisible_AndOnClickTriggersOnCloseTree()
    {
        var tag = new Tag { Id = 1, Name = "TestTag", OwnerId = "u1" };
        var relation = new TagRelationToTag { Id = 10, TagId = 1, Tag = tag, TargetTagId = 99, OwnerId = "u1", Weight = 1 };
        var display = new TagCardChipDisplayInfo
        {
            BackgroundColor = "#fff",
            TextColor = "#000",
            DisplayWeight = "1",
            AddButtonColor = Color.Default,
            IsDeleted = false
        };
        var closeTreeCalled = false;

        IRenderedComponent<TagCardChip> cut = _ctx.Render<TagCardChip>(parameters => parameters
            .Add(p => p.Relation, relation)
            .Add(p => p.Display, display)
            .Add(p => p.IsActive, true)
            .Add(p => p.AllTags, [tag])
            .Add(p => p.OnCloseTree, () => { closeTreeCalled = true; })
        );

        var overlay = cut.FindComponent<MudOverlay>();
        Assert.True(overlay.Instance.Visible);
        await cut.InvokeAsync(() => overlay.Instance.OnClick.InvokeAsync(new MouseEventArgs()));

        Assert.True(closeTreeCalled);
    }
}