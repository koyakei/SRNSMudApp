using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

using MudBlazor;

using SRNSMudApp.Client.Models;
using SRNSMudApp.Client.Models.Api;
using SRNSMudApp.Client.Services;

namespace SRNSMudApp.Client.Components;

/// <summary>
/// WASM 版のソート条件。
/// </summary>
public sealed record WasmSortCondition(int TagId, string TagName, bool Ascending)
{
    public bool Ascending { get; set; } = Ascending;
}

/// <summary>
/// WebAssembly (WASM) 上で動作するアイテム・タグの検索および一覧表示コンポーネント。
/// <see cref="IItemListApiClient"/> を通じてサーバー API を非同期に呼び出します。
/// </summary>
public sealed partial class ItemSearchView : ComponentBase
{
    [Inject]
    private IItemListApiClient ApiClient { get; set; } = null!;

    [Inject]
    private IJSRuntime JS { get; set; } = null!;

    [Inject]
    private ISnackbar? Snackbar { get; set; }

    /// <summary>
    /// アイテムクリック時に通知されるコールバック。
    /// </summary>
    [Parameter]
    public EventCallback<int> OnItemClick { get; set; }

    private MudAutocomplete<TagSuggestion>? _autocomplete;
    private string _searchText = string.Empty;
    private readonly List<TagSuggestion> _selectedFilters = [];
    private readonly List<WasmSortCondition> _sortConditions = [];
    private bool _isLoading;
    private string? _errorMessage;

    // アイテム追加フォーム用の状態
    private string _newItemContent = string.Empty;
    private bool _newItemIsPrivate;
    private int? _newItemTargetGroupId;
    private int? _quotingItemId;
    private string? _quotingItemContent;
    private bool _isAddingItem;
    private IReadOnlyList<UserGroupDto> _userGroups = [];

    // タグ追加フォーム用の状態
    private string _newTagName = string.Empty;
    private string _newTagContent = string.Empty;
    private bool _newTagIsPrivate;
    private int? _newTagTargetGroupId;
    private bool _isAddingTag;

    // リプライ・引用表示用の状態
    private readonly HashSet<int> _expandedReplyItemIds = [];
    private readonly Dictionary<int, IReadOnlyList<ItemDto>> _repliesByItemId = [];
    private readonly Dictionary<int, string> _replyInputs = [];
    private readonly HashSet<int> _loadingReplyItemIds = [];
    private readonly HashSet<int> _submittingReplyItemIds = [];

    private readonly HashSet<int> _expandedQuoteItemIds = [];
    private readonly Dictionary<int, IReadOnlyList<ItemDto>> _quotesByItemId = [];
    private readonly HashSet<int> _loadingQuoteItemIds = [];

    // CSV インポート用の状態
    private string _csvContentToImport = string.Empty;
    private bool _isImporting;

    // アイテム編集用の状態
    private int? _editingItemId;
    private string _editingContent = string.Empty;
    private int? _savingEditItemId;

    // タグ追加用の状態
    private readonly Dictionary<int, bool> _isAddingTagByItemId = [];

    private IReadOnlyList<ItemDto> _items = [];
    private IReadOnlyList<TagDto> _tags = [];
    private int _totalCount;

