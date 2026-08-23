using System.Security.Claims;

using System.Diagnostics.CodeAnalysis;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;

using MudBlazor;

using SRNSMudApp.Services;
using SRNSMudApp.Services.Dialogs;

namespace SRNSMudApp.Components.Tag;

// 親名前空間 Tag より先に Data.Tag 型を解決させるため、エイリアスを置く

using Tag = SRNSMudApp.Data.Tag;

/// <summary>
///     TagTree ページのコードビハインド。
///     マークアップ (.razor) 側は表示のみを担い、jqTree との JS 連携・
///     タグの追加 / 削除 / 移動オーケストレーションはこちらに集約する。
///     純粋なツリー構築ロジックは <see cref="TagTreeViewModel" /> へ。
/// </summary>
public partial class TagTree : IAsyncDisposable
{
    [Inject] private ITagTreeDataProvider TagTreeData { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private IJSRuntime JSRuntime { get; set; } = null!;
    [Inject] private IDialogLauncher DialogLauncher { get; set; } = null!;
    [Inject] private NavigationManager NavigationManager { get; set; } = null!;

    [CascadingParameter] private Task<AuthenticationState>? AuthState { get; set; }

    private const string TreeContainerId = "jqtree-container";

    private List<Tag> _tags = [];
    private string? _searchText;
    private DotNetObjectReference<TagTree>? _dotNetRef;
    private bool _isTreeInitialized;
    private bool _dataLoaded;
    private string? _currentUserId;

    [SupplyParameterFromQuery(Name = "tagId")]
    public int? SelectedTagId { get; set; }

    protected override async Task OnInitializedAsync()
    {
        switch (AuthState)
        {
            case not null:
                var authState = await AuthState;
                _currentUserId = authState.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                break;
        }

        await LoadDataAsync();
        _dataLoaded = true;
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "UI 層で発生した例外の内容をユーザーへ通知するために広く捕捉する")]
    private async Task LoadDataAsync()
    {
        try
        {
            _tags = await TagTreeData.LoadTagsAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TagTree] LoadDataAsync ERROR: {ex.GetType().Name}: {ex.Message}");
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // _dataLoaded が true になるまでツリーを初期化しない
        // （OnInitializedAsync の await 中に firstRender が先に来る場合がある）
        switch (!_isTreeInitialized && _dataLoaded)
        {
            case true:
                _dotNetRef = DotNetObjectReference.Create(this);
                var treeDataJson = GetSerializedTreeData();
                var isLoggedIn = !string.IsNullOrEmpty(_currentUserId);
                try
                {
                    await JSRuntime.InvokeVoidAsync("jqTreeInterop.init", TreeContainerId, treeDataJson, _dotNetRef, isLoggedIn, SelectedTagId);
                }
                catch (JSException)
                {
                    // ignored
                }

                _isTreeInitialized = true;
                break;
        }
    }

    private IEnumerable<Tag> GetFilteredTags() => TagTreeViewModel.FilterTags(_tags, _searchText, _currentUserId);

    private string GetSerializedTreeData() => TagTreeViewModel.SerializeTreeData(GetFilteredTags());

    /// <summary>初期化済みの場合、jqTree 側のデータを現在のフィルタ結果で差し替える。</summary>
    private async Task ReloadTreeDataAsync()
    {
        switch (_isTreeInitialized)
        {
            case false: return;
        }

        var treeDataJson = GetSerializedTreeData();
        try
        {
            await JSRuntime.InvokeVoidAsync("jqTreeInterop.loadData", TreeContainerId, treeDataJson);
        }
        catch (JSException)
        {
            // ignored
        }
    }

