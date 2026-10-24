namespace SRNSMudApp.Components.Item;

using System;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Components;

using MudBlazor;

using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;

/// <summary>
///     引用投稿ダイアログコンポーネントのコードビハインド。
///     引用元アイテムの確認、コメント本文の入力検証、URLプレビューの抽出および引用投稿の保存を制御する。
/// </summary>
public partial class QuoteItemDialog : ComponentBase
{
    [CascadingParameter]
    private IMudDialogInstance MudDialog { get; set; } = null!;

    [Parameter]
    public Item QuotedItem { get; set; } = null!;

    [Parameter]
    public string CurrentUserId { get; set; } = string.Empty;

    [Inject]
    private QuoteItemViewModel ViewModel { get; set; } = null!;

    [Inject]
    private ISnackbar Snackbar { get; set; } = null!;

    protected override void OnParametersSet()
    {
        ViewModel.QuotedItem = QuotedItem;
        ViewModel.CurrentUserId = CurrentUserId;
    }

    private void Cancel()
    {
        MudDialog.Cancel();
    }

    private async Task SaveAsync()
    {
        if (ViewModel.IsSubmitting)
        {
            Snackbar.Add(Client.Services.DoubleSubmissionGuard.DuplicateSubmissionMessage, Severity.Info);
            return;
        }

        var result = await ViewModel.SubmitAsync();
        switch (result)
        {
            case Success<Item> success:
                Snackbar.Add("引用を投稿しました。", Severity.Success);
                MudDialog.Close(DialogResult.Ok(success.Value));
                break;
            case Failure failure:
                if (failure.ErrorMessage == Client.Services.DoubleSubmissionGuard.DuplicateSubmissionMessage)
                {
                    Snackbar.Add(failure.ErrorMessage, Severity.Info);
                    return;
                }
                Snackbar.Add(failure.ErrorMessage, Severity.Error);
                if (failure.ErrorMessage.Contains("見つかりませんでした", StringComparison.Ordinal))
                {
                    MudDialog.Cancel();
                }
                break;
            default:
                break;
        }
    }
}