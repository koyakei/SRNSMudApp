using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

using MudBlazor;

using SRNSMudApp.Components.UI;
using SRNSMudApp.Data;
using SRNSMudApp.Services;
using SRNSMudApp.Services.Dialogs;

namespace SRNSMudApp.Components.Tag;

/// <summary>
///     TagTable のコードビハインド。
///     マークアップ (.razor) 側は表示のみを担い、タグ操作・ダイアログ起動などの
///     UI オーケストレーションはこちらに集約する。
/// </summary>
public partial class TagTable
{
    [CascadingParameter] private Task<AuthenticationState> AuthState { get; set; } = default!;

    [Parameter] public IEnumerable<Data.Tag>? Tags { get; set; }
    [Parameter] public IReadOnlyDictionary<int, int>? OverrideWeights { get; set; }
    [Parameter] public EventCallback OnDataChanged { get; set; }
    [Parameter] public EventCallback<Data.Tag> OnRemoveTag { get; set; }
    [Parameter] public bool ShowHeader { get; set; } = true;
    [Parameter] public bool ShowCreateButton { get; set; } = true;

    [Inject] private TagTableViewModel ViewModel { get; set; } = null!;
    [Inject] private IDialogLauncher DialogLauncher { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;

#pragma warning disable IDE1006 // Naming Styles for Blazor bindings
    private string _currentUserId => ViewModel.CurrentUserId;
    private bool _isAdmin => ViewModel.IsAdmin;
    private string _tagSearch = "";
    private List<Data.Tag> _allTagsCache => ViewModel.AllTagsCache;
#pragma warning restore IDE1006

    protected override async Task OnInitializedAsync()
    {
        string currentUserId = "";
        var isAdmin = false;
        if (AuthState is not null)
        {
            AuthenticationState authState = await AuthState;
            currentUserId = authState.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
            isAdmin = authState.User.IsInRole("Admin");
        }

        ViewModel.SetUser(currentUserId, isAdmin);
        await ViewModel.InitializeAsync();
    }

    private Task ReloadLockStatusAsync() => ViewModel.ReloadLockStatusAsync();

    private bool IsTagLocked(int tagId) => ViewModel.IsTagLocked(tagId);

    private bool FilterFunc(Data.Tag tag) => TagTableViewModel.FilterFunc(tag, _tagSearch);

    private async Task<IEnumerable<string>> SearchTags(string? value, CancellationToken _)
    {
        await Task.Yield();
        return TagTableViewModel.GetTagSearchSuggestions(Tags, value);
    }

    private readonly HashSet<int> _expandedTagIds = [];

    private void ToggleTagExpand(int tagId)
    {
        if (!_expandedTagIds.Remove(tagId))
        {
            _ = _expandedTagIds.Add(tagId);
        }
    }

    // ===== タグツリーポップオーバー用 =====
    private int? _activeTreeTagId;

    private void ToggleTree(int tagId)
    {
        _activeTreeTagId = _activeTreeTagId == tagId ? null : tagId;
    }

    private async Task OnAddTagToTagClicked(Data.Tag targetTag)
    {
        var options = new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Large, FullWidth = true };
        IDialogReference dialog = await DialogLauncher.ShowAsync<TagAddDialog>("タグにタグを追加", options);
        DialogResult? result = await dialog.Result;

        await (result switch
        {
            { Canceled: false, Data: Data.Tag selectedTag } => AddTagToTagAsync(targetTag, selectedTag),
            _ => Task.CompletedTask
        });
    }

    private async Task ApplyResultAsync(TagCardActionResult result)
    {
        switch (result.Type)
        {
            case TagCardActionResultType.Warning:
                if (result.Message != null)
                {
                    _ = Snackbar.Add(result.Message, Severity.Warning);
                }
                break;
            case TagCardActionResultType.Error:
                if (result.Message != null)
                {
                    _ = Snackbar.Add(result.Message, Severity.Error);
                }
                break;
            case TagCardActionResultType.Success:
                if (result.Message != null)
                {
                    _ = Snackbar.Add(result.Message, Severity.Success);
                }
                break;
            case TagCardActionResultType.NoOp:
            default:
                break;
        }

        if (result.ShouldNotifyChanged)
        {
            await NotifyDataChangedAsync();
        }
    }

    private async Task AddTagToTagAsync(Data.Tag targetTag, Data.Tag selectedTag)
    {
        TagCardActionResult result = await ViewModel.AddRelationAsync(targetTag.Id, selectedTag.Id);
        await ApplyResultAsync(result);
    }

    private async Task RemoveTagToTagRelationAsync(TagRelationToTag relation)
    {
        TagCardActionResult result = await ViewModel.RemoveRelationAsync(relation);
        await ApplyResultAsync(result);
    }

    private async Task EditTagAsync(Data.Tag tag)
    {
        TagCardActionResult checkResult = ViewModel.CheckCanEditTag(tag);
        if (checkResult.Type == TagCardActionResultType.Success)
        {
            await ShowTagEditDialogAsync(tag);
        }
        else
        {
            await ApplyResultAsync(checkResult);
        }
    }

    private async Task ShowTagEditDialogAsync(Data.Tag tag)
    {
        var options = new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Small, FullWidth = true };
        var parameters = new DialogParameters { ["Tag"] = tag };

        IDialogReference dialog = await DialogLauncher.ShowAsync<TagEditDialog>("タグの編集", parameters, options);
        DialogResult? result = await dialog.Result;

        if (result is { Canceled: false })
        {
            await ExecutePostEditTagAsync();
        }
    }

    private async Task ExecutePostEditTagAsync()
    {
        await NotifyDataChangedAsync();
        _ = Snackbar.Add("タグを更新しました。", Severity.Success);
    }

    private async Task DeleteTagAsync(Data.Tag tag)
    {
        TagCardActionResult result = await ViewModel.DeleteTagAsync(tag);
        await ApplyResultAsync(result);
    }

    private async Task NotifyDataChangedAsync()
    {
        await ReloadLockStatusAsync();
        if (OnDataChanged.HasDelegate)
        {
            await OnDataChanged.InvokeAsync();
        }
    }
}