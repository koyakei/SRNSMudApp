using Bunit;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.UI;
using SRNSMudApp.Tests.TestSupport;

namespace SRNSMudApp.Tests.Components.UI;

/// <summary>
///     <see cref="RejectRequestDialog" /> の bUnit 単体テスト。
///     却下理由入力、キャンセル、未入力送信、入力送信の各動作を検証する。
/// </summary>
public sealed class RejectRequestDialogTests : IAsyncLifetime
{
    private readonly BunitContext _ctx = new();

    public RejectRequestDialogTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices().AddMockSrnsServices();
        _ = _ctx.Render<MudPopoverProvider>();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _ctx.DisposeAsync().AsTask();

    [Fact]
    public async Task RejectRequestDialog_RendersPromptAndInputs()
    {
        // Arrange
        var host = _ctx.Render<DialogHost>();
        var dialogService = _ctx.Services.GetRequiredService<IDialogService>();

        // Act
        _ = await dialogService.ShowAsync<RejectRequestDialog>("分割リクエストを却下");

        // Assert
        host.WaitForAssertion(() =>
        {
            Assert.Contains("このリクエストを却下します", host.Markup);
            Assert.Contains("却下理由 (オプション)", host.Markup);
            Assert.Contains("キャンセル", host.Markup);
            Assert.Contains("却下する", host.Markup);
        });
    }

    [Fact]
    public async Task RejectRequestDialog_WhenCancelClicked_CancelsDialog()
    {
        // Arrange
        var host = _ctx.Render<DialogHost>();
        var dialogService = _ctx.Services.GetRequiredService<IDialogService>();

        var dialogRef = await dialogService.ShowAsync<RejectRequestDialog>("分割リクエストを却下");

        host.WaitForAssertion(() =>
        {
            var cancelBtn = host.Find("button:contains('キャンセル')");
            Assert.NotNull(cancelBtn);
        });

        // Act
        var cancelBtn = host.Find("button:contains('キャンセル')");
        await host.InvokeAsync(() => cancelBtn.Click());

        // Assert
        var result = await dialogRef.Result;
        Assert.NotNull(result);
        Assert.True(result.Canceled);
    }

    [Fact]
    public async Task RejectRequestDialog_WhenSubmittedWithoutComment_ClosesWithEmptyString()
    {
        // Arrange
        var host = _ctx.Render<DialogHost>();
        var dialogService = _ctx.Services.GetRequiredService<IDialogService>();

        var dialogRef = await dialogService.ShowAsync<RejectRequestDialog>("分割リクエストを却下");

        host.WaitForAssertion(() =>
        {
            var submitBtn = host.Find("button:contains('却下する')");
            Assert.NotNull(submitBtn);
        });

        // Act
        var submitBtn = host.Find("button:contains('却下する')");
        await host.InvokeAsync(() => submitBtn.Click());

        // Assert
        var result = await dialogRef.Result;
        Assert.NotNull(result);
        Assert.False(result.Canceled);
        Assert.Equal(string.Empty, result.Data);
    }

    [Fact]
    public async Task RejectRequestDialog_WhenSubmittedWithComment_ClosesWithCommentString()
    {
        // Arrange
        var host = _ctx.Render<DialogHost>();
        var dialogService = _ctx.Services.GetRequiredService<IDialogService>();

        var dialogRef = await dialogService.ShowAsync<RejectRequestDialog>("分割リクエストを却下");

        host.WaitForAssertion(() =>
        {
            var textarea = host.Find("textarea");
            Assert.NotNull(textarea);
        });

        // Act
        var textarea = host.Find("textarea");
        textarea.Input("この部分は元記事に不可欠なため分割できません。");

        var submitBtn = host.Find("button:contains('却下する')");
        await host.InvokeAsync(() => submitBtn.Click());

        // Assert
        var result = await dialogRef.Result;
        Assert.NotNull(result);
        Assert.False(result.Canceled);
        Assert.Equal("この部分は元記事に不可欠なため分割できません。", result.Data);
    }

    private sealed class DialogHost : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<MudDialogProvider>(0);
            builder.CloseComponent();
        }
    }
}