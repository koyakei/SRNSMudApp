namespace SRNSMudApp.Tests.Client;

using System;
using System.Threading;
using System.Threading.Tasks;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Client.Models.Api;
using SRNSMudApp.Client.Pages;
using SRNSMudApp.Client.Services;

using Xunit;

public class TaggingImportPageTests : IAsyncDisposable
{
    private readonly BunitContext _ctx = new();
    private readonly Mock<IItemListApiClient> _apiClientMock = new();

    public TaggingImportPageTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddMudServices();
        _ctx.Services.AddScoped(_ => _apiClientMock.Object);
        _ctx.Render<MudPopoverProvider>();
    }

    [Fact]
    public void WhenInitialized_RendersFormElements()
    {
        // Act
        var cut = _ctx.Render<TaggingImportPage>();

        // Assert
        Assert.Contains("タグ付け構造インポート (WASM)", cut.Markup);
        Assert.Contains("JSONファイルを選択", cut.Markup);
        Assert.Contains("インポート開始", cut.Markup);
    }

    [Fact]
    public async Task WhenJsonProvided_CallsImportAndRendersAlert()
    {
        // Arrange
        var expectedResponse = new ImportTaggingResponse(1, 2, 1, 1, 1, ["成功"]);
        _apiClientMock
            .Setup(c => c.ImportTaggingJsonAsync(It.IsAny<ImportTaggingJsonRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        var cut = _ctx.Render<TaggingImportPage>();

        // JSON テキスト入力
        var textField = cut.FindComponent<MudTextField<string>>();
        await cut.InvokeAsync(() => textField.Instance.ValueChanged.InvokeAsync("{\"TaggingRequestEntity\":{}}"));

        // Act - インポート実行クリック
        var executeBtn = cut.Find("[data-testid='execute-tagging-import-btn']");
        executeBtn.Click();

        // Assert
        _apiClientMock.Verify(c => c.ImportTaggingJsonAsync(
            It.Is<ImportTaggingJsonRequest>(r => r.JsonText == "{\"TaggingRequestEntity\":{}}"),
            It.IsAny<CancellationToken>()), Times.Once);

        Assert.Contains("インポート完了！", cut.Markup);
        Assert.Contains("作成されたアイテム: 1 件", cut.Markup);
        Assert.Contains("新規作成されたタグ: 2 件", cut.Markup);
        Assert.Contains("作成されたタグリレーション: 1 件", cut.Markup);
        Assert.Contains("作成されたエッジ: 1 件", cut.Markup);
    }

    [Fact]
    public async Task WhenImportReturnsNull_ShowsErrorMessage()
    {
        // Arrange
        _apiClientMock
            .Setup(c => c.ImportTaggingJsonAsync(It.IsAny<ImportTaggingJsonRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ImportTaggingResponse?)null);

        var cut = _ctx.Render<TaggingImportPage>();

        var textField = cut.FindComponent<MudTextField<string>>();
        await cut.InvokeAsync(() => textField.Instance.ValueChanged.InvokeAsync("invalid"));

        // Act
        var executeBtn = cut.Find("[data-testid='execute-tagging-import-btn']");
        executeBtn.Click();

        // Assert
        _apiClientMock.Verify(c => c.ImportTaggingJsonAsync(It.IsAny<ImportTaggingJsonRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.DoesNotContain("インポート完了！", cut.Markup);
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}