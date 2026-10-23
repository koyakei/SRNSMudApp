using System.Diagnostics.CodeAnalysis;

using Microsoft.JSInterop;

using MudBlazor;

using SRNSMudApp.Components.Contract;
using SRNSMudApp.Components.Tag;
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
    IItemQuoteService itemQuoteService,
    IItemCardVoteCoordinator voteCoordinator,
    IItemCardDataProvider itemCardDataProvider,
    IDialogLauncher dialogLauncher,
    ISnackbar snackbar,
    IJSRuntime js) : IItemCardSplitCoordinator
{
    private readonly IItemSplitService _itemSplitService =
        itemSplitService ?? throw new ArgumentNullException(nameof(itemSplitService));
    private readonly IItemQuoteService _itemQuoteService =
        itemQuoteService ?? throw new ArgumentNullException(nameof(itemQuoteService));
    private readonly IItemCardVoteCoordinator _voteCoordinator =
        voteCoordinator ?? throw new ArgumentNullException(nameof(voteCoordinator));
    private readonly IItemCardDataProvider _itemCardDataProvider =
        itemCardDataProvider ?? throw new ArgumentNullException(nameof(itemCardDataProvider));
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

        string? selectedText = await GetSelectedTextAsync();
        if (selectedText is null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(selectedText))
        {
            _ = _snackbar.Add("分割するテキストが選択されていません。", Severity.Warning);
            return false;
        }

        if (!ContainsSelectedText(item.Content, selectedText))
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
                    await ClearSelectionAsync();
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
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "UI 層の操作失敗時に例外メッセージを Snackbar で安全に通知し、ロールバックを行うため")]
    public async Task<ItemSplitRequest?> RequestSplitSelectionAsync(Item item, string currentUserId)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (string.IsNullOrWhiteSpace(currentUserId))
        {
            _ = _snackbar.Add("リクエストを送信するにはログインが必要です。", Severity.Warning);
            return null;
        }

        if (((IOwnable)item).IsOwnedBy(currentUserId))
        {
            _ = _snackbar.Add(ErrorMessages.SplitSelfItemNotAllowed, Severity.Error);
            return null;
        }

        string? selectedText = await GetSelectedTextAsync();
        if (selectedText is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(selectedText))
        {
            _ = _snackbar.Add("分割するテキストが選択されていません。", Severity.Warning);
            return null;
        }

        if (!ContainsSelectedText(item.Content, selectedText))
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
            int? quoteItemId = dialogResult.CreatedQuoteItemId;

            Result<ItemSplitRequest> splitResult;
            try
            {
                splitResult = await _itemSplitService.RequestSplitAsync(
                    item.Id, selectedText, currentUserId, quoteItemId);
            }
            catch (Exception ex)
            {
                if (quoteItemId.HasValue)
                {
                    await _itemCardDataProvider.DeleteItemAsync(quoteItemId.Value);
                }
                _ = _snackbar.Add($"分割リクエストの送信に失敗しました: {ex.Message}", Severity.Error);
                return null;
            }

            switch (splitResult)
            {
                case Success<ItemSplitRequest> success:
                    _ = _snackbar.Add("アイテム分割リクエストを送信しました。", Severity.Success);
                    await ClearSelectionAsync();
                    return success.Value;
                case Failure fail:
                    if (quoteItemId.HasValue)
                    {
                        await _itemCardDataProvider.DeleteItemAsync(quoteItemId.Value);
                    }
                    _ = _snackbar.Add(fail.ErrorMessage, Severity.Error);
                    return null;
                default:
                    if (quoteItemId.HasValue)
                    {
                        await _itemCardDataProvider.DeleteItemAsync(quoteItemId.Value);
                    }
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

    /// <inheritdoc />
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "UI 層の操作失敗時に例外メッセージを Snackbar で安全に通知し、ロールバックを行うため")]
    public async Task<ItemSplitWithReactionResult?> RequestSplitWithReactionAsync(
        Item item,
        string currentUserId,
        string reactionTagName,
        int targetWeight,
        int? reactionTagId,
        IReadOnlyList<Tag> allTags,
        Func<Task>? ensureSystemTagsAsync,
        string? selectedText = null)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (string.IsNullOrWhiteSpace(currentUserId))
        {
            _ = _snackbar.Add("リクエストを送信するにはログインが必要です。", Severity.Warning);
            return null;
        }

        if (string.IsNullOrWhiteSpace(selectedText))
        {
            selectedText = await GetSelectedTextAsync();
            if (selectedText is null)
            {
                return null;
            }
        }

        if (string.IsNullOrWhiteSpace(selectedText))
        {
            _ = _snackbar.Add("分割するテキストが選択されていません。", Severity.Warning);
            return null;
        }

        if (!ContainsSelectedText(item.Content, selectedText))
        {
            _ = _snackbar.Add("選択したテキストがこのアイテムの本文に含まれていません。", Severity.Error);
            return null;
        }

        // 1. 選択されたテキストを本文として引用アイテムを投稿
        Item? quoteItem;
        try
        {
            quoteItem = await _itemQuoteService.CreateQuoteItemAsync(item.Id, selectedText, currentUserId);
        }
        catch (Exception ex)
        {
            _ = _snackbar.Add($"引用アイテムの作成に失敗しました: {ex.Message}", Severity.Error);
            return null;
        }

        if (quoteItem is null)
        {
            _ = _snackbar.Add("引用アイテムの作成に失敗しました。", Severity.Error);
            return null;
        }

        // 2. 引用アイテムに対してリアクションタグを投票
        ItemVoteResult? voteResult;
        try
        {
            voteResult = await _voteCoordinator.ToggleReactionAsync(
                quoteItem.Id, currentUserId, reactionTagName, targetWeight, reactionTagId, allTags, ensureSystemTagsAsync);
        }
        catch (Exception ex)
        {
            await _itemCardDataProvider.DeleteItemAsync(quoteItem.Id);
            _ = _snackbar.Add($"リアクションの投票に失敗しました: {ex.Message}", Severity.Error);
            return null;
        }

        if (voteResult is null)
        {
            await _itemCardDataProvider.DeleteItemAsync(quoteItem.Id);
            _ = _snackbar.Add("リアクションの投票に失敗しました。", Severity.Error);
            return null;
        }

        // 3. 分割リクエストを送信 (RequesterQuoteItemId = quoteItem.Id)
        Result<ItemSplitRequest> splitResult;
        try
        {
            splitResult = await _itemSplitService.RequestSplitAsync(
                item.Id, selectedText, currentUserId, quoteItem.Id);
        }
        catch (Exception ex)
        {
            await _itemCardDataProvider.DeleteItemAsync(quoteItem.Id);
            _ = _snackbar.Add($"分割リクエストの送信に失敗しました: {ex.Message}", Severity.Error);
            return null;
        }

        switch (splitResult)
        {
            case Success<ItemSplitRequest> success:
                _ = _snackbar.Add("引用してリアクションを投票し、アイテム分割リクエストを送信しました。", Severity.Success);
                await ClearSelectionAsync();

                return new ItemSplitWithReactionResult(success.Value, voteResult, quoteItem);

            case Failure fail:
                await _itemCardDataProvider.DeleteItemAsync(quoteItem.Id);
                _ = _snackbar.Add(fail.ErrorMessage, Severity.Error);
                return null;

            default:
                await _itemCardDataProvider.DeleteItemAsync(quoteItem.Id);
                return null;
        }
    }

    /// <inheritdoc />
    public async Task<string?> GetValidSelectedTextForSplitAsync(Item item, string? currentUserId)
    {
        if (item is null || string.IsNullOrWhiteSpace(currentUserId))
        {
            return null;
        }

        if (((IOwnable)item).IsOwnedBy(currentUserId))
        {
            return null;
        }

        string? selectedText = await GetSelectedTextAsync();
        if (string.IsNullOrWhiteSpace(selectedText))
        {
            return null;
        }

        return ContainsSelectedText(item.Content, selectedText) ? selectedText : null;
    }

    /// <inheritdoc />
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "UI 層の操作失敗時に例外メッセージを Snackbar で安全に通知し、ロールバックを行うため")]
    public async Task<TagAddOutcome> RequestSplitWithTagAddAsync(
        Item item,
        string currentUserId,
        string? selectedText = null)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (string.IsNullOrWhiteSpace(currentUserId))
        {
            _ = _snackbar.Add("リクエストを送信するにはログインが必要です。", Severity.Warning);
            return TagAddOutcome.None;
        }

        if (string.IsNullOrWhiteSpace(selectedText))
        {
            selectedText = await GetSelectedTextAsync();
            if (selectedText is null)
            {
                return TagAddOutcome.None;
            }
        }

        if (string.IsNullOrWhiteSpace(selectedText))
        {
            _ = _snackbar.Add("分割するテキストが選択されていません。", Severity.Warning);
            return TagAddOutcome.None;
        }

        if (!ContainsSelectedText(item.Content, selectedText))
        {
            _ = _snackbar.Add("選択したテキストがこのアイテムの本文に含まれていません。", Severity.Error);
            return TagAddOutcome.None;
        }

        var options = new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Large, FullWidth = true };
        IDialogReference dialog = await _dialogLauncher.ShowAsync<TagAddDialog>("タグの追加", options);
        DialogResult? result = await dialog.Result;

        if (result is not { Canceled: false, Data: Tag selectedTag })
        {
            return TagAddOutcome.None;
        }

        Tag? tagFromDb = await _itemCardDataProvider.GetTagWithOwnerAsync(selectedTag.Id);
        if (tagFromDb is null)
        {
            return TagAddOutcome.None;
        }

        // 1. 選択されたテキストを本文として引用アイテムを投稿
        Item? quoteItem;
        try
        {
            quoteItem = await _itemQuoteService.CreateQuoteItemAsync(item.Id, selectedText, currentUserId);
        }
        catch (Exception ex)
        {
            _ = _snackbar.Add($"引用アイテムの作成に失敗しました: {ex.Message}", Severity.Error);
            return TagAddOutcome.None;
        }

        if (quoteItem is null)
        {
            _ = _snackbar.Add("引用アイテムの作成に失敗しました。", Severity.Error);
            return TagAddOutcome.None;
        }

        // 2. 引用アイテムに対して直接タグ付け、またはコントラクト提案を実行
        TagAddOutcome tagOutcome;
        try
        {
            bool canAttachDirectly = await _itemCardDataProvider.CanUserAttachTagDirectlyAsync(tagFromDb.Id, currentUserId);
            if (canAttachDirectly)
            {
                TagRelation? newRelation = await _itemCardDataProvider.AddFreeTagRelationAsync(
                    quoteItem.Id, tagFromDb.Id, currentUserId);
                if (newRelation is null)
                {
                    await _itemCardDataProvider.DeleteItemAsync(quoteItem.Id);
                    _ = _snackbar.Add("タグの追加に失敗しました。", Severity.Error);
                    return TagAddOutcome.None;
                }

                quoteItem.TagRelations ??= [];
                newRelation.Tag = tagFromDb;
                quoteItem.TagRelations.Add(newRelation);
                tagOutcome = TagAddOutcome.AddedDirectly;
            }
            else
            {
                var proposeParams = new DialogParameters<ProposeContractDialog>
                {
                    { x => x.TargetItem, quoteItem },
                    { x => x.RequestedTag, tagFromDb },
                    { x => x.WeightDelta, 1 }
                };
                var proposeOptions = new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Medium, FullWidth = true };
                IDialogReference proposeDialog = await _dialogLauncher.ShowAsync<ProposeContractDialog>(
                    "コントラクトの提案", proposeParams, proposeOptions);
                DialogResult? proposeResult = await proposeDialog.Result;

                if (proposeResult is not { Canceled: false })
                {
                    await _itemCardDataProvider.DeleteItemAsync(quoteItem.Id);
                    return TagAddOutcome.None;
                }

                tagOutcome = TagAddOutcome.ContractProposed;
            }
        }
        catch (Exception ex)
        {
            await _itemCardDataProvider.DeleteItemAsync(quoteItem.Id);
            _ = _snackbar.Add($"タグの追加に失敗しました: {ex.Message}", Severity.Error);
            return TagAddOutcome.None;
        }

        // 3. 分割リクエストを送信 (RequesterQuoteItemId = quoteItem.Id)
        Result<ItemSplitRequest> splitResult;
        try
        {
            splitResult = await _itemSplitService.RequestSplitAsync(
                item.Id, selectedText, currentUserId, quoteItem.Id);
        }
        catch (Exception ex)
        {
            await _itemCardDataProvider.DeleteItemAsync(quoteItem.Id);
            _ = _snackbar.Add($"分割リクエストの送信に失敗しました: {ex.Message}", Severity.Error);
            return TagAddOutcome.None;
        }

        switch (splitResult)
        {
            case Success<ItemSplitRequest>:
                _ = _snackbar.Add("引用してタグを追加し、アイテム分割リクエストを送信しました。", Severity.Success);
                await ClearSelectionAsync();
                return tagOutcome;

            case Failure fail:
                await _itemCardDataProvider.DeleteItemAsync(quoteItem.Id);
                _ = _snackbar.Add(fail.ErrorMessage, Severity.Error);
                return TagAddOutcome.None;

            default:
                await _itemCardDataProvider.DeleteItemAsync(quoteItem.Id);
                return TagAddOutcome.None;
        }
    }

    private async Task<string?> GetSelectedTextAsync()
    {
        try
        {
            return await _js.InvokeAsync<string>("selectionHelper.getSelectedText");
        }
        catch (JSException)
        {
            return null;
        }
    }

    private async Task ClearSelectionAsync()
    {
        try
        {
            await _js.InvokeVoidAsync("selectionHelper.clearSelection");
        }
        catch (JSException)
        {
            // ignored
        }
    }

    private static bool ContainsSelectedText(string? content, string selectedText)
    {
        if (content is null)
        {
            return false;
        }

        string normalizedContent = content.Replace("\r\n", "\n", StringComparison.Ordinal);
        string normalizedSelected = selectedText.Replace("\r\n", "\n", StringComparison.Ordinal);
        return normalizedContent.Contains(normalizedSelected, StringComparison.Ordinal);
    }

    private static string ReplaceSelectedTextWithLink(string content, string selectedText, int createdItemId)
    {
        string linkUrl = $"/ItemDetail/{createdItemId}";
        string crlf = selectedText.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal);
        string lf = selectedText.Replace("\r\n", "\n", StringComparison.Ordinal);

        ReadOnlySpan<string> candidates = [selectedText, crlf, lf];
        foreach (string candidate in candidates)
        {
            int index = content.IndexOf(candidate, StringComparison.Ordinal);
            if (index >= 0)
            {
                return content.Remove(index, candidate.Length).Insert(index, linkUrl);
            }
        }

        return content;
    }
}