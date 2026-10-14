namespace SRNSMudApp.Client.Components;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Components;

using MudBlazor;

using SRNSMudApp.Client.Models.Api;
using SRNSMudApp.Client.Services;

/// <summary>
/// タグツリー階層エクスプローラー WASM コンポーネント。
/// </summary>
public partial class TagTreeView : ComponentBase, IDisposable
{
    private readonly CancellationTokenSource _cts = new();

    [Inject]
    public required IItemListApiClient ApiClient { get; set; }

    [Inject]
    public required ISnackbar Snackbar { get; set; }

    public IReadOnlyList<TagTreeNodeDto> RootNodes { get; private set; } = [];

    public bool IsLoading { get; private set; } = true;

    public string SearchTerm { get; set; } = string.Empty;

    public TagTreeNodeDto? SelectedNode { get; private set; }

    public string NewChildTagName { get; set; } = string.Empty;

    public string? NewChildTagContent { get; set; }

    public bool IsCreatingChild { get; private set; }

    public bool IsEditingTag { get; private set; }

    public string EditTagName { get; set; } = string.Empty;

    public string? EditTagContent { get; set; }

    public bool IsUpdatingTag { get; private set; }

    public bool IsDeletingTag { get; private set; }

    public IReadOnlyList<ItemDto> RelatedItems { get; private set; } = [];

    public bool IsLoadingRelatedItems { get; private set; }

