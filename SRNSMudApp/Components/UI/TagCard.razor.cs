using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

using MudBlazor;

using SRNSMudApp.Components.Tag;
using SRNSMudApp.Data;
using SRNSMudApp.Services;
using SRNSMudApp.Services.Dialogs;

namespace SRNSMudApp.Components.UI;

// 兄弟名前空間 SRNSMudApp.Components.Tag が同名型と解決されるため、
// エイリアスを名前空間の内側に置く
using Tag = SRNSMudApp.Data.Tag;

/// <summary>
///     TagCard のコードビハインド。
///     マークアップ (.razor) 側は表示のみを担い、JS 連携・投票・タグ操作・ダイアログ起動などの
///     UI オーケストレーションはこちらに集約する。純粋な計算は <see cref="TagCardViewModel" /> へ。
/// </summary>
public partial class TagCard : IAsyncDisposable
{
    [Inject] private ITagCardDataProvider TagCardData { get; set; } = null!;
    [Inject] private IDialogLauncher DialogLauncher { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private IJSRuntime JS { get; set; } = null!;
    [Inject] private NavigationManager NavigationManager { get; set; } = null!;

    [Parameter][EditorRequired] public Tag Tag { get; set; } = null!;
    [Parameter] public EventCallback OnDataChanged { get; set; }
    [Parameter] public bool IsFocused { get; set; }
    [Parameter] public EventCallback<int> OnFocus { get; set; }
    [Parameter] public List<Tag> AllTags { get; set; } = [];
    [Parameter] public string CurrentUserId { get; set; } = "";
    [Parameter] public int? CurrentUserGoodTagId { get; set; }
    [Parameter] public int? CurrentUserBadTagId { get; set; }
    [Parameter] public EventCallback OnEnsureSystemTags { get; set; }
    [Parameter] public List<TimelineEvent>? HighlightEvents { get; set; }

    private bool _areTagsExpanded;
    private int _activePopoverTagId = -1;
    private string _activePopoverChipKey = "";

    private string GetTagCardStyle() => ItemCardViewModel.GetItemCardStyle(IsFocused);

    [JSInvokable]
    public void OnElementFocusedByScroll(string elementId)
    {
        switch (elementId == $"tag-card-{Tag.Id}")
        {
            case true:
                OnFocus.InvokeAsync(Tag.Id);
                break;
        }
    }

    private DotNetObjectReference<TagCard>? _dotNetRef;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        switch (firstRender)
        {
            case true:
                _dotNetRef = DotNetObjectReference.Create(this);
                try
                {
                    await JS.InvokeVoidAsync("contentOverflowHelper.initScrollObserver");
                }
                catch (JSException)
                {
                    // ignored
                }

                try
                {
                    await JS.InvokeVoidAsync("contentOverflowHelper.observeElements", $"#tag-card-{Tag.Id}");
                }
                catch (JSException)
                {
                    // ignored
                }
                break;
        }
    }

    public async ValueTask DisposeAsync()
    {
        switch (_dotNetRef)
        {
            case not null:
                try
                {
                    await JS.InvokeVoidAsync("contentOverflowHelper.removeDotNetRef", _dotNetRef);
                }
                catch (JSException)
                {
                    // ignored
                }

                _dotNetRef.Dispose();
                break;
        }
    }

    private void ToggleTagTagExpand() => _areTagsExpanded = !_areTagsExpanded;

    private void ClosePopover()
    {
        _activePopoverTagId = -1;
        _activePopoverChipKey = "";
    }

    /// <summary>親へデータ変更を通知する。</summary>
    private async Task NotifyChangedAsync()
    {
        switch (OnDataChanged.HasDelegate)
        {
            case true:
                await OnDataChanged.InvokeAsync();
                break;
        }
    }

    private async Task ToggleTagTreePopover(int tagId, string chipKey)
    {
        switch (_activePopoverTagId == tagId && _activePopoverChipKey == chipKey)
        {
            case true:
                ClosePopover();
                break;
            case false:
                _activePopoverTagId = tagId;
                _activePopoverChipKey = chipKey;
                StateHasChanged();
                await Task.Yield();
                try
                {
                    await JS.InvokeVoidAsync("contentOverflowHelper.scrollToElement", ".tag-tree-popover-content .tag-tree-line.current");
                }
                catch (JSException)
                {
                    // ignored
                }
                break;
        }
    }

    // --- Voting Logic ---
    private async Task UpvoteTagAsync() => await ToggleTagVoteAsync(true);

    private async Task DownvoteTagAsync() => await ToggleTagVoteAsync(false);

    private async Task ToggleTagVoteAsync(bool isUpvote)
    {
        switch (string.IsNullOrEmpty(CurrentUserId))
        {
            case true:
                Snackbar.Add("ログインが必要です。", Severity.Warning);
                return;
        }

        switch (OnEnsureSystemTags.HasDelegate)
        {
            case true:
                await OnEnsureSystemTags.InvokeAsync();
                break;
        }

        switch (!CurrentUserGoodTagId.HasValue || !CurrentUserBadTagId.HasValue)
        {
            case true:
                Snackbar.Add("システムタグの取得に失敗しました。", Severity.Error);
                return;
        }

        var targetSystemTagId = isUpvote ? CurrentUserGoodTagId.Value : CurrentUserBadTagId.Value;
        var oppositeSystemTagId = isUpvote ? CurrentUserBadTagId.Value : CurrentUserGoodTagId.Value;

        await TagCardData.ToggleTagVoteAsync(Tag.Id, CurrentUserId, targetSystemTagId, oppositeSystemTagId);
        await NotifyChangedAsync();
    }

