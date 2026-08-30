// IDE0010 / IDE0072: union 型・enum の網羅的 switch に対する「Populate switch」は、
// 全ケース列挙済み・default 併記済みでも解消されない解析器の誤検知のため抑制する。
#pragma warning disable IDE0010, IDE0072

using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

using MudBlazor;

using SRNSMudApp.Components.Tag;
using SRNSMudApp.Data;
using SRNSMudApp.Services;
using SRNSMudApp.Services.Dialogs;

namespace SRNSMudApp.Components.UI;

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

    [Parameter][EditorRequired] public Data.Tag Tag { get; set; } = null!;
    [Parameter] public EventCallback OnDataChanged { get; set; }
    [Parameter] public bool IsFocused { get; set; }
    [Parameter] public EventCallback<int> OnFocus { get; set; }
    [Parameter] public IReadOnlyList<Data.Tag> AllTags { get; set; } = [];
    [Parameter] public string CurrentUserId { get; set; } = "";
    [Parameter] public int? CurrentUserGoodTagId { get; set; }
    [Parameter] public int? CurrentUserBadTagId { get; set; }
    [Parameter] public EventCallback OnEnsureSystemTags { get; set; }
    [Parameter] public IReadOnlyList<TimelineEvent>? HighlightEvents { get; set; }

    private bool _areTagsExpanded;
    private int _activePopoverTagId = -1;
    private string _activePopoverChipKey = "";

    private string GetTagCardStyle() => ItemCardViewModel.GetItemCardStyle(IsFocused);

    [JSInvokable]
    public void OnElementFocusedByScroll(string elementId)
    {
        if (elementId == $"tag-card-{Tag.Id}")
        {
            _ = OnFocus.InvokeAsync(Tag.Id);
        }
    }

    private DotNetObjectReference<TagCard>? _dotNetRef;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
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
        }
    }

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        if (_dotNetRef is not null)
        {
            try
            {
                await JS.InvokeVoidAsync("contentOverflowHelper.removeDotNetRef", _dotNetRef);
            }
            catch (JSDisconnectedException)
            {
                // ignored
            }
            catch (TaskCanceledException)
            {
                // ignored
            }
            catch (JSException)
            {
                // ignored
            }

            _dotNetRef.Dispose();
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
        if (OnDataChanged.HasDelegate)
        {
            await OnDataChanged.InvokeAsync();
        }
    }

    private async Task ToggleTagTreePopover(int tagId, string chipKey)
    {
        if (_activePopoverTagId == tagId && _activePopoverChipKey == chipKey)
        {
            ClosePopover();
        }
        else
        {
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
        }
    }

    // --- Voting Logic ---
    private async Task UpvoteTagAsync() => await ToggleTagVoteAsync(true);

    private async Task DownvoteTagAsync() => await ToggleTagVoteAsync(false);

    private async Task ToggleTagVoteAsync(bool isUpvote)
    {
        if (string.IsNullOrEmpty(CurrentUserId))
        {
            _ = Snackbar.Add("ログインが必要です。", Severity.Warning);
            return;
        }

        if (OnEnsureSystemTags.HasDelegate)
        {
            await OnEnsureSystemTags.InvokeAsync();
        }

        if (!CurrentUserGoodTagId.HasValue || !CurrentUserBadTagId.HasValue)
        {
            _ = Snackbar.Add("システムタグの取得に失敗しました。", Severity.Error);
            return;
        }

        var targetSystemTagId = isUpvote ? CurrentUserGoodTagId.Value : CurrentUserBadTagId.Value;
        var oppositeSystemTagId = isUpvote ? CurrentUserBadTagId.Value : CurrentUserGoodTagId.Value;

        await TagCardData.ToggleTagVoteAsync(Tag.Id, CurrentUserId, targetSystemTagId, oppositeSystemTagId);
        await NotifyChangedAsync();
    }

    // --- Tag Operations ---
    private async Task OnAddTagToTagClicked(Data.Tag? targetTag)
    {
        await (targetTag switch
        {
            null => Task.CompletedTask,
            not null => ExecuteWithTagSelection("関連タグの追加", targetTag, AddTagToTagAsync)
        });
    }

    /// <summary>タグ選択ダイアログを表示し、選択されたタグで処理を実行する共通フロー。</summary>
    private async Task ExecuteWithTagSelection(
        string title, Data.Tag targetTag, Func<Data.Tag, Data.Tag, Task> execute)
    {
        var options = new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Large, FullWidth = true };
        IDialogReference dialog = await DialogLauncher.ShowAsync<TagAddDialog>(title, options);
        DialogResult? result = await dialog.Result;

        if (result is not { Canceled: false })
        {
            return;
        }

        if (result.Data is not Data.Tag selectedTag)
        {
            return;
        }

        await execute(targetTag, selectedTag);
    }

    private async Task AddTagToTagAsync(Data.Tag targetTag, Data.Tag selectedTag)
    {
        TagCardOperationResult result =
            await TagCardData.AddTagToTagAsync(targetTag.Id, selectedTag.Id, CurrentUserId);
        switch (result)
        {
            case TagCardOperationResult.AlreadyExists:
                _ = Snackbar.Add("このタグは既に追加されています。", Severity.Warning);
                return;
            case TagCardOperationResult.Success:
                _ = Snackbar.Add("タグを追加しました。", Severity.Success);
                await NotifyChangedAsync();
                break;
            case TagCardOperationResult.NotFound:
            case TagCardOperationResult.NotOwner:
                break;
        }
    }

    private async Task RemoveTagToTagRelationAsync(TagRelationToTag relation)
    {
        if (!TagCardViewModel.IsRelationOwner(relation.OwnerId, CurrentUserId))
        {
            _ = Snackbar.Add("関連付けた本人ではないため、解除する権限がありません。", Severity.Error);
            return;
        }

        TagCardOperationResult result = await TagCardData.RemoveRelationAsync(relation.Id, CurrentUserId);
        switch (result)
        {
            case TagCardOperationResult.Success:
                _ = Snackbar.Add("タグの関連付けを解除しました。", Severity.Success);
                await NotifyChangedAsync();
                break;
            case TagCardOperationResult.NotFound:
            case TagCardOperationResult.NotOwner:
                break;
        }
    }

    private async Task UpdateTagToTagWeightAsync(TagRelationToTag relation, int delta)
    {
        if (!TagCardViewModel.IsRelationOwner(relation.OwnerId, CurrentUserId))
        {
            _ = Snackbar.Add("関連付けた本人ではないため、Weightを変更する権限がありません。", Severity.Error);
            return;
        }

        TagCardOperationResult result =
            await TagCardData.UpdateRelationWeightAsync(relation.Id, delta, CurrentUserId);
        switch (result)
        {
            case TagCardOperationResult.Success:
                await NotifyChangedAsync();
                break;
            case TagCardOperationResult.NotFound:
            case TagCardOperationResult.NotOwner:
                break;
        }
    }

    private async Task EditTagToTagWeightAsync(TagRelationToTag relation)
    {
        if (!TagCardViewModel.IsRelationOwner(relation.OwnerId, CurrentUserId))
        {
            _ = Snackbar.Add("関連付けた本人ではないため、Weightを変更する権限がありません。", Severity.Error);
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
                        case TagCardOperationResult.NotFound:
                        case TagCardOperationResult.NotOwner:
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

        if (!TagCardViewModel.IsRelationOwner(oldRelation.OwnerId, CurrentUserId))
        {
            _ = Snackbar.Add("関連付けた本人ではないため、変更する権限がありません。", Severity.Error);
            return;
        }

        TagCardOperationResult result =
            await TagCardData.ChangeRelationTagAsync(oldRelation.Id, Tag.Id, newTagId, CurrentUserId);
        switch (result)
        {
            case TagCardOperationResult.AlreadyExists:
                _ = Snackbar.Add("変更先のタグは既に追加されています。", Severity.Warning);
                return;
            case TagCardOperationResult.Success:
                _ = Snackbar.Add("タグを変更しました。", Severity.Success);
                ClosePopover();
                await NotifyChangedAsync();
                break;
            case TagCardOperationResult.NotFound:
            case TagCardOperationResult.NotOwner:
                break;
        }
    }

    private async Task OnAddChildTagFromTree(Data.Tag? targetTag)
    {
        await (targetTag switch
        {
            null => Task.CompletedTask,
            not null => ShowCreateChildDialogAsync(targetTag)
        });
    }

    private async Task ShowCreateChildDialogAsync(Data.Tag parentTag)
    {
        var parameters = new DialogParameters { [nameof(TagAddDialog.DefaultParentTag)] = parentTag };
        var options = new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Large, FullWidth = true };

        IDialogReference dialog = await DialogLauncher.ShowAsync<TagAddDialog>("子タグの追加", parameters, options);
        DialogResult? result = await dialog.Result;

        await (result switch
        {
            { Canceled: false, Data: Data.Tag createdTag } => HandleCreatedChildTagAsync(createdTag),
            _ => Task.CompletedTask
        });
    }

    private Task HandleCreatedChildTagAsync(Data.Tag createdTag)
    {
        _ = Snackbar.Add($"'{createdTag.Name}' を追加しました。", Severity.Success);
        return NotifyChangedAsync();
    }
}