    public IReadOnlyList<TagTreeNodeDto> FilteredRootNodes
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SearchTerm))
            {
                return RootNodes;
            }

            var term = SearchTerm.Trim();
            return FilterNodes(RootNodes, term);
        }
    }

    protected override async Task OnInitializedAsync()
    {
        await LoadTreeAsync();
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "UI 表示向けにエラーメッセージへ集約するため")]
    public async Task LoadTreeAsync()
    {
        IsLoading = true;

        try
        {
            RootNodes = await ApiClient.GetTagTreeAsync(_cts.Token);

            if (SelectedNode != null)
            {
                SelectedNode = FindNodeById(RootNodes, SelectedNode.Id);
            }
        }
        catch (OperationCanceledException)
        {
            // キャンセル時は無視
        }
        catch (Exception ex)
        {
            Snackbar.Add($"タグツリーの読み込みに失敗しました: {ex.Message}", Severity.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void SelectNode(TagTreeNodeDto node)
    {
        ArgumentNullException.ThrowIfNull(node);

        SelectedNode = node;
        NewChildTagName = string.Empty;
        NewChildTagContent = null;
        IsEditingTag = false;
        EditTagName = node.Name;
        EditTagContent = node.Content;

        _ = LoadRelatedItemsAsync(node.Id);
    }

    public void StartEditTag()
    {
        if (SelectedNode != null)
        {
            EditTagName = SelectedNode.Name;
            EditTagContent = SelectedNode.Content;
            IsEditingTag = true;
        }
    }

    public void CancelEditTag()
    {
        IsEditingTag = false;
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "UI 表示向けにエラーメッセージへ集約するため")]
    public async Task UpdateSelectedTagAsync()
    {
        if (SelectedNode == null || string.IsNullOrWhiteSpace(EditTagName))
        {
            return;
        }

        IsUpdatingTag = true;
        try
        {
            var req = new UpdateTagRequest(EditTagName.Trim(), EditTagContent?.Trim());
            bool ok = await ApiClient.UpdateTagAsync(SelectedNode.Id, req, _cts.Token);
            if (ok)
            {
                Snackbar.Add($"タグ「{EditTagName}」を更新しました。", Severity.Success);
                IsEditingTag = false;
                await LoadTreeAsync();
            }
            else
            {
                Snackbar.Add("タグの更新に失敗しました。権限を確認してください。", Severity.Error);
            }
        }
        catch (Exception ex)
        {
            Snackbar.Add($"タグ更新エラー: {ex.Message}", Severity.Error);
        }
        finally
        {
            IsUpdatingTag = false;
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "UI 表示向けにエラーメッセージへ集約するため")]
    public async Task DeleteSelectedTagAsync()
    {
        if (SelectedNode == null)
        {
            return;
        }

        IsDeletingTag = true;
        try
        {
            bool ok = await ApiClient.DeleteTagAsync(SelectedNode.Id, _cts.Token);
            if (ok)
            {
                Snackbar.Add($"タグ「{SelectedNode.Name}」を削除しました。", Severity.Success);
                SelectedNode = null;
                await LoadTreeAsync();
            }
            else
            {
                Snackbar.Add("タグの削除に失敗しました。権限を確認してください。", Severity.Error);
            }
        }
        catch (Exception ex)
        {
            Snackbar.Add($"タグ削除エラー: {ex.Message}", Severity.Error);
        }
        finally
        {
            IsDeletingTag = false;
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "UI 表示向けにエラーメッセージへ集約するため")]
    public async Task LoadRelatedItemsAsync(int tagId)
    {
        IsLoadingRelatedItems = true;
        try
        {
            var req = new ItemListQueryRequest(
                Filters: [new ItemListFilterDto(TagId: tagId)],
                Take: 10
            );
            ItemListQueryResponse response = await ApiClient.QueryItemsAsync(req, _cts.Token);
            RelatedItems = response.Items;
        }
        catch
        {
            RelatedItems = [];
        }
        finally
        {
            IsLoadingRelatedItems = false;
            StateHasChanged();
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "UI 表示向けにエラーメッセージへ集約するため")]
    public async Task VoteTagAsync(int tagId, bool isUpvote)
    {
        try
        {
            bool success = await ApiClient.VoteTagAsync(tagId, isUpvote, _cts.Token);
            if (success)
            {
                Snackbar.Add(isUpvote ? "アップボートしました。" : "ダウンボートしました。", Severity.Success);
                await LoadTreeAsync();
            }
            else
            {
                Snackbar.Add("投票に失敗しました。ログイン状態を確認してください。", Severity.Error);
            }
        }
        catch (Exception ex)
        {
            Snackbar.Add($"投票エラー: {ex.Message}", Severity.Error);
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "UI 表示向けにエラーメッセージへ集約するため")]
    public async Task CreateChildTagAsync()
    {
        if (SelectedNode == null || string.IsNullOrWhiteSpace(NewChildTagName))
        {
            return;
        }

        IsCreatingChild = true;

        try
        {
            var request = new CreateTagRequest(
                Name: NewChildTagName.Trim(),
                Content: NewChildTagContent?.Trim(),
                ParentTagIds: [SelectedNode.Id]
            );

            TagDto? created = await ApiClient.CreateTagAsync(request, _cts.Token);
            if (created != null)
            {
                Snackbar.Add($"子タグ「{created.Name}」を作成しました。", Severity.Success);
                NewChildTagName = string.Empty;
                NewChildTagContent = null;
                await LoadTreeAsync();
            }
            else
            {
                Snackbar.Add("子タグの作成に失敗しました。", Severity.Error);
            }
        }
        catch (Exception ex)
        {
            Snackbar.Add($"子タグ作成エラー: {ex.Message}", Severity.Error);
        }
        finally
        {
            IsCreatingChild = false;
        }
    }

    private static List<TagTreeNodeDto> FilterNodes(IReadOnlyList<TagTreeNodeDto> nodes, string term)
    {
        var result = new List<TagTreeNodeDto>();

        foreach (var node in nodes)
        {
            var matchingChildren = FilterNodes(node.Children, term);
            var isSelfMatch = node.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                              (node.Content != null && node.Content.Contains(term, StringComparison.OrdinalIgnoreCase));

            if (isSelfMatch || matchingChildren.Count > 0)
            {
                result.Add(node with { Children = matchingChildren.Count > 0 ? matchingChildren : node.Children });
            }
        }

        return result;
    }

    private static TagTreeNodeDto? FindNodeById(IEnumerable<TagTreeNodeDto> nodes, int id)
    {
        foreach (var node in nodes)
        {
            if (node.Id == id)
            {
                return node;
            }

            var found = FindNodeById(node.Children, id);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cts.Cancel();
            _cts.Dispose();
        }
    }
}