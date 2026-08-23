using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

using MudBlazor;

using SRNSMudApp.Data;
using SRNSMudApp.Services;
using SRNSMudApp.Services.Dialogs;

// 親名前空間 SRNSMudApp.Components.Tag 自体が同名と解決されるため、
// エイリアスを名前空間の内側に置く

// IDE0010: union 型・enum の網羅的 switch に対する「Populate switch」は、
// 全ケース列挙済み・default 併記済みでも解消されない解析器の誤検知のため抑制する。
// IDE1006: tagSearch は元の .razor マークアップの @bind-Value が参照し続けるため命名を維持する。
#pragma warning disable IDE0010, IDE1006

namespace SRNSMudApp.Components.Tag;

using Tag = SRNSMudApp.Data.Tag;
/// <summary>
///     TagTable のコードビハインド。
///     マークアップ (.razor) 側は表示のみを担い、タグ操作・ダイアログ起動などの
///     UI オーケストレーションはこちらに集約する。
/// </summary>
public partial class TagTable
{
    [CascadingParameter] private Task<AuthenticationState>? AuthState { get; set; }

    [Parameter] public IEnumerable<Tag>? Tags { get; set; }
    [Parameter] public IReadOnlyDictionary<int, int>? OverrideWeights { get; set; }
    [Parameter] public EventCallback OnDataChanged { get; set; }
    [Parameter] public EventCallback<Tag> OnRemoveTag { get; set; }
    [Parameter] public bool ShowHeader { get; set; } = true;
    [Parameter] public bool ShowCreateButton { get; set; } = true;

    [Inject] private ITagTableDataProvider TagTableData { get; set; } = null!;
    [Inject] private NavigationManager NavigationManager { get; set; } = null!;
    [Inject] private IDialogLauncher DialogLauncher { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private string _currentUserId = "";
    private string tagSearch = "";
    private List<Tag> _allTagsCache = [];

    protected override async Task OnInitializedAsync()
    {
        switch (AuthState)
        {
            case not null:
                AuthenticationState authState = await AuthState;
                _currentUserId = authState.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
                break;
        }

        _allTagsCache = await TagTableData.GetAllTagsAsync();
    }

    private bool FilterFunc(Tag tag) => TagTableViewModel.FilterFunc(tag, tagSearch);

    private async Task<IEnumerable<string>> SearchTags(string? value, CancellationToken token)
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

    private async Task OnAddTagToTagClicked(Tag targetTag)
    {
        var options = new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Large, FullWidth = true };
        IDialogReference dialog = await DialogLauncher.ShowAsync<TagAddDialog>("タグにタグを追加", options);
        DialogResult? result = await dialog.Result;

        await (result switch
        {
            { Canceled: false, Data: Tag selectedTag } => AddTagToTagAsync(targetTag, selectedTag),
            _ => Task.CompletedTask
        });
    }

    private async Task AddTagToTagAsync(Tag targetTag, Tag selectedTag)
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
                _ = Snackbar.Add("関連付けた本人ではないため、解除する権限がありません。", Severity.Error);
                break;
        }
    }

    private async Task ExecuteRemoveTagToTagRelationAsync(TagRelationToTag relation)
    {
        TagCardOperationResult result = await TagTableData.RemoveRelationAsync(relation.Id);
        switch (result)
        {
            case TagCardOperationResult.Success:
                _ = Snackbar.Add("タグの関連付けを解除しました。", Severity.Success);
                await NotifyDataChangedAsync();
                break;
        }
    }

    private async Task EditTagAsync(Tag tag)
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

    private async Task ShowTagEditDialogAsync(Tag tag)
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
        }
    }

    private async Task ExecutePostEditTagAsync()
    {
        await NotifyDataChangedAsync();
        _ = Snackbar.Add("タグを更新しました。", Severity.Success);
    }

    private async Task DeleteTagAsync(Tag tag)
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
    private async Task ExecuteDeleteTagAsync(Tag tag)
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