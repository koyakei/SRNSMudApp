using System.Diagnostics.CodeAnalysis;

using Microsoft.JSInterop;

using MudBlazor;

using SRNSMudApp.Components.UI;
using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Resources;
using SRNSMudApp.Services.Dialogs;

#pragma warning disable IDE0010, IDE0072

namespace SRNSMudApp.Services;

/// <summary>
///     ItemCard におけるテキスト分割（直接分割・リクエスト送信・承認・却下・取消）の調整実装。
///     UI コンポーネントから JS 連携、モーダル起動、サービス呼び出し、Snackbar 通知を分離する。
/// </summary>
public class ItemCardSplitCoordinator(
    IItemSplitService itemSplitService,
    IDialogLauncher dialogLauncher,
    ISnackbar snackbar,
    IJSRuntime js) : IItemCardSplitCoordinator
{
    private readonly IItemSplitService _itemSplitService =
        itemSplitService ?? throw new ArgumentNullException(nameof(itemSplitService));
    private readonly IDialogLauncher _dialogLauncher =
        dialogLauncher ?? throw new ArgumentNullException(nameof(dialogLauncher));
    private readonly ISnackbar _snackbar =
        snackbar ?? throw new ArgumentNullException(nameof(snackbar));
    private readonly IJSRuntime _js =
        js ?? throw new ArgumentNullException(nameof(js));

    /// <inheritdoc />
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "UI 層の操作失敗時に例外メッセージを Snackbar で安全に通知するため")]
    public async Task<bool> SplitSelectionAsync(Item item, string currentUserId)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.OwnerId != currentUserId)
        {
            _ = _snackbar.Add(ErrorMessages.NotAuthorizedToEdit, Severity.Error);
            return false;
        }

        string selectedText;
        try
        {
            selectedText = await _js.InvokeAsync<string>("selectionHelper.getSelectedText");
        }
        catch (JSException)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(selectedText))
        {
            _ = _snackbar.Add("分割するテキストが選択されていません。", Severity.Warning);
            return false;
        }

        string normalizedContent = (item.Content ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal);
        string normalizedSelected = selectedText.Replace("\r\n", "\n", StringComparison.Ordinal);

        if (item.Content == null || !normalizedContent.Contains(normalizedSelected, StringComparison.Ordinal))
        {
            _ = _snackbar.Add("選択したテキストがこのアイテムの本文に含まれていません。", Severity.Error);
            return false;
        }

        try
        {
            Result<Item> splitResult = await _itemSplitService.SplitDirectlyAsync(item.Id, selectedText, currentUserId);
            switch (splitResult)
            {
                case Success<Item> success:
                    item.Content = ReplaceSelectedTextWithLink(item.Content, selectedText, success.Value.Id);
                    _ = _snackbar.Add("アイテムを分割しました。", Severity.Success);
                    try
                    {
                        await _js.InvokeVoidAsync("selectionHelper.clearSelection");
                    }
                    catch (JSException)
                    {
                    }

                    return true;
                case Failure fail:
                    _ = _snackbar.Add(fail.ErrorMessage, Severity.Error);
                    return false;
                default:
                    return false;
            }
        }
        catch (Exception ex)
        {
            _ = _snackbar.Add($"エラーが発生しました: {ex.Message}", Severity.Error);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<ItemSplitRequest?> RequestSplitSelectionAsync(Item item, string currentUserId)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (string.IsNullOrWhiteSpace(currentUserId))
        {
            _ = _snackbar.Add("リクエストを送信するにはログインが必要です。", Severity.Warning);
            return null;
        }

        string selectedText;
        try
        {
            selectedText = await _js.InvokeAsync<string>("selectionHelper.getSelectedText");
        }
        catch (JSException)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(selectedText))
        {
            _ = _snackbar.Add("分割するテキストが選択されていません。", Severity.Warning);
            return null;
        }

        string reqNormalizedContent = (item.Content ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal);
        string reqNormalizedSelected = selectedText.Replace("\r\n", "\n", StringComparison.Ordinal);

        if (item.Content == null || !reqNormalizedContent.Contains(reqNormalizedSelected, StringComparison.Ordinal))
        {
            _ = _snackbar.Add("選択したテキストがこのアイテムの本文に含まれていません。", Severity.Error);
            return null;
        }

        // 引用コメント入力付きの分割リクエスト確認ダイアログを表示する
        DialogParameters<SplitRequestWithQuoteDialog> parameters = new()
        {
            { x => x.SelectedText, selectedText },
            { x => x.QuotedItem, item },
            { x => x.CurrentUserId, currentUserId }
        };
        DialogOptions options = new() { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Small, FullWidth = true };
        IDialogReference dialog = await _dialogLauncher.ShowAsync<SplitRequestWithQuoteDialog>(
            "アイテム分割リクエストの送信", parameters, options);
        DialogResult? result = await dialog.Result;

        if (result is { Canceled: false, Data: SplitRequestWithQuoteDialogResult dialogResult })
        {
            // 引用コメントがある場合は先に引用Itemを作成する
            int? quoteItemId = dialogResult.CreatedQuoteItemId;

            Result<ItemSplitRequest> splitResult = await _itemSplitService.RequestSplitAsync(
                item.Id, selectedText, currentUserId, quoteItemId);
            switch (splitResult)
            {
                case Success<ItemSplitRequest> success:
                    _ = _snackbar.Add("アイテム分割リクエストを送信しました。", Severity.Success);
                    try
                    {
                        await _js.InvokeVoidAsync("selectionHelper.clearSelection");
                    }
                    catch (JSException)
                    {
                    }

                    return success.Value;
                case Failure fail:
                    _ = _snackbar.Add(fail.ErrorMessage, Severity.Error);
                    return null;
                default:
                    return null;
            }
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<bool> ApproveSplitRequestAsync(Item item, ItemSplitRequest request, string currentUserId)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(request);

        if (item.OwnerId != currentUserId)
        {
            _ = _snackbar.Add(ErrorMessages.NotAuthorizedToEdit, Severity.Error);
            return false;
        }

        Result<Item> result = await _itemSplitService.ApproveSplitAsync(request.Id, currentUserId);
        switch (result)
        {
            case Success<Item> success:
                _ = _snackbar.Add("分割リクエストを承認しました。", Severity.Success);
                item.Content = ReplaceSelectedTextWithLink(item.Content, request.SelectedText, success.Value.Id);
                return true;
            case Failure fail:
                _ = _snackbar.Add(fail.ErrorMessage, Severity.Error);
                return false;
            default:
                return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> RejectSplitRequestAsync(Item item, ItemSplitRequest request, string currentUserId)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(request);

        if (item.OwnerId != currentUserId)
        {
            _ = _snackbar.Add(ErrorMessages.NotAuthorizedToEdit, Severity.Error);
            return false;
        }

        var options = new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Small, FullWidth = true };
        IDialogReference dialog = await _dialogLauncher.ShowAsync<RejectRequestDialog>("分割リクエストを却下", options);
        DialogResult? result = await dialog.Result;

        if (result is { Canceled: false })
        {
            var comment = result.Data as string;
            Result<bool> rejectResult = await _itemSplitService.RejectSplitAsync(request.Id, currentUserId, comment);
            switch (rejectResult)
            {
                case Success<bool>:
                    _ = _snackbar.Add("分割リクエストを却下しました。", Severity.Success);
                    return true;
                case Failure fail:
                    _ = _snackbar.Add(fail.ErrorMessage, Severity.Error);
                    return false;
                default:
                    return false;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public async Task<bool> CancelSplitRequestAsync(ItemSplitRequest request, string currentUserId)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.RequesterUserId != currentUserId)
        {
            _ = _snackbar.Add("リクエストを取り下げる権限がありません。", Severity.Error);
            return false;
        }

        Result<bool> cancelResult = await _itemSplitService.CancelSplitAsync(request.Id, currentUserId);
        switch (cancelResult)
        {
            case Success<bool>:
                _ = _snackbar.Add("分割リクエストを取り下げました。", Severity.Success);
                return true;
            case Failure fail:
                _ = _snackbar.Add(fail.ErrorMessage, Severity.Error);
                return false;
            default:
                return false;
        }
    }

    private static string ReplaceSelectedTextWithLink(string content, string selectedText, int createdItemId)
    {
        string linkUrl = $"/ItemDetail/{createdItemId}";
        int index = content.IndexOf(selectedText, StringComparison.Ordinal);
        int lengthToReplace = selectedText.Length;

        if (index < 0)
        {
            string crlfSelected = selectedText.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal);
            int crlfIndex = content.IndexOf(crlfSelected, StringComparison.Ordinal);
            if (crlfIndex >= 0)
            {
                index = crlfIndex;
                lengthToReplace = crlfSelected.Length;
            }
        }

        return index >= 0
            ? content.Remove(index, lengthToReplace).Insert(index, linkUrl)
            : content;
    }
}