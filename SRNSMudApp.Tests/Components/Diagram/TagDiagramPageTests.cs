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

using ItemEntity = SRNSMudApp.Data.Item;
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
    public void TagDiagramPage_HidesNodeFromDiagram_AndRestoresIt()
    {
        // Arrange
        var tag1 = new TagEntity { Id = 1, Name = "TagAlpha", OwnerId = TestUserId, CachedWeight = 5 };
        var tag2 = new TagEntity { Id = 2, Name = "TagBeta", OwnerId = TestUserId, CachedWeight = 3 };
        var tag3 = new TagEntity { Id = 3, Name = "TagGamma", OwnerId = TestUserId, CachedWeight = 2 };
        var edge1 = new TagEdge { Id = 101, SourceTagId = 1, TargetTagId = 2, OwnerId = TestUserId, SourceTag = tag1, TargetTag = tag2 };
        var edge2 = new TagEdge { Id = 102, SourceTagId = 2, TargetTagId = 3, OwnerId = TestUserId, SourceTag = tag2, TargetTag = tag3 };

        _ = _dataProviderMock.Setup(p => p.LoadAllTagsAsync()).ReturnsAsync([tag1, tag2, tag3]);
        _ = _dataProviderMock.Setup(p => p.LoadAllEdgesAsync()).ReturnsAsync([edge1, edge2]);

        // Act
        var cut = _ctx.Render<TagDiagramPage>();
        cut.WaitForState(() => cut.Markup.Contains("Tag Edge Diagram"));

        var canvas = cut.FindComponent<TagDiagramCanvas>();
        var diagram = canvas.Instance.Diagram;

        // 初期状態: 3ノード表示、2エッジ表示
        Assert.Equal(3, diagram.Nodes.Count);
        Assert.Equal(2, diagram.Links.OfType<TagEdgeLink>().Count());
        var node1 = diagram.Nodes.OfType<TagNode>().First(n => n.Tag.Id == 1);
        Assert.NotNull(node1.RequestHideNode);

        // tag1 の画面表示から消すコールバックを呼び出す
        cut.InvokeAsync(() => node1.RequestHideNode(tag1));

        // Assert: tag1 がダイアグラムから消え、tag2 と tag3 (2ノード) のみ表示され、edge1 は消えて edge2 (1エッジ) のみ残る
        cut.WaitForState(() => diagram.Nodes.Count == 2);
        Assert.DoesNotContain(diagram.Nodes.OfType<TagNode>(), n => n.Tag.Id == 1);
        Assert.Contains(diagram.Nodes.OfType<TagNode>(), n => n.Tag.Id == 2);
        Assert.Contains(diagram.Nodes.OfType<TagNode>(), n => n.Tag.Id == 3);
        Assert.Single(diagram.Links.OfType<TagEdgeLink>());
        Assert.Equal(102, diagram.Links.OfType<TagEdgeLink>().First().Edge.Id);

        // 再表示ボタンが表示されていること
        cut.WaitForState(() => cut.FindAll("button.tag-restore-hidden-button").Count > 0);
        var restoreBtn = cut.Find("button.tag-restore-hidden-button");
        Assert.Contains("非表示 (1) を再表示", restoreBtn.TextContent);

        // 再表示ボタンをクリック
        cut.InvokeAsync(() => restoreBtn.Click());

        // Assert: 3ノードに戻り、2エッジも復元すること
        cut.WaitForState(() => diagram.Nodes.Count == 3);
        Assert.Contains(diagram.Nodes.OfType<TagNode>(), n => n.Tag.Id == 1);
        Assert.Contains(diagram.Nodes.OfType<TagNode>(), n => n.Tag.Id == 2);
        Assert.Contains(diagram.Nodes.OfType<TagNode>(), n => n.Tag.Id == 3);
        Assert.Equal(2, diagram.Links.OfType<TagEdgeLink>().Count());
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

    [Fact]
    public void TagDiagramPage_RendersItemNodesAndTagRelationLinks_WhenItemIdIsProvided()
    {
        // Arrange
        var tag1 = new TagEntity { Id = 1, Name = "SourceTag", OwnerId = TestUserId, CachedWeight = 5 };
        var parentItem = new ItemEntity
        {
            Id = 10,
            Content = "Parent item content",
            OwnerId = TestUserId,
            TagRelations = [new TagRelation { TagId = 1, ItemId = 10, OwnerId = TestUserId }]
        };
        var childItem = new ItemEntity
        {
            Id = 20,
            Content = "Child item content",
            OwnerId = TestUserId,
            TagRelations = [new TagRelation { TagId = 1, ItemId = 20, OwnerId = TestUserId }]
        };

        _ = _dataProviderMock.Setup(p => p.LoadAllTagsAsync()).ReturnsAsync([tag1]);
        _ = _dataProviderMock.Setup(p => p.LoadAllEdgesAsync()).ReturnsAsync([]);
        _ = _dataProviderMock.Setup(p => p.GetContextTagIdsForItemAsync(10)).ReturnsAsync([1]);
        _ = _dataProviderMock.Setup(p => p.GetContextItemsAsync(10)).ReturnsAsync([parentItem, childItem]);

        var nav = _ctx.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        nav.NavigateTo("http://localhost/tag-diagram?itemId=10");

        // Act
        var cut = _ctx.Render<TagDiagramPage>();
        cut.WaitForState(() => cut.Markup.Contains("Tag Edge Diagram"));

        var canvas = cut.FindComponent<TagDiagramCanvas>();
        var diagram = canvas.Instance.Diagram;

        // Assert: TagNode と 2つの ItemNode がダイアグラム上に存在すること
        var tagNode = diagram.Nodes.OfType<TagNode>().FirstOrDefault(n => n.Tag.Id == 1);
        var pItemNode = diagram.Nodes.OfType<ItemNode>().FirstOrDefault(n => n.Item.Id == 10);
        var cItemNode = diagram.Nodes.OfType<ItemNode>().FirstOrDefault(n => n.Item.Id == 20);

        Assert.NotNull(tagNode);
        Assert.NotNull(pItemNode);
        Assert.NotNull(cItemNode);

        // Assert: ItemNode と TagNode を結ぶ TagRelationLink が存在すること
        var tagRelationLinks = diagram.Links.OfType<TagRelationLink>().ToList();
        Assert.NotEmpty(tagRelationLinks);
        Assert.Contains(tagRelationLinks, l => l.Source.Model == pItemNode && l.Target.Model == tagNode);
        Assert.Contains(tagRelationLinks, l => l.Source.Model == cItemNode && l.Target.Model == tagNode);

        // Assert: 親Item と 子Item を結ぶ LinkModel が存在すること
        var itemToItemLink = diagram.Links.OfType<Blazor.Diagrams.Core.Models.LinkModel>()
            .FirstOrDefault(l => l is not TagRelationLink && l is not TagEdgeLink);
        Assert.NotNull(itemToItemLink);
        Assert.Same(pItemNode, itemToItemLink.Source.Model);
        Assert.Same(cItemNode, itemToItemLink.Target.Model);
    }

    [Fact]
    public void TagDiagramPage_ClearingFocus_PreservesContextItemsAndPinnedTags()
    {
        // Arrange
        var tag1 = new TagEntity { Id = 1, Name = "SourceTag", OwnerId = TestUserId, CachedWeight = 5 };
        var parentItem = new ItemEntity
        {
            Id = 10,
            Content = "Parent item content",
            OwnerId = TestUserId,
            TagRelations = [new TagRelation { TagId = 1, ItemId = 10, OwnerId = TestUserId }]
        };

        _ = _dataProviderMock.Setup(p => p.LoadAllTagsAsync()).ReturnsAsync([tag1]);
        _ = _dataProviderMock.Setup(p => p.LoadAllEdgesAsync()).ReturnsAsync([]);
        _ = _dataProviderMock.Setup(p => p.GetContextTagIdsForItemAsync(10)).ReturnsAsync([1]);
        _ = _dataProviderMock.Setup(p => p.GetContextItemsAsync(10)).ReturnsAsync([parentItem]);

        var nav = _ctx.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        nav.NavigateTo("http://localhost/tag-diagram?itemId=10");

        var cut = _ctx.Render<TagDiagramPage>();
        cut.WaitForState(() => cut.Markup.Contains("Tag Edge Diagram"));

        var canvas = cut.FindComponent<TagDiagramCanvas>();
        var diagram = canvas.Instance.Diagram;

        // ノードをクリックしてフォーカス
        var tagNode = diagram.Nodes.OfType<TagNode>().First(n => n.Tag.Id == 1);
        cut.InvokeAsync(() => tagNode.RequestFocusTag!(1));
        cut.WaitForState(() => cut.Markup.Contains("① 始点"));

        // フォーカス解除ボタンをクリック
        var clearFocusButton = cut.FindAll("button").First(b => b.TextContent.Contains("フォーカス解除"));
        cut.InvokeAsync(() => clearFocusButton.Click());

        // Assert: フォーカス解除後も、ItemNode および pinned な TagNode が消えずに残っていること
        cut.WaitForState(() => !cut.Markup.Contains("① 始点"));
        Assert.Contains(diagram.Nodes.OfType<TagNode>(), n => n.Tag.Id == 1);
        Assert.Contains(diagram.Nodes.OfType<ItemNode>(), n => n.Item.Id == 10);
    }

    [Fact]
    public void TagDiagramPage_InitializesDiagram_WithInverseZoomEnabled()
    {
        // Arrange
        _ = _dataProviderMock.Setup(p => p.LoadAllTagsAsync()).ReturnsAsync([]);
        _ = _dataProviderMock.Setup(p => p.LoadAllEdgesAsync()).ReturnsAsync([]);

        // Act
        var cut = _ctx.Render<TagDiagramPage>();
        cut.WaitForState(() => cut.Markup.Contains("Tag Edge Diagram"));

        var canvas = cut.FindComponent<TagDiagramCanvas>();
        var diagram = canvas.Instance.Diagram;

        // Assert: Zoom.Inverse が true（ピンチインで縮小、ピンチアウトで拡大）に設定されていること
        Assert.True(diagram.Options.Zoom.Enabled);
        Assert.True(diagram.Options.Zoom.Inverse);

        // 動作検証: コンテナを設定した状態で DeltaY > 0（ピンチイン）を送信すると縮小し、DeltaY < 0（ピンチアウト）を送信すると拡大すること
        diagram.SetContainer(new Rectangle(0, 0, 800, 600));
        diagram.SetZoom(1.0);

        // ピンチイン（DeltaY > 0）
        diagram.TriggerWheel(new Blazor.Diagrams.Core.Events.WheelEventArgs(100, 100, 0, 0, true, false, false, 0, 100, 0, 0));
        Assert.True(diagram.Zoom < 1.0, $"Expected zoom to decrease on pinch-in (DeltaY > 0), but was {diagram.Zoom}");

        // ピンチアウト（DeltaY < 0）
        double currentZoom = diagram.Zoom;
        diagram.TriggerWheel(new Blazor.Diagrams.Core.Events.WheelEventArgs(100, 100, 0, 0, true, false, false, 0, -100, 0, 0));
        Assert.True(diagram.Zoom > currentZoom, $"Expected zoom to increase on pinch-out (DeltaY < 0), but was {diagram.Zoom}");
    }

    [Fact]
    public void TagDiagramPage_InitializesDiagram_WithSmoothPathGenerator()
    {
        // Arrange
        _ = _dataProviderMock.Setup(p => p.LoadAllTagsAsync()).ReturnsAsync([]);
        _ = _dataProviderMock.Setup(p => p.LoadAllEdgesAsync()).ReturnsAsync([]);

        // Act
        var cut = _ctx.Render<TagDiagramPage>();
        cut.WaitForState(() => cut.Markup.Contains("Tag Edge Diagram"));

        var canvas = cut.FindComponent<TagDiagramCanvas>();
        var diagram = canvas.Instance.Diagram;

        // Assert: Links.DefaultPathGenerator が SmoothPathGenerator に設定されていること
        Assert.IsType<Blazor.Diagrams.Core.PathGenerators.SmoothPathGenerator>(diagram.Options.Links.DefaultPathGenerator);
    }

    [Fact]
    public void ApplyEdgeOffset_AddsPerpendicularVertex_AlternatingSigns()
    {
        // Arrange
        var tag1 = new TagEntity { Id = 1, Name = "Tag1", OwnerId = TestUserId };
        var tag2 = new TagEntity { Id = 2, Name = "Tag2", OwnerId = TestUserId };
        var nodeA = new TagNode(tag1, new Point(0, 0));
        var nodeB = new TagNode(tag2, new Point(100, 0)); // 水平方向
        var edge1 = new TagEdge { Id = 10, SourceTagId = 1, TargetTagId = 2, OwnerId = TestUserId };
        var edge2 = new TagEdge { Id = 11, SourceTagId = 1, TargetTagId = 2, OwnerId = TestUserId };
        var link1 = new TagEdgeLink(edge1, nodeA.Ports[0], nodeB.Ports[0]);
        var link2 = new TagEdgeLink(edge2, nodeA.Ports[0], nodeB.Ports[0]);

        // Act: pairIndex = 1 (+40), pairIndex = 2 (-40)
        TagDiagramPage.ApplyEdgeOffset(link1, nodeA, nodeB, 1);
        TagDiagramPage.ApplyEdgeOffset(link2, nodeA, nodeB, 2);

        // Assert
        Assert.Single(link1.Vertices);
        Assert.Single(link2.Vertices);

        var v1 = link1.Vertices[0].Position;
        var v2 = link2.Vertices[0].Position;

        // 中間点は X = 50
        Assert.Equal(50, v1.X, 0.01);
        Assert.Equal(50, v2.X, 0.01);

        // 水平進行 (100, 0) に対する垂直は Y 方向。v1 と v2 は逆方向 (一方が正、一方が負)
        Assert.True(v1.Y != 0);
        Assert.True(v2.Y != 0);
        Assert.Equal(-v1.Y, v2.Y, 0.01);
    }

    [Fact]
    public void TagDiagramPage_BuildDiagramElements_AppliesOffsetToMultipleEdgesBetweenSameTags()
    {
        // Arrange: 同じタグ間に 2 本のエッジが存在する
        var tag1 = new TagEntity { Id = 1, Name = "Alpha", OwnerId = TestUserId };
        var tag2 = new TagEntity { Id = 2, Name = "Beta", OwnerId = TestUserId };
        var edge1 = new TagEdge { Id = 101, SourceTagId = 1, TargetTagId = 2, OwnerId = TestUserId };
        var edge2 = new TagEdge { Id = 102, SourceTagId = 2, TargetTagId = 1, OwnerId = TestUserId };

        _ = _dataProviderMock.Setup(p => p.LoadAllTagsAsync()).ReturnsAsync([tag1, tag2]);
        _ = _dataProviderMock.Setup(p => p.LoadAllEdgesAsync()).ReturnsAsync([edge1, edge2]);

        // Act
        var cut = _ctx.Render<TagDiagramPage>();
        cut.WaitForState(() => cut.Markup.Contains("Tag Edge Diagram"));

        var canvas = cut.FindComponent<TagDiagramCanvas>();
        var diagram = canvas.Instance.Diagram;

        var links = diagram.Links.OfType<TagEdgeLink>().ToList();
        Assert.Equal(2, links.Count);

        // Assert: 1本目はオフセットなし(Vertices 0件)、2本目は重なり回避のため中間点 Vertex が付与されていること
        TagEdgeLink firstLink = links[0];
        TagEdgeLink secondLink = links[1];

        Assert.Empty(firstLink.Vertices);
        Assert.Single(secondLink.Vertices);
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }
}