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
    [Inject] private TagCardViewModel ViewModel { get; set; } = null!;
    [Inject] private IDialogLauncher DialogLauncher { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private IJSRuntime JS { get; set; } = null!;
    [Inject] private NavigationManager NavigationManager { get; set; } = null!;

    [Parameter][EditorRequired] public Data.Tag Tag { get; set; } = null!;
    [Parameter] public EventCallback OnDataChanged { get; set; }
    [Parameter] public bool IsFocused { get; set; }
    [Parameter] public EventCallback<int> OnFocus { get; set; }
    /// <summary>カードクリック時のコールバック。未指定時は OnFocus にフォールバックする。</summary>
    [Parameter] public EventCallback<int> OnCardClick { get; set; }
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

    private async Task HandleCardClickAsync()
    {
        if (OnCardClick.HasDelegate)
        {
            await OnCardClick.InvokeAsync(Tag.Id);
        }
        else if (OnFocus.HasDelegate)
        {
            await OnFocus.InvokeAsync(Tag.Id);
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
                await JS.InvokeVoidAsync("contentOverflowHelper.scrollToElement", ".tag-tree-popover-content #tag-tree-current-line");
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

    private async Task ApplyResultAsync(TagCardActionResult result)
    {
        switch (result.Type)
        {
            case TagCardActionResultType.Warning when result.Message != null:
                _ = Snackbar.Add(result.Message, Severity.Warning);
                break;
            case TagCardActionResultType.Error when result.Message != null:
                _ = Snackbar.Add(result.Message, Severity.Error);
                break;
            case TagCardActionResultType.Success when result.Message != null:
                _ = Snackbar.Add(result.Message, Severity.Success);
                break;
        }

        if (result.ShouldNotifyChanged)
        {
            await NotifyChangedAsync();
        }
    }

    private async Task ToggleTagVoteAsync(bool isUpvote)
    {
        if (OnEnsureSystemTags.HasDelegate)
        {
            await OnEnsureSystemTags.InvokeAsync();
        }

        TagCardActionResult result = await ViewModel.ToggleTagVoteAsync(
            Tag.Id, CurrentUserId, CurrentUserGoodTagId, CurrentUserBadTagId, isUpvote);
        await ApplyResultAsync(result);
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
        TagCardActionResult result =
            await ViewModel.AddTagToTagAsync(targetTag.Id, selectedTag.Id, CurrentUserId);
        await ApplyResultAsync(result);
    }

    private async Task RemoveTagToTagRelationAsync(TagRelationToTag relation)
    {
        TagCardActionResult result = await ViewModel.RemoveRelationAsync(relation, CurrentUserId);
        await ApplyResultAsync(result);
    }

    private async Task UpdateTagToTagWeightAsync(TagRelationToTag relation, int delta)
    {
        TagCardActionResult result =
            await ViewModel.UpdateRelationWeightAsync(relation, delta, CurrentUserId);
        await ApplyResultAsync(result);
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
                    TagCardActionResult opResult =
                        await ViewModel.SetRelationWeightAsync(relation, newWeight, CurrentUserId);
                    await ApplyResultAsync(opResult);
                    break;
                }
        }
    }

    private async Task ChangeTagTagAsync(TagRelationToTag oldRelation, int newTagId)
    {
        TagCardActionResult result =
            await ViewModel.ChangeRelationTagAsync(oldRelation, Tag.Id, newTagId, CurrentUserId);
        if (result.Type == TagCardActionResultType.Success)
        {
            ClosePopover();
        }

        await ApplyResultAsync(result);
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

    private async Task ReportTagAsync()
    {
        if (string.IsNullOrEmpty(CurrentUserId))
        {
            _ = Snackbar.Add("通報するにはログインが必要です。", Severity.Warning);
            return;
        }

        var options = new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Small, FullWidth = true };
        var parameters = new DialogParameters
        {
            [nameof(ReportContentDialog.TargetType)] = ReportTargetType.Tag,
            [nameof(ReportContentDialog.TagId)] = Tag.Id,
            [nameof(ReportContentDialog.TargetContent)] = $"タグ名: {Tag.Name}\n{Tag.Content}",
            [nameof(ReportContentDialog.TargetOwnerName)] = Tag.Owner?.UserName
        };

        _ = await DialogLauncher.ShowAsync<ReportContentDialog>("不適切な投稿を通報", parameters, options);
    }
}