    protected override async Task OnInitializedAsync()
    {
        await LoadDataAsync();
        await LoadUserGroupsAsync();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "グループ取得失敗時も一覧表示は継続するため")]
    private async Task LoadUserGroupsAsync()
    {
        try
        {
            _userGroups = await ApiClient.GetUserGroupsAsync() ?? [];
        }
        catch
        {
            // グループ取得失敗時は空のまま続行
        }
    }

    private async Task<IEnumerable<TagSuggestion>> SearchSuggestionsAsync(string? text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        try
        {
            return await ApiClient.SearchTagsAsync(text.Trim(), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return [];
        }
    }

    private async Task OnSuggestionSelected(TagSuggestion? suggestion)
    {
        if (suggestion is null || string.IsNullOrWhiteSpace(suggestion.TagName))
        {
            return;
        }

        var alreadyExists = suggestion.TagId.HasValue
            ? _selectedFilters.Any(f => f.TagId == suggestion.TagId)
            : _selectedFilters.Any(f => f.TagName.Equals(suggestion.TagName, StringComparison.OrdinalIgnoreCase) && f.UserName == suggestion.UserName);

        if (!alreadyExists)
        {
            _selectedFilters.Add(suggestion);
            _searchText = string.Empty;
            if (_autocomplete != null)
            {
                await _autocomplete.ClearAsync();
            }
            await LoadDataAsync();
        }
    }

    private async Task OnAdornmentClick()
    {
        if (!string.IsNullOrWhiteSpace(_searchText))
        {
            var text = _searchText.Trim();
            if (!_selectedFilters.Any(f => f.TagName.Equals(text, StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(f.UserName)))
            {
                _selectedFilters.Add(new TagSuggestion(null, text, null));
                _searchText = string.Empty;
                if (_autocomplete != null)
                {
                    await _autocomplete.ClearAsync();
                }
                await LoadDataAsync();
            }
        }
    }

    private async Task RemoveFilterAsync(TagSuggestion filter)
    {
        if (_selectedFilters.Remove(filter))
        {
            await LoadDataAsync();
        }
    }

    private async Task ClearFiltersAsync()
    {
        if (_selectedFilters.Count > 0)
        {
            _selectedFilters.Clear();
            await LoadDataAsync();
        }
    }

    /// <summary>
    /// 現在の検索条件に基づいてサーバー API からアイテム・タグデータを非同期に読み込みます。
    /// </summary>
    /// <returns>非同期タスク。</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "UI 表示向けにエラーメッセージへ集約するため")]
    public async Task LoadDataAsync()
    {
        _isLoading = true;
        _errorMessage = null;
        StateHasChanged();

        try
        {
            var filterDtos = _selectedFilters
                .Select(f => new ItemListFilterDto(
                    TagId: f.TagId,
                    TagName: f.TagId.HasValue ? null : f.TagName,
                    UserName: f.UserName
                ))
                .ToList();

            var sortDtos = _sortConditions
                .Select(s => new ItemListSortDto(s.TagId, s.Ascending))
                .ToList();

            var request = new ItemListQueryRequest(
                Filters: filterDtos,
                Sorts: sortDtos,
                Skip: 0,
                Take: 50
            );

            ItemListQueryResponse response = await ApiClient.QueryItemsAsync(request);
            _items = response.Items;
            _tags = response.Tags;
            _totalCount = response.TotalCount;
        }
        catch (Exception ex)
        {
            _errorMessage = $"データの取得に失敗しました: {ex.Message}";
        }
        finally
        {
            _isLoading = false;
            StateHasChanged();
        }
    }

    private Task<IEnumerable<TagSuggestion>> SearchSortTagsAsync(string? value, CancellationToken _)
    {
        IEnumerable<TagSuggestion> source = _selectedFilters.Where(f => f.TagId.HasValue);
        if (string.IsNullOrWhiteSpace(value))
        {
            return Task.FromResult(source);
        }

        return Task.FromResult(source.Where(t => t.TagName.Contains(value, StringComparison.OrdinalIgnoreCase)));
    }

    private async Task OnSortTargetTagAdded(TagSuggestion? suggestion)
    {
        if (suggestion?.TagId is { } tagId && _sortConditions.All(c => c.TagId != tagId))
        {
            _sortConditions.Add(new WasmSortCondition(tagId, suggestion.TagName, false));
            await LoadDataAsync();
        }
    }

    private async Task ToggleSortOrderAsync(WasmSortCondition condition)
    {
        condition.Ascending = !condition.Ascending;
        await LoadDataAsync();
    }

    private async Task RemoveSortConditionAsync(WasmSortCondition condition)
    {
        if (_sortConditions.Remove(condition))
        {
            await LoadDataAsync();
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "UI 表示向けにエラーメッセージへ集約するため")]
    private async Task ExportToJsonAsync()
    {
        if (_items.Count == 0)
        {
            return;
        }

        try
        {
            var itemIds = _items.Select(i => i.Id).ToList();
            string json = await ApiClient.ExportItemsAsync(itemIds);
            await JS.InvokeVoidAsync("window.downloadFileFromText", "search_results.json", json);
        }
        catch (Exception ex)
        {
            _errorMessage = $"エクスポートに失敗しました: {ex.Message}";
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "UI 表示向けにエラーメッセージへ集約するため")]
    private async Task VoteItemAsync(int itemId, bool isUpvote)
    {
        try
        {
            var success = await ApiClient.VoteItemAsync(itemId, isUpvote);
            if (success)
            {
                await LoadDataAsync();
            }
            else
            {
                _errorMessage = "投票に失敗しました。ログイン状態を確認してください。";
            }
        }
        catch (Exception ex)
        {
            _errorMessage = $"投票に失敗しました: {ex.Message}";
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "UI 表示向けにエラーメッセージへ集約するため")]
    private async Task ReactItemAsync(int itemId, string reactionTagName)
    {
        try
        {
            var success = await ApiClient.ReactItemAsync(itemId, reactionTagName);
            if (success)
            {
                await LoadDataAsync();
            }
            else
            {
                _errorMessage = "リアクションに失敗しました。ログイン状態を確認してください。";
            }
        }
        catch (Exception ex)
        {
            _errorMessage = $"リアクションに失敗しました: {ex.Message}";
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "UI 表示向けにエラーメッセージへ集約するため")]
    private async Task AddNewItemAsync()
    {
        if (string.IsNullOrWhiteSpace(_newItemContent))
        {
            return;
        }

        if (_isAddingItem)
        {
            Snackbar?.Add(DoubleSubmissionGuard.DuplicateSubmissionMessage, Severity.Info);
            _errorMessage = DoubleSubmissionGuard.DuplicateSubmissionMessage;
            return;
        }

        _isAddingItem = true;
        _errorMessage = null;

        try
        {
            var initialTagIds = _selectedFilters
                .Where(f => f.TagId.HasValue)
                .Select(f => f.TagId!.Value)
                .ToList();

            var request = new CreateItemRequest(
                Content: _newItemContent,
                IsPrivate: _newItemIsPrivate,
                InitialTagIds: initialTagIds,
                QuotedItemId: _quotingItemId,
                TargetUserGroupId: _newItemTargetGroupId
            );
            ItemDto? created = await ApiClient.CreateItemAsync(request);
            if (created != null)
            {
                if (created.IsDuplicate)
                {
                    Snackbar?.Add(DoubleSubmissionGuard.DuplicateSubmissionMessage, Severity.Info);
                    _errorMessage = DoubleSubmissionGuard.DuplicateSubmissionMessage;
                    return;
                }

                _newItemContent = string.Empty;
                _quotingItemId = null;
                _quotingItemContent = null;
                await LoadDataAsync();
            }
            else
            {
                _errorMessage = "アイテムの作成に失敗しました。";
            }
        }
        catch (Exception ex)
        {
            _errorMessage = $"アイテムの作成に失敗しました: {ex.Message}";
        }
        finally
        {
            _isAddingItem = false;
        }
    }

    private void SetQuotingItem(ItemDto item)
    {
        _quotingItemId = item.Id;
        _quotingItemContent = item.Content;
    }

    private void ClearQuotingItem()
    {
        _quotingItemId = null;
        _quotingItemContent = null;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "UI 表示向けにエラーメッセージへ集約するため")]
    private async Task ToggleRepliesAsync(int itemId)
    {
        if (_expandedReplyItemIds.Remove(itemId))
        {
            return;
        }

        _expandedReplyItemIds.Add(itemId);
        if (!_repliesByItemId.ContainsKey(itemId))
        {
            _loadingReplyItemIds.Add(itemId);
            try
            {
                IReadOnlyList<ItemDto> replies = await ApiClient.GetRepliesAsync(itemId);
                _repliesByItemId[itemId] = replies;
            }
            catch (Exception ex)
            {
                _errorMessage = $"リプライの取得に失敗しました: {ex.Message}";
            }
            finally
            {
                _loadingReplyItemIds.Remove(itemId);
            }
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "UI 表示向けにエラーメッセージへ集約するため")]
    private async Task SubmitReplyAsync(int itemId)
    {
        if (!_replyInputs.TryGetValue(itemId, out var content) || string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        if (!_submittingReplyItemIds.Add(itemId))
        {
            Snackbar?.Add(DoubleSubmissionGuard.DuplicateSubmissionMessage, Severity.Info);
            _errorMessage = DoubleSubmissionGuard.DuplicateSubmissionMessage;
            return;
        }

        try
        {
            var request = new CreateReplyRequest(content.Trim());
            ItemDto? reply = await ApiClient.AddReplyAsync(itemId, request);
            if (reply != null)
            {
                if (reply.IsDuplicate)
                {
                    Snackbar?.Add(DoubleSubmissionGuard.DuplicateSubmissionMessage, Severity.Info);
                    _errorMessage = DoubleSubmissionGuard.DuplicateSubmissionMessage;
                    return;
                }

                _replyInputs[itemId] = string.Empty;
                IReadOnlyList<ItemDto> currentReplies = _repliesByItemId.TryGetValue(itemId, out var list) ? list : [];
                _repliesByItemId[itemId] = [.. currentReplies, reply];
                await LoadDataAsync();
            }
            else
            {
                _errorMessage = "リプライの送信に失敗しました。";
            }
        }
        catch (Exception ex)
        {
            _errorMessage = $"リプライの送信に失敗しました: {ex.Message}";
        }
        finally
        {
            _submittingReplyItemIds.Remove(itemId);
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "UI 表示向けにエラーメッセージへ集約するため")]
    private async Task ToggleQuotesAsync(int itemId)
    {
        if (_expandedQuoteItemIds.Remove(itemId))
        {
            return;
        }

        _expandedQuoteItemIds.Add(itemId);
        if (!_quotesByItemId.ContainsKey(itemId))
        {
            _loadingQuoteItemIds.Add(itemId);
            try
            {
                IReadOnlyList<ItemDto> quotes = await ApiClient.GetQuotesAsync(itemId);
                _quotesByItemId[itemId] = quotes;
            }
            catch (Exception ex)
            {
                _errorMessage = $"引用一覧の取得に失敗しました: {ex.Message}";
            }
            finally
            {
                _loadingQuoteItemIds.Remove(itemId);
            }
        }
    }

    private void StartEdit(ItemDto item)
    {
        _editingItemId = item.Id;
        _editingContent = item.Content;
    }

    private void CancelEdit()
    {
        _editingItemId = null;
        _editingContent = string.Empty;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "UI 表示向けにエラーメッセージへ集約するため")]
    private async Task SaveEditAsync(int itemId)
    {
        if (string.IsNullOrWhiteSpace(_editingContent))
        {
            return;
        }

        if (_savingEditItemId == itemId)
        {
            Snackbar?.Add(DoubleSubmissionGuard.DuplicateSubmissionMessage, Severity.Info);
            _errorMessage = DoubleSubmissionGuard.DuplicateSubmissionMessage;
            return;
        }

        _savingEditItemId = itemId;
        try
        {
            var request = new UpdateItemRequest(_editingContent.Trim());
            bool success = await ApiClient.UpdateItemAsync(itemId, request);
            if (success)
            {
                _editingItemId = null;
                _editingContent = string.Empty;
                await LoadDataAsync();
            }
            else
            {
                _errorMessage = "アイテムの更新に失敗しました。";
            }
        }
        catch (Exception ex)
        {
            _errorMessage = $"アイテムの更新に失敗しました: {ex.Message}";
        }
        finally
        {
            _savingEditItemId = null;
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "UI 表示向けにエラーメッセージへ集約するため")]
    private async Task DeleteItemAsync(int itemId)
    {
        try
        {
            bool success = await ApiClient.DeleteItemAsync(itemId);
            if (success)
            {
                await LoadDataAsync();
            }
            else
            {
                _errorMessage = "アイテムの削除に失敗しました。";
            }
        }
        catch (Exception ex)
        {
            _errorMessage = $"アイテムの削除に失敗しました: {ex.Message}";
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "UI 表示向けにエラーメッセージへ集約するため")]
    private async Task AddTagToItemAsync(int itemId, TagSuggestion? suggestion)
    {
        if (suggestion?.TagId is not { } tagId)
        {
            return;
        }

        try
        {
            var request = new AddTagRelationRequest(tagId);
            bool success = await ApiClient.AddTagToItemAsync(itemId, request);
            if (success)
            {
                _isAddingTagByItemId[itemId] = false;
                await LoadDataAsync();
            }
            else
            {
                _errorMessage = "タグの追加に失敗しました。";
            }
        }
        catch (Exception ex)
        {
            _errorMessage = $"タグの追加に失敗しました: {ex.Message}";
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "UI 表示向けにエラーメッセージへ集約するため")]
    private async Task RemoveTagFromItemAsync(int itemId, int relationId)
    {
        try
        {
            // TagRelation の Id (RelationId) をキーとして RemoveTagFromItemAsync を呼び出す
            bool success = await ApiClient.RemoveTagFromItemAsync(itemId, relationId);
            if (success)
            {
                await LoadDataAsync();
            }
            else
            {
                _errorMessage = "タグの削除に失敗しました。";
            }
        }
        catch (Exception ex)
        {
            _errorMessage = $"タグの削除に失敗しました: {ex.Message}";
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "UI 表示向けにエラーメッセージへ集約するため")]
    private async Task VoteTagAsync(int tagId, bool isUpvote)
    {
        try
        {
            bool success = await ApiClient.VoteTagAsync(tagId, isUpvote);
            if (success)
            {
                await LoadDataAsync();
            }
            else
            {
                _errorMessage = "タグへの投票に失敗しました。";
            }
        }
        catch (Exception ex)
        {
            _errorMessage = $"タグへの投票に失敗しました: {ex.Message}";
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "UI 表示向けにエラーメッセージへ集約するため")]
    private async Task AddNewTagAsync()
    {
        if (string.IsNullOrWhiteSpace(_newTagName))
        {
            return;
        }

        if (_isAddingTag)
        {
            Snackbar?.Add(DoubleSubmissionGuard.DuplicateSubmissionMessage, Severity.Info);
            _errorMessage = DoubleSubmissionGuard.DuplicateSubmissionMessage;
            return;
        }

        _isAddingTag = true;
        _errorMessage = null;

        try
        {
            var request = new CreateTagRequest(
                Name: _newTagName,
                Content: string.IsNullOrWhiteSpace(_newTagContent) ? null : _newTagContent,
                IsPrivate: _newTagIsPrivate,
                TargetUserGroupId: _newTagTargetGroupId
            );

            TagDto? created = await ApiClient.CreateTagAsync(request);
            if (created != null)
            {
                _newTagName = string.Empty;
                _newTagContent = string.Empty;
                await LoadDataAsync();
            }
            else
            {
                _errorMessage = "タグの作成に失敗しました。";
            }
        }
        catch (Exception ex)
        {
            _errorMessage = $"タグの作成に失敗しました: {ex.Message}";
        }
        finally
        {
            _isAddingTag = false;
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "UI 表示向けにエラーメッセージへ集約するため")]
    private async Task DeleteTagAsync(int tagId)
    {
        try
        {
            bool success = await ApiClient.DeleteTagAsync(tagId);
            if (success)
            {
                await LoadDataAsync();
            }
            else
            {
                _errorMessage = "タグの削除に失敗しました。";
            }
        }
        catch (Exception ex)
        {
            _errorMessage = $"タグの削除に失敗しました: {ex.Message}";
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "UI 表示向けにエラーメッセージへ集約するため")]
    private async Task ImportCsvAsync()
    {
        if (string.IsNullOrWhiteSpace(_csvContentToImport))
        {
            return;
        }

        if (_isImporting)
        {
            Snackbar?.Add(DoubleSubmissionGuard.DuplicateSubmissionMessage, Severity.Info);
            _errorMessage = DoubleSubmissionGuard.DuplicateSubmissionMessage;
            return;
        }

        _isImporting = true;
        _errorMessage = null;

        try
        {
            var request = new ImportCsvRequest(_csvContentToImport);
            ImportCsvResponse? response = await ApiClient.ImportCsvAsync(request);
            if (response != null)
            {
                _csvContentToImport = string.Empty;
                await LoadDataAsync();
            }
            else
            {
                _errorMessage = "CSVのインポートに失敗しました。ログイン状態を確認してください。";
            }
        }
        catch (Exception ex)
        {
            _errorMessage = $"CSVインポートエラー: {ex.Message}";
        }
        finally
        {
            _isImporting = false;
        }
    }
}