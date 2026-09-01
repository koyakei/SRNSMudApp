using System.Security.Claims;

using Blazor.Diagrams.Core.Geometry;

using Bunit;

using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.Diagram;
using SRNSMudApp.Components.Pages;
using SRNSMudApp.Data;
using SRNSMudApp.Services;

using TagEntity = SRNSMudApp.Data.Tag;

namespace SRNSMudApp.Tests.Components.Diagram;

public sealed class TagDiagramPageTests : IAsyncDisposable
{
    private const string TestUserId = "diagram-user-1";
    private readonly BunitContext _ctx = new();
    private readonly Mock<ITagDiagramDataProvider> _dataProviderMock = new();

    public TagDiagramPageTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices().AddMockSrnsServices();
        _ = _ctx.Services.AddScoped(_ => _dataProviderMock.Object);
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
        Assert.True(node1.IsFocused);
        Assert.False(node2.IsFocused);
        Assert.True(node1.Selected);

        // node1 は X=80, Width=160 (center=160)。画面幅 800 の中央 (400) に配置するため panX = 400 - 160 = 240
        Assert.Equal(240, diagram.Pan.X);
        Assert.NotEqual(0, diagram.Pan.Y);
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }
}