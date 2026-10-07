using Microsoft.AspNetCore.Components;

using MudBlazor;

namespace SRNSMudApp.Components.Item;

/// <summary>
///     モバイル表示等で全画面表示されるアイテム追加ダイアログ。
///     上部バーに閉じるボタンと保存ボタンを配置し、Twitter風のUXを提供する。
/// </summary>
public sealed partial class AddItemDialog : ComponentBase
{
    /// <summary>
    ///     MudBlazor ダイアログインスタンスを取得または設定します。
    /// </summary>
    [CascadingParameter]
    private IMudDialogInstance MudDialog { get; set; } = null!;

    /// <summary>
    ///     アイテム追加フォームの HTML フォーム ID。
    /// </summary>
    internal const string FormId = "add-item-dialog-form";

    /// <summary>
    ///     ダイアログをキャンセルして閉じます。
    /// </summary>
    private void Cancel() => MudDialog.Cancel();

    /// <summary>
    ///     アイテムが追加された際にダイアログを成功結果で閉じます。
    /// </summary>
    private void HandleItemAdded() => MudDialog.Close(DialogResult.Ok(true));
}