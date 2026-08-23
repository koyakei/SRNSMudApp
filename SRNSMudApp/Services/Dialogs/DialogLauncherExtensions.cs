using Microsoft.AspNetCore.Components;

using MudBlazor;

namespace SRNSMudApp.Services.Dialogs;

/// <summary>
///     <see cref="IDialogLauncher" /> の利便性拡張。既存の
///     <c>DialogService.ShowAsync&lt;T&gt;(title, parameters, options)</c> 呼び出しと同じ形で書けるようにする。
/// </summary>
public static class DialogLauncherExtensions
{
    public static Task<IDialogReference> ShowAsync<TDialog>(
        this IDialogLauncher launcher,
        string title,
        DialogParameters? parameters = null,
        DialogOptions? options = null)
        where TDialog : IComponent => launcher.ShowAsync(typeof(TDialog), title, parameters, options);

    public static Task<IDialogReference> ShowAsync<TDialog>(
        this IDialogLauncher launcher,
        string title,
        DialogOptions options)
        where TDialog : IComponent => launcher.ShowAsync(typeof(TDialog), title, null, options);
}