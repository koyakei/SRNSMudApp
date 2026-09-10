using System.Security.Claims;

using Blazor.Diagrams;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.Diagram;
using SRNSMudApp.Components.UI;
using SRNSMudApp.Tests.TestSupport;

using ItemEntity = SRNSMudApp.Data.Item;

namespace SRNSMudApp.Tests.Components.Diagram;

public class ItemNodeWidgetTests : IAsyncDisposable
{
    private const string TestUserId = "user-item-widget";
    private readonly BunitContext _ctx = new();

    public ItemNodeWidgetTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices().AddMockSrnsServices();
        var authContext = _ctx.AddAuthorization();
        authContext.SetAuthorized(TestUserId);
        authContext.SetClaims(new Claim(ClaimTypes.NameIdentifier, TestUserId));
        _ = _ctx.Render<MudPopoverProvider>();
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }

    [Fact]
    public void ItemNodeWidget_Unexpanded_RendersPreviewText_WithReplacedInternalLink()
    {
        // Arrange
        var diagram = new BlazorDiagram();
        var linkedItem = new ItemEntity { Id = 200, Content = "Detailed Linked Information", OwnerId = TestUserId };
        var parentItem = new ItemEntity
        {
            Id = 100,
            Content = "See internal link /ItemDetail/200 for more info.",
            OwnerId = TestUserId
        };
        var node = new ItemNode(parentItem, [parentItem, linkedItem]);
        diagram.Nodes.Add(node);

        // Act
        var cut = _ctx.Render<ItemNodeWidget>(parameters => parameters
            .Add(p => p.Node, node)
            .AddCascadingValue(diagram));

        // Assert: /ItemDetail/200 が "Detailed Linked Information" に置換されていること
        Assert.Contains("Detailed Linked Information", cut.Markup);
        Assert.DoesNotContain("/ItemDetail/200", cut.Markup);

        // 展開用カードコンテナはまだ存在しないこと
        Assert.Empty(cut.FindAll(".expanded-card-container"));
    }

    [Fact]
    public void ItemNodeWidget_Unexpanded_TruncatesTextAt80Characters()
    {
        // Arrange: 100文字のテキスト
        var diagram = new BlazorDiagram();
        var longText = new string('A', 100);
        var item = new ItemEntity { Id = 100, Content = longText, OwnerId = TestUserId };
        var node = new ItemNode(item, [item]);
        diagram.Nodes.Add(node);

        // Act
        var cut = _ctx.Render<ItemNodeWidget>(parameters => parameters
            .Add(p => p.Node, node)
            .AddCascadingValue(diagram));

        // Assert: 80文字 + "..." で省略されていること
        Assert.Contains(new string('A', 80) + "...", cut.Markup);
        Assert.DoesNotContain(new string('A', 81), cut.Markup);
    }

    [Fact]
    public void ItemNodeWidget_ToggleExpand_ShowsItemCard_AndCompressButtonCollapses()
    {
        // Arrange
        var diagram = new BlazorDiagram();
        var item = new ItemEntity { Id = 100, Content = "Item to expand", OwnerId = TestUserId };
        var node = new ItemNode(item, [item]);
        diagram.Nodes.Add(node);

        // Act 1: 初期表示
        var cut = _ctx.Render<ItemNodeWidget>(parameters => parameters
            .Add(p => p.Node, node)
            .AddCascadingValue(diagram));
        Assert.False(node.IsExpanded);
        Assert.Empty(cut.FindAll(".expanded-card-container"));

        // Act 2: 展開ボタンをクリック
        var expandButton = cut.Find("button");
        expandButton.Click();

        // Assert 2: 展開され、ItemCard と Compress アイコンが表示されること
        Assert.True(node.IsExpanded);
        Assert.NotEmpty(cut.FindAll(".expanded-card-container"));
        Assert.NotNull(cut.FindComponent<ItemCard>());

        // Act 3: 折りたたみ（Compress）ボタンをクリック
        var compressButton = cut.Find(".expanded-card-container button");
        compressButton.Click();

        // Assert 3: 折りたたまれ、ItemCard が非表示になること
        Assert.False(node.IsExpanded);
        Assert.Empty(cut.FindAll(".expanded-card-container"));
    }
}