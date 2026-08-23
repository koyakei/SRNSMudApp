using Microsoft.AspNetCore.Components;

using MudBlazor;

using SRNSMudApp.Components.Contract;
using SRNSMudApp.Components.UI;
using SRNSMudApp.Services;
using SRNSMudApp.Services.Dialogs;

// 親名前空間 Tag より先に Data.Tag 型を解決させるため、エイリアスを置く
using SRNSMudApp.Data;

namespace SRNSMudApp.Components.Tag;

using Item = SRNSMudApp.Data.Item;
using Tag = SRNSMudApp.Data.Tag;
/// <summary>
///     ItemTagChip のコードビハインド。
///     マークアップ (.razor) 側は表示のみを担い、タグ操作・ダイアログ起動などの
///     UI オーケストレーションはこちらに集約する。純粋な表示計算は <see cref="ItemTagChipViewModel" /> へ。
/// </summary>
public partial class ItemTagChip
{
    [Inject] private IDialogLauncher DialogLauncher { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private NavigationManager NavigationManager { get; set; } = null!;
    [Inject] private IItemTagService ItemTagService { get; set; } = null!;

    [Parameter][EditorRequired] public TagRelation TagRelation { get; set; } = null!;
    [Parameter][EditorRequired] public Item Item { get; set; } = null!;
    [Parameter] public string CurrentUserId { get; set; } = "";
    [Parameter] public IReadOnlyList<Tag> AllTags { get; set; } = [];
    [Parameter] public IReadOnlyList<TagRelationToTag> AllTagRelationsToTags { get; set; } = [];
    [Parameter] public TimelineEvent? HighlightEvent { get; set; }
    [Parameter] public EventCallback OnDataChanged { get; set; }
    [Parameter] public int ChipIndex { get; set; }
    [Parameter] public IReadOnlyList<string> ChipBackgrounds { get; set; } = ["#EEEDFE"];
    [Parameter] public IReadOnlyList<string> ChipTextColors { get; set; } = ["#26215C"];
    [Parameter] public bool ShowNameAndOwner { get; set; } = true;

    private bool _isTreePopoverOpen;

    private void ToggleTagTreePopover() => _isTreePopoverOpen = !_isTreePopoverOpen;

    /// <summary>親へデータ変更を通知する。</summary>
    private Task NotifyChangedAsync() =>
        OnDataChanged.HasDelegate ? OnDataChanged.InvokeAsync() : Task.CompletedTask;

    private async Task RemoveTagRelationAsync()
    {
        await ((TagRelation.OwnerId == CurrentUserId, TagRelation.Tag != null) switch
        {
            (false, true) => ProposeRemovalContractAsync(),
            (true, _) => ExecuteRemovalAsync(),
            _ => Task.CompletedTask
        });
    }

    private Task ProposeRemovalContractAsync() =>
        ProposeContractAsync(-TagRelation.Weight, isRemovalRequest: true);

    /// <summary>自分以外のタグの Weight 変更・削除をコントラクト提案ダイアログ経由で行う。</summary>
    private async Task ProposeContractAsync(int weightDelta, bool isRemovalRequest)
    {
        var parameters = new DialogParameters<ProposeContractDialog>
        {
            { x => x.TargetItem, Item },
            { x => x.RequestedTag, TagRelation.Tag },
            { x => x.WeightDelta, weightDelta },
            { x => x.IsRemovalRequest, isRemovalRequest }
        };
        var options = new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Medium, FullWidth = true };
        IDialogReference dialog = await DialogLauncher.ShowAsync<ProposeContractDialog>("コントラクトの提案", parameters, options);
        DialogResult? result = await dialog.Result;

        await ((result?.Canceled == true) switch
        {
            false => NotifyChangedAsync(),
            _ => Task.CompletedTask
        });
    }

    private async Task ExecuteRemovalAsync()
    {
        await (await ItemTagService.RemoveTagRelationAsync(TagRelation.Id, CurrentUserId) switch
        {
            null => HandleSuccessfulRemoval(),
            string err => HandleError(err)
        });
    }

    private Task HandleSuccessfulRemoval()
    {
        var localRelation = Item.TagRelations.FirstOrDefault(tr => tr.Id == TagRelation.Id);
        _ = localRelation switch
        {
            not null => Item.TagRelations.Remove(localRelation),
            _ => false
        };

        _ = Snackbar.Add("タグの関連付けを解除しました。", Severity.Success);
        return NotifyChangedAsync();
    }

    private Task HandleError(string error)
    {
        _ = Snackbar.Add(error, Severity.Error);
        return Task.CompletedTask;
    }

    private async Task UpdateTagRelationWeightAsync(int delta)
    {
        await ((TagRelation.OwnerId == CurrentUserId, TagRelation.Tag != null) switch
        {
            (false, true) => ProposeUpdateContractAsync(delta),
            (true, _) => ExecuteWeightUpdateAsync(delta),
            _ => Task.CompletedTask
        });
    }

    private Task ProposeUpdateContractAsync(int delta) =>
        ProposeContractAsync(delta, isRemovalRequest: false);

