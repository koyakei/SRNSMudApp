#region

using Bunit;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

using MudBlazor.Services;

using SRNSMudApp.Components.UI;
using SRNSMudApp.Models;

#endregion

namespace SRNSMudApp.Tests.Components.UI;

/// <summary>
///     UrlPreviewCard コンポーネントの単体テスト (bUnit)。
///     内部リンクプレビュー時のピル描画および TagBar のツールチップ表示を検証する。
/// </summary>
public class UrlPreviewCardTests : IAsyncDisposable
{
    private readonly BunitContext _ctx;

    public UrlPreviewCardTests()
    {
        _ctx = new BunitContext();
        _ctx.Services.AddMudServices().AddSrnsComponentServices();
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Render<MudBlazor.MudPopoverProvider>();
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void UrlPreviewCard_InternalLinkWithTags_RendersPillAndTooltipWithTagBar()
    {
        // Arrange
        var previewData = new LinkPreviewData
        {
            Url = "/ItemDetail/123",
            Title = "Item #123",
            Description = "本文のみのプレビューテキスト",
            Tags = [new TagPreviewItem(1, "TestTag", "author", 5)],
            SiteName = "SRNSMudApp",
            IsSuccess = true
        };

        // Act
        IRenderedComponent<UrlPreviewCard> component = _ctx.Render<UrlPreviewCard>(parameters => parameters
            .Add(p => p.Url, "/ItemDetail/123")
            .Add(p => p.LoadPreview, _ => Task.FromResult<LinkPreviewData?>(previewData))
        );

        // Assert
        var pill = component.Find("[data-testid='internal-link-preview-pill']");
        Assert.NotNull(pill);
        Assert.Equal("本文のみのプレビューテキスト", pill.TextContent.Trim());
        // TagBar またはツールチップの要素が存在することを検証
        Assert.NotNull(component.FindComponent<MudBlazor.MudTooltip>());
    }

    [Fact]
    public void UrlPreviewCard_InternalLinkWithoutTags_RendersPillWithoutTooltip()
    {
        // Arrange
        var previewData = new LinkPreviewData
        {
            Url = "/ItemDetail/456",
            Title = "Item #456",
            Description = "タグなし本文",
            Tags = [],
            SiteName = "SRNSMudApp",
            IsSuccess = true
        };

        // Act
        IRenderedComponent<UrlPreviewCard> component = _ctx.Render<UrlPreviewCard>(parameters => parameters
            .Add(p => p.Url, "/ItemDetail/456")
            .Add(p => p.LoadPreview, _ => Task.FromResult<LinkPreviewData?>(previewData))
        );

        // Assert
        var pill = component.Find("[data-testid='internal-link-preview-pill']");
        Assert.NotNull(pill);
        Assert.Equal("タグなし本文", pill.TextContent.Trim());
        Assert.Empty(component.FindComponents<MudBlazor.MudTooltip>());
    }
}