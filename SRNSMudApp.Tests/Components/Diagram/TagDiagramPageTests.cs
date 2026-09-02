using System.Security.Claims;

using Blazor.Diagrams.Core.Geometry;

using Bunit;

using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Moq;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.Diagram;
using SRNSMudApp.Components.Pages;
using SRNSMudApp.Components.Tag;
using SRNSMudApp.Data;
using SRNSMudApp.Services;
using SRNSMudApp.Services.Dialogs;

using TagEntity = SRNSMudApp.Data.Tag;

namespace SRNSMudApp.Tests.Components.Diagram;

public sealed class TagDiagramPageTests : IAsyncDisposable
{
    private const string TestUserId = "diagram-user-1";
    private readonly BunitContext _ctx = new();
    private readonly Mock<ITagDiagramDataProvider> _dataProviderMock = new();
    private readonly Mock<IDialogLauncher> _dialogLauncherMock = new();

    public TagDiagramPageTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices().AddMockSrnsServices();
        _ = _ctx.Services.AddScoped(_ => _dataProviderMock.Object);
        _ctx.Services.RemoveAll<IDialogLauncher>();
        _ = _ctx.Services.AddScoped(_ => _dialogLauncherMock.Object);
        _ctx.Services.AddAuth(TestUserId);
        _ = _ctx.Render<MudPopoverProvider>();
        _ctx.JSInterop.Setup<Rectangle>(invocation => invocation.Identifier.Contains("getBoundingClientRect"))
            .SetResult(new Rectangle(0, 0, 800, 600));
    }

    [Fact]
    public void TagDiagramPage_FocusesAndCentersNode_WhenNodeRequestFocusTagInvoked()
    {
        // Arrange
        var tag1 = new TagEntity { Id = 1, Name = "Alpha", OwnerId = TestUserId, CachedWeight = 5 };
        var tag2 = new TagEntity { Id = 2, Name = "Beta", OwnerId = TestUserId, CachedWeight = 3 };
        var edge = new TagEdge { Id = 101, SourceTagId = 1, TargetTagId = 2, OwnerId = TestUserId, SourceTag = tag1, TargetTag = tag2 };

        _ = _dataProviderMock.Setup(p => p.LoadAllTagsAsync()).ReturnsAsync([tag1, tag2]);
        _ = _dataProviderMock.Setup(p => p.LoadAllEdgesAsync()).ReturnsAsync([edge]);

        // Act
        var cut = _ctx.Render<TagDiagramPage>();
        cut.WaitForState(() => cut.Markup.Contains("Tag Edge Diagram"));

        var canvas = cut.FindComponent<TagDiagramCanvas>();
        var diagram = canvas.Instance.Diagram;

        // ノードが2つ作成されていることを確認
        var node1 = diagram.Nodes.OfType<TagNode>().FirstOrDefault(n => n.Tag.Id == 1);
        var node2 = diagram.Nodes.OfType<TagNode>().FirstOrDefault(n => n.Tag.Id == 2);
        Assert.NotNull(node1);
        Assert.NotNull(node2);
        Assert.NotNull(node1.RequestFocusTag);

        // node2 のツリーから tag1 (Alpha) がクリックされたことを想定し RequestFocusTag を呼び出す
        cut.InvokeAsync(() => node2.RequestFocusTag(1));

        // Assert: node1 がフォーカス状態・選択状態になり、パンが計算されて中央に寄せられていること
        cut.WaitForState(() => diagram.Nodes.OfType<TagNode>().Any(n => n.Tag.Id == 1 && n.IsFocused));
        var updatedNode1 = diagram.Nodes.OfType<TagNode>().First(n => n.Tag.Id == 1);
        var updatedNode2 = diagram.Nodes.OfType<TagNode>().First(n => n.Tag.Id == 2);

        Assert.True(updatedNode1.IsFocused);
        Assert.False(updatedNode2.IsFocused);
        Assert.True(updatedNode1.Selected);

        // node1 は X=80, Width=160 (center=160)。画面幅 800 の中央 (400) に配置するため panX = 400 - 160 = 240
        Assert.Equal(240, diagram.Pan.X);
        Assert.NotEqual(0, diagram.Pan.Y);
    }

    [Fact]
    public void TagDiagramPage_FocusesDualUnconnectedNodes_AndIncludesThemWhenOnlyConnectedTagsIsTrue()
    {
        // Arrange: tag1-tag2 are connected by an edge. tag3 and tag4 are unconnected.
        var tag1 = new TagEntity { Id = 1, Name = "Connected1", OwnerId = TestUserId, CachedWeight = 5 };
        var tag2 = new TagEntity { Id = 2, Name = "Connected2", OwnerId = TestUserId, CachedWeight = 3 };
        var tag3 = new TagEntity { Id = 3, Name = "UnconnectedA", OwnerId = TestUserId, CachedWeight = 1 };
        var tag4 = new TagEntity { Id = 4, Name = "UnconnectedB", OwnerId = TestUserId, CachedWeight = 1 };
        var edge = new TagEdge { Id = 101, SourceTagId = 1, TargetTagId = 2, OwnerId = TestUserId, SourceTag = tag1, TargetTag = tag2 };

        _ = _dataProviderMock.Setup(p => p.LoadAllTagsAsync()).ReturnsAsync([tag1, tag2, tag3, tag4]);
        _ = _dataProviderMock.Setup(p => p.LoadAllEdgesAsync()).ReturnsAsync([edge]);

        // Act
        var cut = _ctx.Render<TagDiagramPage>();
        cut.WaitForState(() => cut.Markup.Contains("Tag Edge Diagram"));

        var canvas = cut.FindComponent<TagDiagramCanvas>();
        var diagram = canvas.Instance.Diagram;

        // 初期状態: エッジを持つ tag1, tag2 のみがダイアグラムに含まれる
        Assert.Equal(2, diagram.Nodes.Count);
        Assert.DoesNotContain(diagram.Nodes.OfType<TagNode>(), n => n.Tag.Id == 3);
        Assert.DoesNotContain(diagram.Nodes.OfType<TagNode>(), n => n.Tag.Id == 4);

        var node1 = diagram.Nodes.OfType<TagNode>().First(n => n.Tag.Id == 1);

        // 1つ目の未接続タグ (tag3) をフォーカス (Source として追加される)
        cut.InvokeAsync(() => node1.RequestFocusTag!(3));

        // tag3 が例外的に含まれ、ノード数が3になる
        cut.WaitForState(() => diagram.Nodes.OfType<TagNode>().Any(n => n.Tag.Id == 3));
        var node3 = diagram.Nodes.OfType<TagNode>().First(n => n.Tag.Id == 3);
        Assert.Equal(TagFocusRole.Source, node3.FocusRole);

        // 2つ目の未接続タグ (tag4) をフォーカス (Target として追加される)
        cut.InvokeAsync(() => node3.RequestFocusTag!(4));

        // tag3 と tag4 の双方が含まれ、ノード数が4になる
        cut.WaitForState(() => diagram.Nodes.OfType<TagNode>().Any(n => n.Tag.Id == 4));
        Assert.Equal(4, diagram.Nodes.Count);

        var finalNode3 = diagram.Nodes.OfType<TagNode>().First(n => n.Tag.Id == 3);
        var finalNode4 = diagram.Nodes.OfType<TagNode>().First(n => n.Tag.Id == 4);

        Assert.Equal(TagFocusRole.Source, finalNode3.FocusRole);
        Assert.Equal(TagFocusRole.Target, finalNode4.FocusRole);

        // サマリーバーに2タグ間の Edge 作成ボタンが表示されていること
        Assert.Contains("この2つのタグ間に Edge を作成", cut.Markup);

        // 入れ替えボタンをクリックして、Source と Target が入れ替わることを確認
        var swapButton = cut.FindAll("button").FirstOrDefault(b => b.GetAttribute("title") == "入れ替え");
        Assert.NotNull(swapButton);
        swapButton.Click();

        var swappedNode3 = diagram.Nodes.OfType<TagNode>().First(n => n.Tag.Id == 3);
        var swappedNode4 = diagram.Nodes.OfType<TagNode>().First(n => n.Tag.Id == 4);

        Assert.Equal(TagFocusRole.Target, swappedNode3.FocusRole);
        Assert.Equal(TagFocusRole.Source, swappedNode4.FocusRole);
    }

    [Fact]
    public async Task TagDiagramPage_AssignsRequestAddChildTag_AndOpensTagAddDialogOnInvocation()
    {
        // Arrange
        var tag1 = new TagEntity { Id = 1, Name = "Alpha", OwnerId = TestUserId, CachedWeight = 5 };
        var edge = new TagEdge { Id = 101, SourceTagId = 1, TargetTagId = 1, OwnerId = TestUserId, SourceTag = tag1, TargetTag = tag1 };

        _ = _dataProviderMock.Setup(p => p.LoadAllTagsAsync()).ReturnsAsync([tag1]);
        _ = _dataProviderMock.Setup(p => p.LoadAllEdgesAsync()).ReturnsAsync([edge]);

        var dialogRefMock = new Mock<IDialogReference>();
        var newTag = new TagEntity { Id = 2, Name = "AlphaChild", OwnerId = TestUserId, ParentTagId = 1 };
        _ = dialogRefMock.Setup(r => r.Result).ReturnsAsync(DialogResult.Ok(newTag));

        _ = _dialogLauncherMock
            .Setup(l => l.ShowAsync(
                typeof(TagAddDialog),
                "子タグの追加",
                It.IsAny<DialogParameters>(),
                It.IsAny<DialogOptions>()))
            .ReturnsAsync(dialogRefMock.Object);

        // Act
        var cut = _ctx.Render<TagDiagramPage>();
        cut.WaitForState(() => cut.Markup.Contains("Tag Edge Diagram"));

        var canvas = cut.FindComponent<TagDiagramCanvas>();
        var diagram = canvas.Instance.Diagram;

        var node1 = diagram.Nodes.OfType<TagNode>().FirstOrDefault(n => n.Tag.Id == 1);
        Assert.NotNull(node1);
        Assert.NotNull(node1.RequestAddChildTag);

        await cut.InvokeAsync(() => node1.RequestAddChildTag!(tag1));

        // Assert: TagAddDialog が子タグの追加タイトルで起動され、再読み込みが走ること
        _dialogLauncherMock.Verify(
            l => l.ShowAsync(
                typeof(TagAddDialog),
                "子タグの追加",
                It.Is<DialogParameters>(dp => dp.Get<TagEntity>(nameof(TagAddDialog.DefaultParentTag)) == tag1),
                It.IsAny<DialogOptions>()),
            Times.Once);

        // LoadAllTagsAsync が再読み込みで2回呼ばれていること (初期ロード + 作成後リロード)
        _dataProviderMock.Verify(p => p.LoadAllTagsAsync(), Times.Exactly(2));
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }
}