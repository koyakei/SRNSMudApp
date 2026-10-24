using MudBlazor;

using SRNSMudApp.Components.Contract;
using SRNSMudApp.Components.Tag;
using SRNSMudApp.Data;
using SRNSMudApp.Resources;
using SRNSMudApp.Services.Dialogs;

namespace SRNSMudApp.Services;

/// <summary>
///     ItemCard におけるタグ追加・コントラクト提案モーダル操作の調整実装。
///     UI コンポーネントからモーダル遷移、自動承認判定、タグ付けエンティティ更新を分離する。
/// </summary>
public class ItemCardTagCoordinator(
    IItemCardDataProvider itemCardData,
    IDialogLauncher dialogLauncher,
    ISnackbar snackbar,
    Microsoft.Extensions.Logging.ILogger<ItemCardTagCoordinator>? logger = null) : IItemCardTagCoordinator
{
    private readonly IItemCardDataProvider _itemCardData =
        itemCardData ?? throw new ArgumentNullException(nameof(itemCardData));
    private readonly IDialogLauncher _dialogLauncher =
        dialogLauncher ?? throw new ArgumentNullException(nameof(dialogLauncher));
    private readonly ISnackbar _snackbar =
        snackbar ?? throw new ArgumentNullException(nameof(snackbar));
    private readonly Microsoft.Extensions.Logging.ILogger<ItemCardTagCoordinator>? _logger = logger;

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "データベース一時切断時にUIがクラッシュするのを防ぐため")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1848:Use the LoggerMessage delegates", Justification = "エラー発生時のみのフォールバックログのため")]
    public async Task<TagAddOutcome> PromptAndAddTagAsync(Item item, string currentUserId)
    {
        ArgumentNullException.ThrowIfNull(item);

        try
        {
            var options = new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Large, FullWidth = true };
            IDialogReference dialog = await _dialogLauncher.ShowAsync<TagAddDialog>("タグの追加", options);
            DialogResult? result = await dialog.Result;

            if (result is not { Canceled: false, Data: Tag selectedTag })
            {
                return TagAddOutcome.None;
            }

            Tag? tagFromDb = await _itemCardData.GetTagWithOwnerAsync(selectedTag.Id);
            if (tagFromDb is null)
            {
                return TagAddOutcome.None;
            }

            var canAttachDirectly = await _itemCardData.CanUserAttachTagDirectlyAsync(tagFromDb.Id, currentUserId);
            if (!canAttachDirectly)
            {
                return await ProposeTaggingContractAsync(item, tagFromDb);
            }

            var alreadyExists = item.TagRelations?.Any(tr => tr.TagId == selectedTag.Id) ?? false;
            if (alreadyExists)
            {
                _ = _snackbar.Add(ErrorMessages.TagAlreadyAdded, Severity.Warning);
                return TagAddOutcome.None;
            }

            TagRelation? newRelation = await _itemCardData.AddFreeTagRelationAsync(item.Id, selectedTag.Id, currentUserId);
            if (newRelation is not null)
            {
                item.TagRelations ??= [];
                newRelation.Tag = tagFromDb;
                item.TagRelations.Add(newRelation);
            }

            _ = _snackbar.Add("タグを追加しました。", Severity.Success);
            return TagAddOutcome.AddedDirectly;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "タグ追加処理中にエラーが発生しました (ItemId: {ItemId})", item.Id);
            _ = _snackbar.Add("データベースとの通信に失敗しました。時間をおいて再試行してください。", Severity.Error);
            return TagAddOutcome.None;
        }
    }

    private async Task<TagAddOutcome> ProposeTaggingContractAsync(Item item, Tag tagFromDb)
    {
        var parameters = new DialogParameters<ProposeContractDialog>
        {
            { x => x.TargetItem, item },
            { x => x.RequestedTag, tagFromDb },
            { x => x.WeightDelta, 1 }
        };
        var options = new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Medium, FullWidth = true };
        IDialogReference dialog = await _dialogLauncher.ShowAsync<ProposeContractDialog>("コントラクトの提案", parameters, options);
        DialogResult? result = await dialog.Result;

        return result is { Canceled: false } ? TagAddOutcome.ContractProposed : TagAddOutcome.None;
    }
}