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

    [Fact]
    public void UrlPreviewCard_WhenTagIsRedirectedToVisibleAncestor_RendersRedirectedTagNameInPill()
    {
        // Arrange: 不可視タグからリダイレクトされた祖先タグ情報を持つプレビューデータ
        const string invisibleTagUrl = "/TagDetail/999";
        const string redirectedTagName = "PublicAncestorCategory";
        const string ownerName = "alice";

        var redirectedPreviewData = new LinkPreviewData
        {
            Url = invisibleTagUrl,
            Title = $"Tag: {redirectedTagName} ({ownerName})",
            DisplayText = $"{redirectedTagName}:{ownerName}",
            Description = "祖先タグの説明文",
            Tags = [new TagPreviewItem(10, redirectedTagName, ownerName, 0)],
            SiteName = "SRNSMudApp",
            IsSuccess = true
        };

        // Act: 不可視タグの URL に対する UrlPreviewCard をレンダリング
        IRenderedComponent<UrlPreviewCard> component = _ctx.Render<UrlPreviewCard>(parameters => parameters
            .Add(p => p.Url, invisibleTagUrl)
            .Add(p => p.LoadPreview, _ => Task.FromResult<LinkPreviewData?>(redirectedPreviewData))
        );

        // Assert: 画面上のピル表示がリダイレクトしたタグ名になっていること
        var pill = component.Find("[data-testid='internal-link-preview-pill']");
        Assert.NotNull(pill);
        Assert.Equal($"{redirectedTagName}:{ownerName}", pill.TextContent.Trim());
        Assert.Contains(redirectedTagName, pill.TextContent);
        Assert.DoesNotContain("Secret", pill.TextContent);
    }

    [Fact]
    public void UrlPreviewCard_InternalLinkClick_NavigatesUsingNavigationManager()
    {
        // Arrange
        const string targetUrl = "/ItemDetail/789";
        var previewData = new LinkPreviewData
        {
            Url = targetUrl,
            Title = "Item #789",
            Description = "アイテムへのリンク",
            IsSuccess = true
        };

        NavigationManager navManager = _ctx.Services.GetRequiredService<NavigationManager>();
        navManager.NavigateTo("http://localhost/");

        IRenderedComponent<UrlPreviewCard> component = _ctx.Render<UrlPreviewCard>(parameters => parameters
            .Add(p => p.Url, targetUrl)
            .Add(p => p.LoadPreview, _ => Task.FromResult<LinkPreviewData?>(previewData))
        );

        // Assert 1: 初期表示時点で自動遷移していないこと
        Assert.Equal("http://localhost/", navManager.Uri);

        // Act
        var pill = component.Find("[data-testid='internal-link-preview-pill']");
        pill.Click();

        // Assert 2: 外部ブラウザウィンドウ open ではなく NavigationManager でクリック時に同一 SPA 内遷移されること
        Assert.EndsWith("/ItemDetail/789", navManager.Uri, StringComparison.Ordinal);
    }
}