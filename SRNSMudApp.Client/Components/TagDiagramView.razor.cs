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
/// タグダイアグラム &amp; 関係性エクスプローラー WASM コンポーネント。
/// </summary>
public partial class TagDiagramView : ComponentBase, IDisposable
{
    private readonly CancellationTokenSource _cts = new();

    [Inject]
    public required IItemListApiClient ApiClient { get; set; }

    [Inject]
    public required ISnackbar Snackbar { get; set; }

    [Parameter]
    public string? InitialItemIds { get; set; }

    public TagDiagramResponse? DiagramData { get; private set; }

    public bool IsLoading { get; private set; } = true;

    public string SearchTerm { get; set; } = string.Empty;

    public bool OnlyConnected { get; set; }

    public TagDto? SelectedTag { get; private set; }

    public int? SelectedParentTagIdToAdd { get; set; }

    public bool IsAddingParent { get; private set; }

    public IReadOnlyList<TagDto> FilteredTags
    {
        get
        {
            if (DiagramData == null)
            {
                return [];
            }

            IEnumerable<TagDto> query = DiagramData.Nodes;

            if (!string.IsNullOrWhiteSpace(SearchTerm))
            {
                var term = SearchTerm.Trim();
                query = query.Where(t =>
                    t.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    (t.Content != null && t.Content.Contains(term, StringComparison.OrdinalIgnoreCase)));
            }

            if (OnlyConnected)
            {
                query = query.Where(t => GetTagEdgeCount(t.Id) > 0);
            }

            return query.ToList();
        }
    }

    public IReadOnlyList<TagDto> ConnectedParents
    {
        get
        {
            if (SelectedTag == null || DiagramData == null)
            {
                return [];
            }

            var parentIds = DiagramData.Edges
                .Where(e => e.TargetTagId == SelectedTag.Id)
                .Select(e => e.SourceTagId)
                .ToHashSet();

            return DiagramData.Nodes.Where(n => parentIds.Contains(n.Id)).ToList();
        }
    }

    public IReadOnlyList<TagDto> ConnectedChildren
    {
        get
        {
            if (SelectedTag == null || DiagramData == null)
            {
                return [];
            }

            var childIds = DiagramData.Edges
                .Where(e => e.SourceTagId == SelectedTag.Id)
                .Select(e => e.TargetTagId)
                .ToHashSet();

            return DiagramData.Nodes.Where(n => childIds.Contains(n.Id)).ToList();
        }
    }

    public IReadOnlyList<TagDto> AvailableParents
    {
        get
        {
            if (SelectedTag == null || DiagramData == null)
            {
                return [];
            }

            var existingParentIds = ConnectedParents.Select(p => p.Id).ToHashSet();
            return DiagramData.Nodes
                .Where(n => n.Id != SelectedTag.Id && !existingParentIds.Contains(n.Id))
                .ToList();
        }
    }

    public IReadOnlyList<ItemDto> RelatedItems
    {
        get
        {
            if (SelectedTag == null || DiagramData == null)
            {
                return [];
            }

            return DiagramData.ContextItems
                .Where(i => i.TagRelations.Any(tr => tr.TagId == SelectedTag.Id))
                .ToList();
        }
    }

    protected override async Task OnInitializedAsync()
    {
        await LoadDiagramDataAsync();
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "UI 表示向けにエラーメッセージへ集約するため")]
    public async Task LoadDiagramDataAsync()
    {
        IsLoading = true;

        try
        {
            DiagramData = await ApiClient.GetTagDiagramDataAsync(InitialItemIds, _cts.Token);

            if (SelectedTag != null && DiagramData != null)
            {
                // 選択中ノードを再取得データから更新
                SelectedTag = DiagramData.Nodes.FirstOrDefault(t => t.Id == SelectedTag.Id);
            }
        }
        catch (OperationCanceledException)
        {
            // キャンセル時は無視
        }
        catch (Exception ex)
        {
            Snackbar.Add($"ダイアグラムデータの取得に失敗しました: {ex.Message}", Severity.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    public int GetTagEdgeCount(int tagId)
    {
        if (DiagramData == null)
        {
            return 0;
        }

        return DiagramData.Edges.Count(e => e.SourceTagId == tagId || e.TargetTagId == tagId);
    }

    public void SelectTag(TagDto tag)
    {
        SelectedTag = tag;
        SelectedParentTagIdToAdd = null;
    }

    public void SelectTagById(int tagId)
    {
        if (DiagramData == null)
        {
            return;
        }

        TagDto? found = DiagramData.Nodes.FirstOrDefault(n => n.Id == tagId);
        if (found != null)
        {
            SelectTag(found);
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
                await LoadDiagramDataAsync();
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
    public async Task AddParentTagAsync()
    {
        if (SelectedTag == null || !SelectedParentTagIdToAdd.HasValue)
        {
            return;
        }

        IsAddingParent = true;

        try
        {
            bool success = await ApiClient.AddParentTagAsync(SelectedTag.Id, SelectedParentTagIdToAdd.Value, _cts.Token);
            if (success)
            {
                Snackbar.Add("親タグを関連付けました。", Severity.Success);
                SelectedParentTagIdToAdd = null;
                await LoadDiagramDataAsync();
            }
            else
            {
                Snackbar.Add("親タグの関連付けに失敗しました。", Severity.Error);
            }
        }
        catch (Exception ex)
        {
            Snackbar.Add($"親タグ追加エラー: {ex.Message}", Severity.Error);
        }
        finally
        {
            IsAddingParent = false;
        }
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