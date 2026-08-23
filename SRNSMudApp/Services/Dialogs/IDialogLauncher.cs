using MudBlazor;

namespace SRNSMudApp.Services.Dialogs;

/// <summary>
///     ダイアログ起動の抽象化。
///     MudBlazor の <see cref="IDialogService" /> を「コンポーネント型 + パラメータ辞書」方式でラップし、
///     呼び出し側を具象ダイアログコンポーネントの直接依存から切り離して単体テスト可能にする。
/// </summary>
public interface IDialogLauncher
{
    /// <summary>
    ///     指定したダイアログコンポーネントを表示する。
    /// </summary>
    /// <param name="dialogType">ダイアログとして表示するコンポーネントの型。</param>
    /// <param name="title">ダイアログのタイトル。</param>
    /// <param name="parameters">ダイアログコンポーネントへ渡すパラメータ。</param>
    /// <param name="options">ダイアログの表示オプション。</param>
    Task<IDialogReference> ShowAsync(
        Type dialogType,
        string title,
        DialogParameters? parameters = null,
        DialogOptions? options = null);
}
