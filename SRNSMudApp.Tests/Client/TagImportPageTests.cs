namespace SRNSMudApp.Tests.Client;

using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Bunit;

using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Client.Models;
using SRNSMudApp.Client.Models.Api;
using SRNSMudApp.Client.Pages;
using SRNSMudApp.Client.Services;

using Xunit;

public class TagImportPageTests : IAsyncDisposable
{
    private readonly BunitContext _ctx = new();
    private readonly Mock<IItemListApiClient> _apiClientMock = new();

    public TagImportPageTests()
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
        var cut = _ctx.Render<TagImportPage>();

        // Assert
        Assert.Contains("タグインポート (WASM)", cut.Markup);
        Assert.Contains("インポート先タグ（必須）", cut.Markup);
        Assert.Contains("CSV データ", cut.Markup);
        Assert.Contains("CSVファイルを選択", cut.Markup);
        Assert.Contains("インポート実行", cut.Markup);
    }

    [Fact]
    public async Task WhenParentTagAndCsvProvided_CallsImportAndRendersAlert()
    {
        // Arrange
        var expectedResponse = new ImportTagCsvResponse(5, 2, "5個のタグを新規作成、2個のタグを更新しました。");
        _apiClientMock
            .Setup(c => c.ImportTagCsvAsync(It.IsAny<ImportTagCsvRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        var cut = _ctx.Render<TagImportPage>();

        // 親タグ設定
        var autocomplete = cut.FindComponent<MudAutocomplete<TagSuggestion>>();
        await cut.InvokeAsync(() => autocomplete.Instance.ValueChanged.InvokeAsync(new TagSuggestion(1, "親タグ", null)));

        // CSV テキスト入力
        var textField = cut.FindComponent<MudTextField<string>>();
        await cut.InvokeAsync(() => textField.Instance.ValueChanged.InvokeAsync("親タグ,子タグ,孫タグ"));

        // Act - 実行ボタンクリック
        var executeBtn = cut.Find("[data-testid='execute-import-btn']");
        executeBtn.Click();

        // Assert
        _apiClientMock.Verify(c => c.ImportTagCsvAsync(
            It.Is<ImportTagCsvRequest>(r => r.ParentTagName == "親タグ" && r.CsvContent == "親タグ,子タグ,孫タグ"),
            It.IsAny<CancellationToken>()), Times.Once);

        Assert.Contains("インポート完了", cut.Markup);
        Assert.Contains("5個のタグを新規作成、2個のタグを更新しました。", cut.Markup);
    }

    [Fact]
    public async Task WhenAdminUser_RendersSystemTagSwitch()
    {
        // Arrange
        await using var adminCtx = new BunitContext();
        adminCtx.JSInterop.Mode = JSRuntimeMode.Loose;
        adminCtx.Services.AddMudServices();
        adminCtx.Services.AddScoped(_ => _apiClientMock.Object);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "admin1"),
            new Claim(ClaimTypes.Role, "Admin")
        };
        var identity = new ClaimsIdentity(claims, "test");
        var user = new ClaimsPrincipal(identity);
        var authState = new AuthenticationState(user);

        var authMock = new Mock<AuthenticationStateProvider>();
        authMock.Setup(p => p.GetAuthenticationStateAsync()).ReturnsAsync(authState);
        adminCtx.Services.AddCascadingAuthenticationState();
        adminCtx.Services.AddSingleton(authMock.Object);

        adminCtx.Render<MudPopoverProvider>();

        // Act
        var cut = adminCtx.Render<TagImportPage>();

        // Assert
        Assert.Contains("システムタグとしてインポート", cut.Markup);
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}