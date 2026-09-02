using Bunit;

using Microsoft.Extensions.DependencyInjection;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.Diagram;
using SRNSMudApp.Components.Tag;
using SRNSMudApp.Data;

using TagEntity = SRNSMudApp.Data.Tag;

namespace SRNSMudApp.Tests.Components.Diagram;

public class TagEdgeInspectorTests : IAsyncDisposable
{
    private readonly BunitContext _ctx = new();
    private readonly IRenderedComponent<MudPopoverProvider> _popoverProvider;

    public TagEdgeInspectorTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices();
        _popoverProvider = _ctx.Render<MudPopoverProvider>();
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }

    [Fact]
    public void WhenEdgeIsNull_RendersNoEdgeSelectedMessage()
    {
        var cut = _ctx.Render<TagEdgeInspector>(parameters => parameters
            .Add(p => p.Edge, null));

        Assert.Contains("エッジが選択されていません。", cut.Markup);
    }

    [Fact]
    public void WhenEdgeIsProvided_RendersSourceAndTargetNames()
    {
        var edge = new TagEdge
        {
            Id = 42,
            OwnerId = "owner-1",
            SourceTag = new TagEntity { Id = 1, Name = "SourceAlpha", OwnerId = "owner-1" },
            TargetTag = new TagEntity { Id = 2, Name = "TargetBeta", OwnerId = "owner-1" },
            TagAttachments = []
        };

        var cut = _ctx.Render<TagEdgeInspector>(parameters => parameters
            .Add(p => p.Edge, edge)
            .Add(p => p.CurrentUserId, "owner-1"));

        Assert.Contains("SourceAlpha", cut.Markup);
        Assert.Contains("TargetBeta", cut.Markup);
        Assert.Contains("Edge ID: 42", cut.Markup);
    }

    [Fact]
    public void WhenDeleteEdgeClicked_InvokesCallback()
    {
        var edge = new TagEdge
        {
            Id = 42,
            OwnerId = "owner-1",
            SourceTag = new TagEntity { Id = 1, Name = "SourceAlpha", OwnerId = "owner-1" },
            TargetTag = new TagEntity { Id = 2, Name = "TargetBeta", OwnerId = "owner-1" },
            TagAttachments = []
        };

        TagEdge? deletedEdge = null;
        var cut = _ctx.Render<TagEdgeInspector>(parameters => parameters
            .Add(p => p.Edge, edge)
            .Add(p => p.CurrentUserId, "owner-1")
            .Add(p => p.OnDeleteEdge, (TagEdge e) => deletedEdge = e));

        var deleteBtn = cut.FindAll("button").FirstOrDefault(b => b.TextContent.Contains("Edge を削除"));
        Assert.NotNull(deleteBtn);
        deleteBtn.Click();

        Assert.NotNull(deletedEdge);
        Assert.Equal(42, deletedEdge.Id);
    }

    [Fact]
    public void WhenAttachTagClicked_InvokesCallback()
    {
        var edge = new TagEdge
        {
            Id = 42,
            OwnerId = "owner-1",
            SourceTag = new TagEntity { Id = 1, Name = "SourceAlpha", OwnerId = "owner-1" },
            TargetTag = new TagEntity { Id = 2, Name = "TargetBeta", OwnerId = "owner-1" },
            TagAttachments = []
        };

        TagEdge? targetEdge = null;
        var cut = _ctx.Render<TagEdgeInspector>(parameters => parameters
            .Add(p => p.Edge, edge)
            .Add(p => p.CurrentUserId, "owner-1")
            .Add(p => p.OnAttachTag, (TagEdge e) => targetEdge = e));

        var attachBtn = cut.FindAll("button").FirstOrDefault(b => b.TextContent.Contains("タグ紐付け"));
        Assert.NotNull(attachBtn);
        attachBtn.Click();

        Assert.NotNull(targetEdge);
        Assert.Equal(42, targetEdge.Id);
    }

    [Fact]
    public void WhenAttachmentsExist_RendersAttachmentsAndInvokesDetachCallback()
    {
        var attachment = new TagEdgeTagAttachment
        {
            Id = 101,
            TagEdgeId = 42,
            TagId = 10,
            Tag = new TagEntity { Id = 10, Name = "MeaningTag", OwnerId = "owner-1" },
            Weight = 3,
            OwnerId = "owner-1"
        };

        var edge = new TagEdge
        {
            Id = 42,
            OwnerId = "owner-1",
            SourceTag = new TagEntity { Id = 1, Name = "SourceAlpha", OwnerId = "owner-1" },
            TargetTag = new TagEntity { Id = 2, Name = "TargetBeta", OwnerId = "owner-1" },
            TagAttachments = [attachment]
        };

        TagEdgeTagAttachment? detached = null;
        var cut = _ctx.Render<TagEdgeInspector>(parameters => parameters
            .Add(p => p.Edge, edge)
            .Add(p => p.CurrentUserId, "owner-1")
            .Add(p => p.OnDetachTag, (TagEdgeTagAttachment a) => detached = a));

        Assert.Contains("MeaningTag", cut.Markup);
        Assert.Contains("W: 3", cut.Markup);

        var detachBtn = cut.FindAll("button").FirstOrDefault(b => b.GetAttribute("title") == "紐付け解除");
        Assert.NotNull(detachBtn);
        detachBtn.Click();

        Assert.NotNull(detached);
        Assert.Equal(101, detached.Id);
    }

    [Fact]
    public void WhenTreeButtonClicked_TogglesTreePopover()
    {
        var sourceTag = new TagEntity { Id = 1, Name = "SourceAlpha", OwnerId = "owner-1" };
        var targetTag = new TagEntity { Id = 2, Name = "TargetBeta", OwnerId = "owner-1" };
        var allTags = new List<TagEntity> { sourceTag, targetTag };

        var edge = new TagEdge
        {
            Id = 42,
            OwnerId = "owner-1",
            SourceTag = sourceTag,
            TargetTag = targetTag,
            TagAttachments = []
        };

        var cut = _ctx.Render<TagEdgeInspector>(parameters => parameters
            .Add(p => p.Edge, edge)
            .Add(p => p.AllTags, allTags)
            .Add(p => p.CurrentUserId, "owner-1"));

        var treeButtons = cut.FindAll("button").Where(b => b.GetAttribute("title") == "タグツリーを表示").ToList();
        Assert.NotEmpty(treeButtons);

        // Click first tree button (SourceTag)
        treeButtons[0].Click();

        // Popover content should be open
        Assert.Contains("SourceAlpha", cut.Markup);
    }

    [Fact]
    public async Task WhenAddChildTagClickedInTree_InvokesOnAddChildTagCallback()
    {
        var sourceTag = new TagEntity { Id = 1, Name = "SourceAlpha", OwnerId = "owner-1" };
        var targetTag = new TagEntity { Id = 2, Name = "TargetBeta", OwnerId = "owner-1" };
        var allTags = new List<TagEntity> { sourceTag, targetTag };

        var edge = new TagEdge
        {
            Id = 42,
            OwnerId = "owner-1",
            SourceTag = sourceTag,
            TargetTag = targetTag,
            TagAttachments = []
        };

        TagEntity? addedChildParent = null;
        var cut = _ctx.Render<TagEdgeInspector>(parameters => parameters
            .Add(p => p.Edge, edge)
            .Add(p => p.AllTags, allTags)
            .Add(p => p.CurrentUserId, "owner-1")
            .Add(p => p.OnAddChildTag, (TagEntity t) => addedChildParent = t));

        var treeButtons = cut.FindAll("button").Where(b => b.GetAttribute("title") == "タグツリーを表示").ToList();
        Assert.NotEmpty(treeButtons);

        treeButtons[0].Click();

        _popoverProvider.WaitForState(() => _popoverProvider.FindComponents<TagTreePopoverContent>().Count > 0);
        var popover = _popoverProvider.FindComponent<TagTreePopoverContent>();
        Assert.True(popover.Instance.EnableAddChild);

        await _popoverProvider.InvokeAsync(() => popover.Instance.OnAddChildTag.InvokeAsync(sourceTag));
        Assert.Equal(sourceTag, addedChildParent);
    }
}