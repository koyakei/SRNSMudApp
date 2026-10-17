using Microsoft.AspNetCore.Components;

using MudBlazor;

using SRNSMudApp.Components.Item;
using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;

namespace SRNSMudApp.Components.UI;

/// <summary>
///     分割リクエスト確認ダイアログのコードビハインド。
///     選択テキストの確認と、任意で引用コメントを入力・投稿する機能を提供する。
///     リクエスト送信と同時に引用Itemを作成できる。
/// </summary>
public partial class SplitRequestWithQuoteDialog : ComponentBase
{
    [CascadingParameter]
    private IMudDialogInstance MudDialog { get; set; } = null!;

    /// <summary>分割対象として選択されたテキスト。</summary>
    [Parameter]
    public string SelectedText { get; set; } = string.Empty;

    /// <summary>分割リクエスト対象のアイテム（引用元になる）。</summary>
    [Parameter]
    public Data.Item QuotedItem { get; set; } = null!;

    /// <summary>現在ログイン中のユーザーID。</summary>
    [Parameter]
    public string CurrentUserId { get; set; } = string.Empty;

    [Inject]
    private IItemQuoteService ItemQuoteService { get; set; } = null!;

    [Inject]
    private ISnackbar Snackbar { get; set; } = null!;

    [Inject]
    internal QuoteItemViewModel ViewModel { get; set; } = null!;

    private string _quoteContent = string.Empty;
    private bool _isSubmitting;

    /// <summary>引用本文から抽出されたプレビュー表示用の URL 一覧。</summary>
    private IReadOnlyList<string> ExtractedUrls => ItemCardViewModel.ExtractUrls(_quoteContent);

    protected override void OnParametersSet()
    {
        ViewModel.QuotedItem = QuotedItem;
        ViewModel.CurrentUserId = CurrentUserId;
    }

    private void Cancel() => MudDialog.Cancel();

    private async Task SubmitAsync()
    {
        _isSubmitting = true;
        try
        {
            // 引用コメントが入力された場合のみ引用Itemを作成する
            Data.Item? createdQuote = null;
            if (!string.IsNullOrWhiteSpace(_quoteContent))
            {
                createdQuote = await ItemQuoteService.CreateQuoteItemAsync(
                    QuotedItem.Id, _quoteContent, CurrentUserId);

                if (createdQuote is null)
                {
                    Snackbar.Add("引用の投稿に失敗しました。", Severity.Error);
                    return;
                }
            }

            SplitRequestWithQuoteDialogResult dialogResult = new(createdQuote?.Id, createdQuote);
            MudDialog.Close(DialogResult.Ok(dialogResult));
        }
        finally
        {
            _isSubmitting = false;
        }
    }
}

/// <summary>
///     <see cref="SplitRequestWithQuoteDialog" /> の戻り値。
/// </summary>
/// <param name="CreatedQuoteItemId">
///     引用コメントが入力・保存された場合に作成された引用アイテムのID。
///     引用コメントが省略された場合は null。
/// </param>
/// <param name="CreatedQuoteItem">
///     引用コメントが入力・保存された場合に作成された引用アイテムのインスタンス。
/// </param>
public sealed record SplitRequestWithQuoteDialogResult(int? CreatedQuoteItemId, Data.Item? CreatedQuoteItem = null);