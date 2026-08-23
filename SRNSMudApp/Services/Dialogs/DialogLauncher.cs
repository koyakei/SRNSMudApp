using MudBlazor;

namespace SRNSMudApp.Services.Dialogs;

/// <summary>
///     <see cref="IDialogLauncher" /> の既定実装。MudBlazor の <see cref="IDialogService" /> に委譲する。
/// </summary>
public sealed class DialogLauncher(IDialogService dialogService) : IDialogLauncher
{
    public Task<IDialogReference> ShowAsync(
        Type dialogType,
        string title,
        DialogParameters? parameters = null,
        DialogOptions? options = null)
    {
        return dialogService.ShowAsync(
            dialogType,
            title,
            parameters ?? new DialogParameters(),
            options ?? new DialogOptions());
    }
}
