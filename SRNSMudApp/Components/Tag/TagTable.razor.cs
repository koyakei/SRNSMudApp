using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

using MudBlazor;

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
    [CascadingParameter] private Task<AuthenticationState>? AuthState { get; set; }

    [Parameter] public IEnumerable<Data.Tag>? Tags { get; set; }
    [Parameter] public IReadOnlyDictionary<int, int>? OverrideWeights { get; set; }
    [Parameter] public EventCallback OnDataChanged { get; set; }
    [Parameter] public EventCallback<Data.Tag> OnRemoveTag { get; set; }
    [Parameter] public bool ShowHeader { get; set; } = true;
    [Parameter] public bool ShowCreateButton { get; set; } = true;

    [Inject] private ITagTableDataProvider TagTableData { get; set; } = null!;
    [Inject] private NavigationManager NavigationManager { get; set; } = null!;
    [Inject] private IDialogLauncher DialogLauncher { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private string _currentUserId = "";
    private string _tagSearch = "";
    private List<Data.Tag> _allTagsCache = [];

    protected override async Task OnInitializedAsync()
    {
        if (AuthState is not null)
        {
            AuthenticationState authState = await AuthState;
            _currentUserId = authState.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        }

        _allTagsCache = await TagTableData.GetAllTagsAsync();
    }

    private bool FilterFunc(Data.Tag tag) => TagTableViewModel.FilterFunc(tag, _tagSearch);

    private async Task<IEnumerable<string>> SearchTags(string? value, CancellationToken _)
    {
        await Task.Yield();
        return TagTableViewModel.GetTagSearchSuggestions(Tags, value);
    }

    private readonly HashSet<int> _expandedTagIds = [];

    private void ToggleTagExpand(int tagId)
    {
        switch (_expandedTagIds.Remove(tagId))
        {
            case false:
                _ = _expandedTagIds.Add(tagId);
                break;
            case true:
                break;
        }
    }

    // ===== タグツリーポップオーバー用 =====
    private int? _activeTreeTagId;

    private void ToggleTree(int tagId)
    {
        _activeTreeTagId = (_activeTreeTagId == tagId) switch
        {
            true => null,
            false => tagId
        };
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

    private async Task AddTagToTagAsync(Data.Tag targetTag, Data.Tag selectedTag)
    {
        TagCardOperationResult result =
            await TagTableData.AddRelationAsync(targetTag.Id, selectedTag.Id, _currentUserId);
        switch (result)
        {
            case TagCardOperationResult.AlreadyExists:
                _ = Snackbar.Add("このタグは既に追加されています。", Severity.Warning);
                break;
            case TagCardOperationResult.Success:
                _ = Snackbar.Add("タグを追加しました。", Severity.Success);
                await NotifyDataChangedAsync();
                break;
            case TagCardOperationResult.NotFound:
            case TagCardOperationResult.NotOwner:
            default:
                break;
        }
    }

    private async Task RemoveTagToTagRelationAsync(TagRelationToTag relation)
    {
        switch (TagTableViewModel.CanRemoveRelation(relation, _currentUserId))
        {
            case true:
                await ExecuteRemoveTagToTagRelationAsync(relation);
                break;
            case false:
                _ = Snackbar.Add("関連付けの作成者本人ではないため、解除する権限がありません。", Severity.Error);
                break;
        }
    }

    private async Task ExecuteRemoveTagToTagRelationAsync(TagRelationToTag relation)
    {
        TagCardOperationResult result = await TagTableData.RemoveRelationAsync(relation.Id);
        switch (result)
        {
            case TagCardOperationResult.Success:
                await NotifyDataChangedAsync();
                _ = Snackbar.Add("タグの関連付けを解除しました。", Severity.Success);
                break;
            case TagCardOperationResult.NotFound:
                _ = Snackbar.Add("対象の関連付けが見つかりません。", Severity.Warning);
                break;
            case TagCardOperationResult.AlreadyExists:
            case TagCardOperationResult.NotOwner:
            default:
                break;
        }
    }

    private async Task EditTagAsync(Data.Tag tag)
    {
        switch (TagTableViewModel.CanEditTag(tag, _currentUserId))
        {
            case true:
                await ShowTagEditDialogAsync(tag);
                break;
            case false:
                _ = Snackbar.Add("タグの作成者本人ではないため、編集する権限がありません。", Severity.Error);
                break;
        }
    }

    private async Task ShowTagEditDialogAsync(Data.Tag tag)
    {
        var options = new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Small, FullWidth = true };
        var parameters = new DialogParameters { ["Tag"] = tag };

        IDialogReference dialog = await DialogLauncher.ShowAsync<TagEditDialog>("タグの編集", parameters, options);
        DialogResult? result = await dialog.Result;

        switch (result)
        {
            case { Canceled: false }:
                await ExecutePostEditTagAsync();
                break;
            default:
                break;
        }
    }

    private async Task ExecutePostEditTagAsync()
    {
        await NotifyDataChangedAsync();
        _ = Snackbar.Add("タグを更新しました。", Severity.Success);
    }

    private async Task DeleteTagAsync(Data.Tag tag)
    {
        switch (TagTableViewModel.CanDeleteTag(tag, _currentUserId))
        {
            case true:
                await ExecuteDeleteTagAsync(tag);
                break;
            case false when tag.IsSystem:
                _ = Snackbar.Add("システムタグは削除できません。", Severity.Error);
                break;
            case false:
                _ = Snackbar.Add("タグの作成者本人ではないため、削除する権限がありません。", Severity.Error);
                break;
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "UI 層で発生した例外の内容をユーザーへ通知するために広く捕捉する")]
    private async Task ExecuteDeleteTagAsync(Data.Tag tag)
    {
        try
        {
            if (await TagTableData.DeleteTagAsync(tag.Id))
            {
                await NotifyDataChangedAsync();
                _ = Snackbar.Add("タグを削除しました。", Severity.Success);
            }
            else
            {
                _ = Snackbar.Add("対象のタグが既に削除されているか、見つかりません。", Severity.Warning);
            }
        }
        catch (Exception ex)
        {
            _ = Snackbar.Add($"エラーが発生しました: {ex.Message}", Severity.Error);
        }
    }

    private async Task NotifyDataChangedAsync()
    {
        await (OnDataChanged.HasDelegate switch
        {
            true => OnDataChanged.InvokeAsync(),
            false => Task.CompletedTask
        });
    }
}