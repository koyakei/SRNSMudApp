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

        if (item.Content == null || !item.Content.Contains(selectedText, StringComparison.Ordinal))
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
                    var linkUrl = $"/ItemDetail/{success.Value.Id}";
                    int index = item.Content.IndexOf(selectedText, StringComparison.Ordinal);
                    item.Content = item.Content.Remove(index, selectedText.Length).Insert(index, linkUrl);
                    _ = _snackbar.Add("アイテムを分割しました。", Severity.Success);
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

        if (item.Content == null || !item.Content.Contains(selectedText, StringComparison.Ordinal))
        {
            _ = _snackbar.Add("選択したテキストがこのアイテムの本文に含まれていません。", Severity.Error);
            return null;
        }

        var parameters = new DialogParameters<SplitRequestConfirmDialog>
        {
            { x => x.SelectedText, selectedText }
        };
        var options = new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Small, FullWidth = true };
        IDialogReference dialog = await _dialogLauncher.ShowAsync<SplitRequestConfirmDialog>("アイテム分割リクエストの送信", parameters, options);
        DialogResult? result = await dialog.Result;

        if (result is { Canceled: false })
        {
            Result<ItemSplitRequest> splitResult = await _itemSplitService.RequestSplitAsync(item.Id, selectedText, currentUserId);
            switch (splitResult)
            {
                case Success<ItemSplitRequest> success:
                    _ = _snackbar.Add("アイテム分割リクエストを送信しました。", Severity.Success);
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
                var linkUrl = $"/ItemDetail/{success.Value.Id}";
                int index = item.Content.IndexOf(request.SelectedText, StringComparison.Ordinal);
                if (index >= 0)
                {
                    item.Content = item.Content.Remove(index, request.SelectedText.Length).Insert(index, linkUrl);
                }
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
}