    // --- Tag Operations ---
    private async Task OnAddTagToTagClicked(Tag? targetTag)
    {
        await (targetTag switch
        {
            null => Task.CompletedTask,
            not null => ExecuteWithTagSelection("関連タグの追加", targetTag, AddTagToTagAsync)
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

    private async Task AddTagToTagAsync(Tag targetTag, Tag selectedTag)
    {
        TagCardOperationResult result =
            await TagCardData.AddTagToTagAsync(targetTag.Id, selectedTag.Id, CurrentUserId);
        switch (result)
        {
            case TagCardOperationResult.AlreadyExists:
                Snackbar.Add("このタグは既に追加されています。", Severity.Warning);
                return;
            case TagCardOperationResult.Success:
                Snackbar.Add("タグを追加しました。", Severity.Success);
                await NotifyChangedAsync();
                break;
        }
    }

    private async Task RemoveTagToTagRelationAsync(TagRelationToTag relation)
    {
        switch (TagCardViewModel.IsRelationOwner(relation.OwnerId, CurrentUserId))
        {
            case false:
                Snackbar.Add("関連付けた本人ではないため、解除する権限がありません。", Severity.Error);
                return;
        }

        TagCardOperationResult result = await TagCardData.RemoveRelationAsync(relation.Id, CurrentUserId);
        switch (result)
        {
            case TagCardOperationResult.Success:
                Snackbar.Add("タグの関連付けを解除しました。", Severity.Success);
                await NotifyChangedAsync();
                break;
        }
    }

    private async Task UpdateTagToTagWeightAsync(TagRelationToTag relation, int delta)
    {
        switch (TagCardViewModel.IsRelationOwner(relation.OwnerId, CurrentUserId))
        {
            case false:
                Snackbar.Add("関連付けた本人ではないため、Weightを変更する権限がありません。", Severity.Error);
                return;
        }

        TagCardOperationResult result =
            await TagCardData.UpdateRelationWeightAsync(relation.Id, delta, CurrentUserId);
        switch (result)
        {
            case TagCardOperationResult.Success:
                await NotifyChangedAsync();
                break;
        }
    }

    private async Task EditTagToTagWeightAsync(TagRelationToTag relation)
    {
        switch (TagCardViewModel.IsRelationOwner(relation.OwnerId, CurrentUserId))
        {
            case false:
                Snackbar.Add("関連付けた本人ではないため、Weightを変更する権限がありません。", Severity.Error);
                return;
        }

        var parameters = new DialogParameters { ["Weight"] = relation.Weight };
        var options = new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.ExtraSmall, FullWidth = true };
        IDialogReference dialog = await DialogLauncher.ShowAsync<WeightEditDialog>("Weightの一括変更", parameters, options);
        DialogResult? result = await dialog.Result;

        switch (result)
        {
            case { Canceled: false, Data: int newWeight }:
                {
                    switch (TagCardViewModel.HasWeightChange(relation.Weight, newWeight))
                    {
                        case false: return;
                    }

                    TagCardOperationResult opResult =
                        await TagCardData.SetRelationWeightAsync(relation.Id, newWeight, CurrentUserId);
                    switch (opResult)
                    {
                        case TagCardOperationResult.Success:
                            await NotifyChangedAsync();
                            break;
                    }

                    break;
                }
        }
    }

    private async Task ChangeTagTagAsync(TagRelationToTag oldRelation, int newTagId)
    {
        switch (TagCardViewModel.IsSameTagChange(oldRelation.TagId, newTagId))
        {
            case true: return;
        }

        switch (TagCardViewModel.IsRelationOwner(oldRelation.OwnerId, CurrentUserId))
        {
            case false:
                Snackbar.Add("関連付けた本人ではないため、変更する権限がありません。", Severity.Error);
                return;
        }

        TagCardOperationResult result =
            await TagCardData.ChangeRelationTagAsync(oldRelation.Id, Tag.Id, newTagId, CurrentUserId);
        switch (result)
        {
            case TagCardOperationResult.AlreadyExists:
                Snackbar.Add("変更先のタグは既に追加されています。", Severity.Warning);
                return;
            case TagCardOperationResult.Success:
                Snackbar.Add("タグを変更しました。", Severity.Success);
                ClosePopover();
                await NotifyChangedAsync();
                break;
        }
    }

    private async Task OnAddChildTagFromTree(Tag? targetTag)
    {
        await (targetTag switch
        {
            null => Task.CompletedTask,
            not null => ExecuteWithTagSelection("子タグの追加", targetTag, SetParentTagAsync)
        });
    }

    private async Task SetParentTagAsync(Tag parentTag, Tag childTag)
    {
        switch (TagCardViewModel.IsSelfParent(parentTag, childTag))
        {
            case true:
                Snackbar.Add("自分自身を親にすることはできません。", Severity.Warning);
                return;
        }

        switch (TagCardViewModel.HasParentCycle(parentTag, childTag, AllTags))
        {
            case true:
                Snackbar.Add("循環参照になるため親に設定できません。", Severity.Error);
                return;
        }

        TagCardOperationResult result =
            await TagCardData.SetParentTagAsync(childTag.Id, parentTag.Id, CurrentUserId);
        switch (result)
        {
            case TagCardOperationResult.NotOwner:
                Snackbar.Add("対象タグの作成者ではないため、親タグを変更する権限がありません。", Severity.Error);
                return;
            case TagCardOperationResult.Success:
                Snackbar.Add("子タグとして設定しました。", Severity.Success);
                await NotifyChangedAsync();
                break;
        }
    }
}