    private async Task ExecuteWeightUpdateAsync(int delta)
    {
        UpdateWeightResult result = await ItemTagService.UpdateTagWeightAsync(TagRelation.Id, delta, CurrentUserId);
        await (result switch
        {
            UpdateWeightResult.Success => NotifyChangedAsync(),
            UpdateWeightResult.NoPermission => HandleError("関連付けた本人ではないため、Weightを変更する権限がありません。"),
            UpdateWeightResult.NotFound => HandleError("タグの関連付けが見つかりません。"),
            _ => HandleError("不明なエラーが発生しました。")
        });
    }

    private async Task EditTagRelationWeightAsync()
    {
        await ((TagRelation.OwnerId == CurrentUserId) switch
        {
            false => HandleError("関連付けた本人ではないため、Weightを変更する権限がありません。"),
            true => ShowEditWeightDialogAsync()
        });
    }

    private async Task ShowEditWeightDialogAsync()
    {
        var parameters = new DialogParameters { ["Weight"] = TagRelation.Weight };
        var options = new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.ExtraSmall, FullWidth = true };
        IDialogReference dialog = await DialogLauncher.ShowAsync<WeightEditDialog>("Weightの一括変更", parameters, options);
        DialogResult? result = await dialog.Result;

        await ((result?.Canceled == false, result?.Data is int) switch
        {
            (true, true) => HandleWeightEditResult((int)result.Data!),
            _ => Task.CompletedTask
        });
    }

    private async Task HandleWeightEditResult(int newWeight)
    {
        await ((newWeight == TagRelation.Weight) switch
        {
            true => Task.CompletedTask,
            false => ExecuteSetWeightAsync(newWeight)
        });
    }

    private async Task ExecuteSetWeightAsync(int newWeight)
    {
        var error = await ItemTagService.SetTagWeightAsync(TagRelation.Id, newWeight, CurrentUserId);
        await (error switch
        {
            null => NotifyChangedAsync(),
            _ => HandleError(error)
        });
    }

    private async Task ChangeItemTagAsync(int newTagId)
    {
        await ((TagRelation.TagId == newTagId) switch
        {
            true => Task.CompletedTask,
            false => (TagRelation.OwnerId == CurrentUserId) switch
            {
                false => HandleError("関連付けた本人ではないため、変更する権限がありません。"),
                true => ExecuteChangeTagAsync(newTagId)
            }
        });
    }

    private async Task ExecuteChangeTagAsync(int newTagId)
    {
        var error = await ItemTagService.ChangeItemTagAsync(TagRelation.Id, newTagId, Item.Id, CurrentUserId);
        await (error switch
        {
            null => HandleSuccessfulChangeTag(),
            _ => HandleError(error)
        });
    }

    private Task HandleSuccessfulChangeTag()
    {
        _ = Snackbar.Add("タグを変更しました。", Severity.Success);
        _isTreePopoverOpen = false;
        return NotifyChangedAsync();
    }

    private async Task OnAddTagToTagClicked(Tag? targetTag)
    {
        await (targetTag switch
        {
            null => Task.CompletedTask,
            not null => ExecuteWithTagSelection("関連タグの追加", targetTag, ExecuteAddTagToTagAsync)
        });
    }

    /// <summary>タグ選択ダイアログを表示し、選択されたタグで処理を実行する共通フロー。</summary>
    private async Task ExecuteWithTagSelection(
        string title, Tag targetTag, Func<Tag, Tag, Task> execute)
    {
        var options = new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Large, FullWidth = true };
        IDialogReference dialog = await DialogLauncher.ShowAsync<TagAddDialog>(title, options);
        DialogResult? result = await dialog.Result;

        if (result is not { Canceled: false })
        {
            return;
        }

        if (result.Data is not Tag selectedTag)
        {
            return;
        }

        await execute(targetTag, selectedTag);
    }

    private async Task ExecuteAddTagToTagAsync(Tag targetTag, Tag selectedTag)
    {
        var error = await ItemTagService.AddTagToTagAsync(targetTag.Id, selectedTag.Id, CurrentUserId);
        await (error switch
        {
            null => HandleSuccessMessage("タグを追加しました。"),
            _ => HandleError(error)
        });
    }

    private async Task RemoveTagToTagRelationAsync(TagRelationToTag relation)
    {
        await ((relation.OwnerId == CurrentUserId) switch
        {
            false => HandleError("関連付けた本人ではないため、解除する権限がありません。"),
            true => ExecuteRemoveTagToTagAsync(relation.Id)
        });
    }

    private async Task ExecuteRemoveTagToTagAsync(int relationId)
    {
        var error = await ItemTagService.RemoveTagToTagRelationAsync(relationId, CurrentUserId);
        await (error switch
        {
            null => HandleSuccessMessage("タグの関連付けを解除しました。"),
            _ => HandleError(error)
        });
    }

    private async Task OnAddChildTagFromTree(Tag? targetTag)
    {
        await (targetTag switch
        {
            null => Task.CompletedTask,
            not null => ExecuteWithTagSelection("子タグの追加", targetTag, ExecuteSetParentTagAsync)
        });
    }

    private async Task ExecuteSetParentTagAsync(Tag parentTag, Tag childTag)
    {
        var error = await ItemTagService.SetParentTagAsync(parentTag.Id, childTag.Id, CurrentUserId, AllTags);
        await (error switch
        {
            null => HandleSuccessMessage("子タグとして設定しました。"),
            _ => HandleError(error)
        });
    }

    private Task HandleSuccessMessage(string message)
    {
        _ = Snackbar.Add(message, Severity.Success);
        return NotifyChangedAsync();
    }
}