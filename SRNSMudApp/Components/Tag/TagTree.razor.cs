using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

using MudBlazor;

using SRNSMudApp.Components.UI;
using SRNSMudApp.Data;
using SRNSMudApp.Services;
using SRNSMudApp.Services.Dialogs;

namespace SRNSMudApp.Components.Tag;

/// <summary>
///     TagTree ページのコードビハインド。
///     マークアップ (.razor) 側は表示のみを担い、jqTree との JS 連携・
///     UI オーケストレーションはこちらに集約する。
///     データアクセスおよびビジネスロジックは <see cref="TagTreeViewModel" /> へ委譲する。
/// </summary>
public partial class TagTree : IAsyncDisposable
{
    [Inject] private TagTreeViewModel ViewModel { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private IJSRuntime JSRuntime { get; set; } = null!;
    [Inject] private IDialogLauncher DialogLauncher { get; set; } = null!;
    [Inject] private NavigationManager NavigationManager { get; set; } = null!;

    [CascadingParameter] private Task<AuthenticationState>? AuthState { get; set; }

    private const string TreeContainerId = "jqtree-container";

#pragma warning disable IDE1006 // Naming Styles for Blazor bindings
    private string? _currentUserId => ViewModel.CurrentUserId;
#pragma warning restore IDE1006

    private string? _searchText;
    private DotNetObjectReference<TagTree>? _dotNetRef;
    private bool _isTreeInitialized;
    private bool _dataLoaded;

    [SupplyParameterFromQuery(Name = "tagId")]
    public int? SelectedTagId { get; set; }

    protected override async Task OnInitializedAsync()
    {
        string? currentUserId = null;
        var isAdmin = false;
        if (AuthState is not null)
        {
            AuthenticationState authState = await AuthState;
            currentUserId = authState.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            isAdmin = authState.User.IsInRole("Admin");
        }

        ViewModel.SetUser(currentUserId, isAdmin);
        await LoadDataAsync();
        _dataLoaded = true;
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "UI 層で発生した例外の内容をユーザーへ通知するために広く捕捉する")]
    private async Task LoadDataAsync()
    {
        try
        {
            await ViewModel.LoadDataAsync();
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
        if (!_isTreeInitialized && _dataLoaded)
        {
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
        }
    }

    private IEnumerable<Data.Tag> GetFilteredTags() => TagTreeViewModel.FilterTags(ViewModel.Tags, _searchText, _currentUserId);

    private IReadOnlySet<int> GetHighlightedTagIds() => TagTreeViewModel.GetMatchingTagIds(ViewModel.Tags, _searchText);

    private string GetSerializedTreeData() =>
        TagTreeViewModel.SerializeTreeData(
            GetFilteredTags(),
            ViewModel.PendingMoves,
            _currentUserId,
            ViewModel.LockedTagIds,
            GetHighlightedTagIds());

    /// <summary>初期化済みの場合、jqTree 側のデータを現在のフィルタ結果で差し替える。</summary>
    private async Task ReloadTreeDataAsync()
    {
        if (!_isTreeInitialized)
        {
            return;
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
        if (string.IsNullOrEmpty(_currentUserId))
        {
            return;
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
                _ = Snackbar.Add("削除するタグが選択されていません。", Severity.Info);
                return;
            default:
                break;
        }

        try
        {
            TagTreeDeleteResult result = await ViewModel.DeleteTagsAsync(selectedIds);

            if (result.UnauthorizedNames.Count > 0)
            {
                _ = Snackbar.Add($"削除権限がないためスキップしました: {string.Join(", ", result.UnauthorizedNames)}", Severity.Warning);
            }

            if (result.SystemNames.Count > 0)
            {
                _ = Snackbar.Add($"システムタグは削除できないためスキップしました: {string.Join(", ", result.SystemNames)}", Severity.Warning);
            }

            if (result.HasDeleted)
            {
                _ = Snackbar.Add($"{result.DeletedCount}個のタグを削除しました。", Severity.Success);
                StateHasChanged();
                await ReloadTreeDataAsync();
            }
        }
        catch (Exception ex)
        {
            _ = Snackbar.Add($"削除中にエラーが発生しました: {ex.Message}", Severity.Error);
        }
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
            StateHasChanged();
            await ReloadTreeDataAsync();
        }
    }

    [JSInvokable]
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "UI 層で発生した例外の内容をユーザーへ通知するために広く捕捉する")]
    public async Task AddChildTagByNodeId(int parentId)
    {
        if (string.IsNullOrEmpty(_currentUserId))
        {
            return;
        }

        IDialogReference dialog = await DialogLauncher.ShowAsync<TagCreateChildDialog>("子タグの追加");
        DialogResult? result = await dialog.Result;

        switch (result)
        {
            case { Canceled: false, Data: TagCreateChildDialog.Result data }:
                TagCardActionResult actionResult = await ViewModel.AddChildTagAsync(parentId, data.Name, data.Content);
                await ApplyResultAsync(actionResult);
                break;
            default:
                break;
        }
    }

    [JSInvokable]
    public async Task OnTreeMove(int movedNodeId, int targetNodeId, string position)
    {
        TagCardActionResult result = await ViewModel.MoveTagAsync(movedNodeId, targetNodeId, position);
        if (result.Type == TagCardActionResultType.NoOp)
        {
            await ReloadTreeDataAsync();
            return;
        }

        await ApplyResultAsync(result);
    }

    [JSInvokable]
    public void NavigateToTagDetail(int tagId) => NavigationManager.NavigateTo($"/TagDetail/{tagId}");

    [JSInvokable]
    public void OnNodeSelected(int? nodeId)
    {
        var uri = NavigationManager.GetUriWithQueryParameter("tagId", nodeId);
        NavigationManager.NavigateTo(uri, false, true);
    }

    [JSInvokable]
    public async Task CancelMoveRequest(int requestId)
    {
        TagCardActionResult result = await ViewModel.CancelMoveRequestAsync(requestId);
        if (result.Type != TagCardActionResultType.NoOp)
        {
            await ApplyResultAsync(result);
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "破棄時の例外は無視する必要がある（テレダウン処理）")]
    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        _dotNetRef?.Dispose();
        try
        {
            if (_isTreeInitialized)
            {
                try
                {
                    await JSRuntime.InvokeVoidAsync("jqTreeInterop.destroy", TreeContainerId);
                }
                catch (JSException)
                {
                    // ignored
                }
            }
        }
        catch
        {
            // Ignore during teardown
        }
    }
}