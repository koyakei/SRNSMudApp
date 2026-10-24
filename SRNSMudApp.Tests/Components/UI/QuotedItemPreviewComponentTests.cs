using Bunit;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

using MudBlazor.Services;

using SRNSMudApp.Components.UI;
using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Tests.TestSupport;

namespace SRNSMudApp.Tests.Components.UI;

/// <summary>
///     <see cref="QuotedItemPreview" /> コンポーネントにおける本文およびリンクプレビュー表示の検証テスト。
/// </summary>
public sealed class QuotedItemPreviewComponentTests : IAsyncLifetime
{
    private readonly BunitContext _ctx = new();

    public QuotedItemPreviewComponentTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices().AddMockSrnsServices();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    [Fact]
    public void QuotedItemPreview_WhenContentContainsUrl_RendersPreviewWithReplacedText()
    {
        // Arrange
        var item = new SRNSMudApp.Data.Item
        {
            Id = 10,
            OwnerId = "u1",
            Content = "前テキスト /ItemDetail/20 後テキスト",
            Owner = new ApplicationUser { Id = "u1", UserName = "TestUser" }
        };

        Task<LinkPreviewData?> CustomLoadPreview(string url)
        {
            return Task.FromResult<LinkPreviewData?>(new LinkPreviewData
            {
                Url = url,
                IsSuccess = true,
                DisplayText = "置換されたリンク文字列"
            });
        }

        // Act
        IRenderedComponent<QuotedItemPreview> cut = _ctx.Render<QuotedItemPreview>(parameters => parameters
            .Add(p => p.QuotedItemId, item.Id)
            .Add(p => p.QuotedItem, item)
            .Add(p => p.LoadPreview, CustomLoadPreview));

        // Assert: リンクプレビューピルが描画され、URLではなく置換文字列が表示されていること
        cut.WaitForAssertion(() =>
        {
            var pill = cut.Find("[data-testid='internal-link-preview-pill']");
            Assert.Contains("置換されたリンク文字列", pill.TextContent);
            Assert.Contains("前テキスト", cut.Markup);
            Assert.Contains("後テキスト", cut.Markup);
        });
    }

    [Fact]
    public void QuotedItemPreview_WhenContentHasNoUrl_RendersPlainText()
    {
        // Arrange
        var item = new SRNSMudApp.Data.Item
        {
            Id = 10,
            OwnerId = "u1",
            Content = "通常のテキストのみ",
            Owner = new ApplicationUser { Id = "u1", UserName = "TestUser" }
        };

        // Act
        IRenderedComponent<QuotedItemPreview> cut = _ctx.Render<QuotedItemPreview>(parameters => parameters
            .Add(p => p.QuotedItemId, item.Id)
            .Add(p => p.QuotedItem, item));

        // Assert
        Assert.Contains("通常のテキストのみ", cut.Markup);
        Assert.Empty(cut.FindAll("[data-testid='internal-link-preview-pill']"));
    }

    [Fact]
    public void QuotedItemPreview_WhenContentEmpty_RendersNoContent()
    {
        // Arrange
        var item = new SRNSMudApp.Data.Item
        {
            Id = 10,
            OwnerId = "u1",
            Content = string.Empty,
            Owner = new ApplicationUser { Id = "u1", UserName = "TestUser" }
        };

        // Act
        IRenderedComponent<QuotedItemPreview> cut = _ctx.Render<QuotedItemPreview>(parameters => parameters
            .Add(p => p.QuotedItemId, item.Id)
            .Add(p => p.QuotedItem, item));

        // Assert
        Assert.Contains("内容なし", cut.Markup);
    }

    [Fact]
    public void QuotedItemPreview_WhenRendered_DoesNotNavigateUntilClicked()
    {
        // Arrange
        var item = new SRNSMudApp.Data.Item
        {
            Id = 55,
            OwnerId = "u1",
            Content = "引用元の本文テキスト",
            Owner = new ApplicationUser { Id = "u1", UserName = "TestUser" }
        };

        NavigationManager navManager = _ctx.Services.GetRequiredService<NavigationManager>();
        navManager.NavigateTo("http://localhost/CurrentPage");

        // Act - 初期レンダリング
        IRenderedComponent<QuotedItemPreview> cut = _ctx.Render<QuotedItemPreview>(parameters => parameters
            .Add(p => p.QuotedItemId, item.Id)
            .Add(p => p.QuotedItem, item));

        // Assert 1: 初期表示時点で自動遷移していないこと
        Assert.Equal("http://localhost/CurrentPage", navManager.Uri);

        // Act 2: プレビュー領域をクリック
        var previewElement = cut.Find("[data-testid='quoted-item-preview-55']");
        previewElement.Click();

        // Assert 2: クリックしたときに初めて /ItemDetail/55 に遷移すること
        Assert.EndsWith("/ItemDetail/55", navManager.Uri, StringComparison.Ordinal);
    }

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }
}