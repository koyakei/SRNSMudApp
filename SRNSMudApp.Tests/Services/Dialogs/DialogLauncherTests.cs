#region

using System.Threading.Tasks;

using Bunit;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor;

using SRNSMudApp.Services.Dialogs;

using Xunit;

#endregion

namespace SRNSMudApp.Tests.Services.Dialogs;

/// <summary>
///     <see cref="DialogLauncher" /> が <see cref="IDialogService" /> へ正しく委譲することを検証する。
/// </summary>
public class DialogLauncherTests : IAsyncDisposable
{
    private sealed class DummyDialog : ComponentBase
    {
    }

    private readonly Mock<IDialogService> _dialogServiceMock = new();
    private readonly Mock<IDialogReference> _referenceMock = new();
    private readonly BunitContext _ctx = new();

    [Fact]
    public async Task ShowAsync_WithNullParameters_PassesEmptyParametersAndDefaultOptions()
    {
        _ = _dialogServiceMock
            .Setup(s => s.ShowAsync(typeof(DummyDialog), "タイトル", It.IsAny<DialogParameters>(), It.IsAny<DialogOptions>()))
            .ReturnsAsync(_referenceMock.Object);

        var actual = await new DialogLauncher(_dialogServiceMock.Object)
            .ShowAsync(typeof(DummyDialog), "タイトル");

        Assert.Same(_referenceMock.Object, actual);
        _dialogServiceMock.Verify(
            s => s.ShowAsync(
                typeof(DummyDialog),
                "タイトル",
                It.Is<DialogParameters>(p => p.Count == 0),
                It.IsAny<DialogOptions>()),
            Times.Once);
    }

    [Fact]
    public async Task ShowAsyncGenericExtension_ForwardsConcreteTypeAndOptions()
    {
        _ = _dialogServiceMock
            .Setup(s => s.ShowAsync(typeof(DummyDialog), "タイトル", It.IsAny<DialogParameters>(), It.IsAny<DialogOptions>()))
            .ReturnsAsync(_referenceMock.Object);

        // bUnit 側に DI 登録し、拡張メソッド経由の呼び出しも確認する
        _ctx.Services.AddSingleton<IDialogLauncher>(new DialogLauncher(_dialogServiceMock.Object));
        var launcher = _ctx.Services.GetRequiredService<IDialogLauncher>();

        var options = new DialogOptions { CloseOnEscapeKey = true };
        var actual = await launcher.ShowAsync<DummyDialog>("タイトル", options);

        Assert.Same(_referenceMock.Object, actual);
        _dialogServiceMock.Verify(
            s => s.ShowAsync(typeof(DummyDialog), "タイトル", It.IsAny<DialogParameters>(), options),
            Times.Once);
    }

    public async ValueTask DisposeAsync() => await _ctx.DisposeAsync();
}