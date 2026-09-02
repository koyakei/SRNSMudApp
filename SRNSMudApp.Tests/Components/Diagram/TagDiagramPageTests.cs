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
        var authContext = _ctx.AddAuthorization();
        authContext.SetAuthorized(TestUserId);
        authContext.SetClaims(new Claim(ClaimTypes.NameIdentifier, TestUserId));
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

    [Fact]
    public void TagDiagramPage_DisplaysChildNodesInDiagram_WhenRequestShowChildNodesInvoked()
    {
        // Arrange
        var parentTag = new TagEntity { Id = 1, Name = "Parent", OwnerId = TestUserId, CachedWeight = 10 };
        var otherTag = new TagEntity { Id = 2, Name = "Other", OwnerId = TestUserId, CachedWeight = 5 };
        var childTag = new TagEntity { Id = 3, Name = "Child", OwnerId = TestUserId, ParentTagId = 1, CachedWeight = 2 };
        var edge = new TagEdge { Id = 101, SourceTagId = 1, TargetTagId = 2, OwnerId = TestUserId, SourceTag = parentTag, TargetTag = otherTag };

        _ = _dataProviderMock.Setup(p => p.LoadAllTagsAsync()).ReturnsAsync([parentTag, otherTag, childTag]);
        _ = _dataProviderMock.Setup(p => p.LoadAllEdgesAsync()).ReturnsAsync([edge]);

        // Act
        var cut = _ctx.Render<TagDiagramPage>();
        cut.WaitForState(() => cut.Markup.Contains("Tag Edge Diagram"));

        var canvas = cut.FindComponent<TagDiagramCanvas>();
        var diagram = canvas.Instance.Diagram;

        // 初期状態: エッジを持つ parentTag, otherTag のみ表示され、childTag は表示されない
        Assert.Equal(2, diagram.Nodes.Count);
        Assert.DoesNotContain(diagram.Nodes.OfType<TagNode>(), n => n.Tag.Id == 3);

        var node1 = diagram.Nodes.OfType<TagNode>().First(n => n.Tag.Id == 1);
        Assert.NotNull(node1.RequestShowChildNodes);
        Assert.Equal(1, node1.ChildCount);

        // node1 の子タグ表示コールバックを呼び出す
        cut.InvokeAsync(() => node1.RequestShowChildNodes(parentTag));

        // Assert: childTag がダイアグラムに追加され、ノード数が 3 になること
        cut.WaitForState(() => diagram.Nodes.OfType<TagNode>().Any(n => n.Tag.Id == 3));
        Assert.Equal(3, diagram.Nodes.Count);

        var childNode = diagram.Nodes.OfType<TagNode>().First(n => n.Tag.Id == 3);
        Assert.Equal("Child", childNode.Tag.Name);

        // 親ノードがフォーカスされていること
        var updatedNode1 = diagram.Nodes.OfType<TagNode>().First(n => n.Tag.Id == 1);
        Assert.True(updatedNode1.IsFocused);
    }

    [Fact]
    public async Task TagDiagramPage_EntersEdgeCreationMode_PreservingFocus_AndExpandsChildren()
    {
        // Arrange
        var parentTag = new TagEntity { Id = 1, Name = "ParentTag", OwnerId = TestUserId, CachedWeight = 10 };
        var child1 = new TagEntity { Id = 2, Name = "ChildA", OwnerId = TestUserId, ParentTagId = 1, CachedWeight = 2 };
        var child2 = new TagEntity { Id = 3, Name = "ChildB", OwnerId = TestUserId, ParentTagId = 1, CachedWeight = 3 };
        var otherTag = new TagEntity { Id = 4, Name = "OtherTag", OwnerId = TestUserId, CachedWeight = 5 };
        var edge = new TagEdge { Id = 101, SourceTagId = 1, TargetTagId = 4, OwnerId = TestUserId, SourceTag = parentTag, TargetTag = otherTag };
        var asset = new RightAsset { Id = 201, OwnerId = TestUserId, TargetTagId = 1, Amount = 10 };

        _ = _dataProviderMock.Setup(p => p.LoadAllTagsAsync()).ReturnsAsync([parentTag, child1, child2, otherTag]);
        _ = _dataProviderMock.Setup(p => p.LoadAllEdgesAsync()).ReturnsAsync([edge]);
        _ = _dataProviderMock.Setup(p => p.GetAvailableRightAssetsAsync(TestUserId, 1)).ReturnsAsync([asset]);

        // Act
        var cut = _ctx.Render<TagDiagramPage>();
        cut.WaitForState(() => cut.Markup.Contains("Tag Edge Diagram"));

        var canvas = cut.FindComponent<TagDiagramCanvas>();
        var diagram = canvas.Instance.Diagram;

        // parentTag をフォーカス
        var parentNode = diagram.Nodes.OfType<TagNode>().First(n => n.Tag.Id == 1);
        await cut.InvokeAsync(() => parentNode.RequestFocusTag!(1));

        // 初期状態では子タグはエッジを持たないため非表示 (ノード数は 2: parentTag と otherTag)
        cut.WaitForState(() => cut.Markup.Contains("① 始点"));
        Assert.Equal(2, diagram.Nodes.Count);

        // 「エッジ作成モード」ボタンをクリック
        var edgeModeButton = cut.FindAll("button").First(b => b.TextContent.Contains("エッジ作成モード"));
        await cut.InvokeAsync(() => edgeModeButton.Click());

        // Assert: エッジ作成モードパネルが表示され、子タグ2件が画面上に展開されること
        cut.WaitForState(() => cut.Markup.Contains("エッジ作成中"));
        Assert.Contains("ParentTag」の子タグ (2 件)", cut.Markup);
        Assert.Contains("ChildA", cut.Markup);
        Assert.Contains("ChildB", cut.Markup);

        // 子タグがダイアグラム上に展開され、ノード数が 4 になっていること
        Assert.Equal(4, diagram.Nodes.Count);
        Assert.Contains(diagram.Nodes.OfType<TagNode>(), n => n.Tag.Id == 2);
        Assert.Contains(diagram.Nodes.OfType<TagNode>(), n => n.Tag.Id == 3);

        // 親タグのフォーカスが維持されていること
        var updatedParentNode = diagram.Nodes.OfType<TagNode>().First(n => n.Tag.Id == 1);
        Assert.True(updatedParentNode.IsFocused);
    }

    [Fact]
    public async Task TagDiagramPage_SelectsNodesInEdgeCreationMode_WithoutChangingPageFocus()
    {
        // Arrange
        var parentTag = new TagEntity { Id = 1, Name = "ParentTag", OwnerId = TestUserId, CachedWeight = 10 };
        var child1 = new TagEntity { Id = 2, Name = "ChildA", OwnerId = TestUserId, ParentTagId = 1, CachedWeight = 2 };
        var child2 = new TagEntity { Id = 3, Name = "ChildB", OwnerId = TestUserId, ParentTagId = 1, CachedWeight = 3 };
        var otherTag = new TagEntity { Id = 4, Name = "OtherTag", OwnerId = TestUserId, CachedWeight = 5 };
        var edge = new TagEdge { Id = 101, SourceTagId = 1, TargetTagId = 4, OwnerId = TestUserId, SourceTag = parentTag, TargetTag = otherTag };

        _ = _dataProviderMock.Setup(p => p.LoadAllTagsAsync()).ReturnsAsync([parentTag, child1, child2, otherTag]);
        _ = _dataProviderMock.Setup(p => p.LoadAllEdgesAsync()).ReturnsAsync([edge]);
        _ = _dataProviderMock.Setup(p => p.GetAvailableRightAssetsAsync(TestUserId, 1)).ReturnsAsync([]);

        var cut = _ctx.Render<TagDiagramPage>();
        cut.WaitForState(() => cut.Markup.Contains("Tag Edge Diagram"));

        var canvas = cut.FindComponent<TagDiagramCanvas>();
        var diagram = canvas.Instance.Diagram;

        // まず親タグをフォーカス
        var parentNode = diagram.Nodes.OfType<TagNode>().First(n => n.Tag.Id == 1);
        await cut.InvokeAsync(() => parentNode.RequestFocusTag!(1));
        cut.WaitForState(() => cut.Markup.Contains("① 始点"));

        // エッジ作成モードを開始
        var edgeModeButton = cut.FindAll("button").First(b => b.TextContent.Contains("エッジ作成モード"));
        await cut.InvokeAsync(() => edgeModeButton.Click());
        cut.WaitForState(() => cut.Markup.Contains("エッジ作成中"));

        // ダイアグラム上で child1 を選択 -> From にセットされる
        await cut.InvokeAsync(() => canvas.Instance.OnNodeSelected.InvokeAsync(child1));

        // 続けて child2 を選択 -> To にセットされる
        await cut.InvokeAsync(() => canvas.Instance.OnNodeSelected.InvokeAsync(child2));

        // Assert: ページ全体のフォーカスは親タグ (ParentTag) のまま維持されていること
        Assert.Contains("① 始点", cut.Markup);
        Assert.Contains("ParentTag", cut.Markup);

        // エッジ作成パネル内で From が ChildA、To が ChildB に設定されていること
        cut.WaitForState(() => cut.Markup.Contains("① ChildA") && cut.Markup.Contains("② ChildB"));
    }

    [Fact]
    public async Task TagDiagramPage_CreatesEdgeAndAttachesTag_InEdgeCreationMode()
    {
        // Arrange
        var parentTag = new TagEntity { Id = 1, Name = "ParentTag", OwnerId = TestUserId, CachedWeight = 10 };
        var child1 = new TagEntity { Id = 2, Name = "ChildA", OwnerId = TestUserId, ParentTagId = 1, CachedWeight = 2 };
        var child2 = new TagEntity { Id = 3, Name = "ChildB", OwnerId = TestUserId, ParentTagId = 1, CachedWeight = 3 };
        var otherTag = new TagEntity { Id = 4, Name = "OtherTag", OwnerId = TestUserId, CachedWeight = 5 };
        var edge = new TagEdge { Id = 101, SourceTagId = 1, TargetTagId = 4, OwnerId = TestUserId, SourceTag = parentTag, TargetTag = otherTag };
        var asset = new RightAsset { Id = 201, OwnerId = TestUserId, TargetTagId = 1, Amount = 10 };
        var createdEdge = new TagEdge { Id = 999, SourceTagId = 2, TargetTagId = 3, OwnerId = TestUserId, SourceTag = child1, TargetTag = child2 };
        var createdAttachment = new TagEdgeTagAttachment { Id = 501, TagEdgeId = 999, TagId = 1, OwnerId = TestUserId, Weight = 1 };

        _ = _dataProviderMock.Setup(p => p.LoadAllTagsAsync()).ReturnsAsync([parentTag, child1, child2, otherTag]);
        _ = _dataProviderMock.Setup(p => p.LoadAllEdgesAsync()).ReturnsAsync([edge]);
        _ = _dataProviderMock.Setup(p => p.GetAvailableRightAssetsAsync(TestUserId, 1)).ReturnsAsync([asset]);
        _ = _dataProviderMock.Setup(p => p.CreateEdgeAsync(2, 3, TestUserId))
            .ReturnsAsync(new SRNSMudApp.Models.Unions.Success<TagEdge>(createdEdge));
        _ = _dataProviderMock.Setup(p => p.AttachTagToEdgeAsync(999, 1, 201, TestUserId, 1))
            .ReturnsAsync(new SRNSMudApp.Models.Unions.Success<TagEdgeTagAttachment>(createdAttachment));

        var cut = _ctx.Render<TagDiagramPage>();
        cut.WaitForState(() => cut.Markup.Contains("Tag Edge Diagram"));

        var canvas = cut.FindComponent<TagDiagramCanvas>();
        var diagram = canvas.Instance.Diagram;

        // 親タグをフォーカス
        var parentNode = diagram.Nodes.OfType<TagNode>().First(n => n.Tag.Id == 1);
        await cut.InvokeAsync(() => parentNode.RequestFocusTag!(1));
        cut.WaitForState(() => cut.Markup.Contains("① 始点"));

        // エッジ作成モードを開始
        var edgeModeButton = cut.FindAll("button").First(b => b.TextContent.Contains("エッジ作成モード"));
        await cut.InvokeAsync(() => edgeModeButton.Click());
        cut.WaitForState(() => cut.Markup.Contains("エッジ作成中"));

        // ダイアグラム上で child1, child2 を選択
        await cut.InvokeAsync(() => canvas.Instance.OnNodeSelected.InvokeAsync(child1));
        await cut.InvokeAsync(() => canvas.Instance.OnNodeSelected.InvokeAsync(child2));

        // 「決定（エッジを作成）」ボタンをクリック
        cut.WaitForState(() => cut.FindAll("button").Any(b => b.TextContent.Contains("決定（エッジを作成）") && !b.HasAttribute("disabled")));
        var submitButton = cut.FindAll("button").First(b => b.TextContent.Contains("決定（エッジを作成）"));

        await cut.InvokeAsync(() => submitButton.Click());

        // Assert: CreateEdgeAsync(2, 3) と AttachTagToEdgeAsync(999, 1, 201) が呼ばれること
        cut.WaitForAssertion(() =>
        {
            _dataProviderMock.Verify(p => p.CreateEdgeAsync(2, 3, TestUserId), Times.Once);
            _dataProviderMock.Verify(p => p.AttachTagToEdgeAsync(999, 1, 201, TestUserId, 1), Times.Once);
        });
    }

    [Fact]
    public void TagDiagramPage_RendersEdgesWithDirectionArrow_AndDirectionLabels()
    {
        // Arrange
        var tag1 = new TagEntity { Id = 1, Name = "SourceTag", OwnerId = TestUserId, CachedWeight = 5 };
        var tag2 = new TagEntity { Id = 2, Name = "TargetTag", OwnerId = TestUserId, CachedWeight = 3 };
        var edge = new TagEdge { Id = 101, SourceTagId = 1, TargetTagId = 2, OwnerId = TestUserId, SourceTag = tag1, TargetTag = tag2 };

        _ = _dataProviderMock.Setup(p => p.LoadAllTagsAsync()).ReturnsAsync([tag1, tag2]);
        _ = _dataProviderMock.Setup(p => p.LoadAllEdgesAsync()).ReturnsAsync([edge]);

        // Act
        var cut = _ctx.Render<TagDiagramPage>();
        cut.WaitForState(() => cut.Markup.Contains("Tag Edge Diagram"));

        var canvas = cut.FindComponent<TagDiagramCanvas>();
        var diagram = canvas.Instance.Diagram;

        // Assert: エッジに対応する TagEdgeLink が存在し、方向矢印マーカーが設定され、余分な矢印ラベルがないこと
        var link = diagram.Links.OfType<TagEdgeLink>().FirstOrDefault(l => l.Edge.Id == 101);
        Assert.NotNull(link);
        Assert.Same(TagEdgeLink.DirectionArrow, link.TargetMarker);
        Assert.Empty(link.Labels);

        // 1/3 と 2/3 の位置に中間方向矢印が描画されていること
        var arrows = cut.Find("g.diagram-link-intermediate-arrows");
        Assert.NotNull(arrows);
        Assert.Equal(2, arrows.QuerySelectorAll("path").Length);
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }
}