    private async Task OnSearchTextChanged(string? text)
    {
        _searchText = text;
        await ReloadTreeDataAsync();
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "UI 層で発生した例外の内容をユーザーへ通知するために広く捕捉する")]
    private async Task DeleteSelectedTags()
    {
        switch (string.IsNullOrEmpty(_currentUserId))
        {
            case true: return;
        }

        List<int> selectedIds = [];
        try
        {
            selectedIds = await JSRuntime.InvokeAsync<List<int>>("jqTreeInterop.getSelectedIds", TreeContainerId);
        }
        catch (JSException)
        {
            // ignored
        }

        switch (selectedIds)
        {
            case null:
            case { Count: 0 }:
                Snackbar.Add("削除するタグが選択されていません。", Severity.Info);
                return;
        }

        var hasDeleted = false;

        try
        {
            TagTreeDeleteResult result = await TagTreeData.DeleteTagsAsync(_currentUserId, selectedIds);

            switch (result.UnauthorizedNames.Count > 0)
            {
                case true:
                    Snackbar.Add($"削除権限がないためスキップしました: {string.Join(", ", result.UnauthorizedNames)}", Severity.Warning);
                    break;
            }

            switch (result.SystemNames.Count > 0)
            {
                case true:
                    Snackbar.Add($"システムタグは削除できないためスキップしました: {string.Join(", ", result.SystemNames)}", Severity.Warning);
                    break;
            }

            if (result.HasDeleted)
            {
                hasDeleted = true;
                Snackbar.Add($"{result.DeletedCount}個のタグを削除しました。", Severity.Success);
            }
        }
        catch (Exception ex)
        {
            Snackbar.Add($"削除中にエラーが発生しました: {ex.Message}", Severity.Error);
        }

        switch (hasDeleted)
        {
            case true:
                await LoadDataAsync();
                StateHasChanged();
                await ReloadTreeDataAsync();
                break;
        }
    }

    [JSInvokable]
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "UI 層で発生した例外の内容をユーザーへ通知するために広く捕捉する")]
    public async Task AddChildTagByNodeId(int parentId)
    {
        switch (string.IsNullOrEmpty(_currentUserId))
        {
            case true: return;
        }

        IDialogReference dialog = await DialogLauncher.ShowAsync<TagCreateChildDialog>("子タグの追加");
        DialogResult? result = await dialog.Result;

        switch (result)
        {
            case { Canceled: false, Data: TagCreateChildDialog.Result data }:
                try
                {
                    Tag newTag = new()
                    {
                        Name = data.Name,
                        Content = data.Content,
                        ParentTagId = parentId,
                        OwnerId = _currentUserId,
                        CachedWeight = 0,
                        CreatedDate = DateTime.UtcNow,
                        UpdatedDate = DateTime.UtcNow
                    };

                    await TagTreeData.AddTagAsync(newTag);

                    Snackbar.Add($"'{data.Name}' を追加しました。", Severity.Success);

                    await LoadDataAsync();
                    StateHasChanged();
                    await ReloadTreeDataAsync();
                }
                catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("UNIQUE constraint failed") == true)
                {
                    Snackbar.Add("同じ名前のタグが既に存在します。", Severity.Error);
                }
                catch (Exception ex)
                {
                    Snackbar.Add($"エラーが発生しました: {ex.Message}", Severity.Error);
                }
                break;
        }
    }

    /// <summary>移動要求が無効な場合に、クライアント側のツリー状態をサーバー側の状態で復元する。</summary>
    private async Task RejectTreeMoveAsync(string message, Severity severity)
    {
        Snackbar.Add(message, severity);
        StateHasChanged();
        await ReloadTreeDataAsync();
    }

    [JSInvokable]
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "UI 層で発生した例外の内容をユーザーへ通知するために広く捕捉する")]
    public async Task OnTreeMove(int movedNodeId, int targetNodeId, string position)
    {
        var movedItem = _tags.FirstOrDefault(t => t.Id == movedNodeId);
        var targetItem = _tags.FirstOrDefault(t => t.Id == targetNodeId);

        switch ((movedItem, targetItem))
        {
            case (null, _):
            case (_, null):
                return;
        }

        // 権限チェック: 自分のタグ以外は移動不可とする
        switch (!string.IsNullOrEmpty(movedItem.OwnerId) && movedItem.OwnerId != _currentUserId)
        {
            case true:
                await RejectTreeMoveAsync("他人が作成したタグの構成を変更する権限がありません。", Severity.Error);
                return;
        }

        // 自分自身の子孫へのドロップは無効（循環参照を防ぐ）
        // movedItem が targetItem の祖先（または自身）であるかを確認する
        switch (TagTreeViewModel.IsDescendantOrSelf(_tags, movedItem, targetItem))
        {
            case true:
                await RejectTreeMoveAsync(
                    $"'{movedItem.Name}' を自身の配下 '{targetItem.Name}' に移動することはできません。", Severity.Warning);
                return;
        }

        movedItem.ParentTagId = position switch
        {
            "inside" => targetItem.Id,
            "before" or "after" => targetItem.ParentTagId,
            _ => movedItem.ParentTagId
        };

        try
        {
            if (await TagTreeData.UpdateParentAsync(movedItem.Id, movedItem.ParentTagId))
            {
                Snackbar.Add("タグ構造を更新しました。", Severity.Success);
            }
        }
        catch (Exception ex)
        {
            Snackbar.Add($"保存時にエラーが発生しました: {ex.Message}", Severity.Error);
        }

        StateHasChanged();

        // jqTreeのデータを更新
        await ReloadTreeDataAsync();
    }

    [JSInvokable]
    public void NavigateToTagDetail(int tagId) => NavigationManager.NavigateTo($"/TagDetail/{tagId}");

    [JSInvokable]
    public void OnNodeSelected(int? nodeId)
    {
        var uri = NavigationManager.GetUriWithQueryParameter("tagId", nodeId);
        NavigationManager.NavigateTo(uri, false, true);
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "破棄時の例外は無視する必要がある（テレダウン処理）")]
    public async ValueTask DisposeAsync()
    {
        _dotNetRef?.Dispose();
        try
        {
            switch (_isTreeInitialized)
            {
                case true:
                    try
                    {
                        await JSRuntime.InvokeVoidAsync("jqTreeInterop.destroy", TreeContainerId);
                    }
                    catch (JSException)
                    {
                        // ignored
                    }
                    break;
            }
        }
        catch
        {
            // Ignore during teardown
        }
    }
}