using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

using MudBlazor;

using SRNSMudApp.Client.Services;
using SRNSMudApp.Components.UI;
using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Resources;
using SRNSMudApp.Services;
using SRNSMudApp.Services.Dialogs;

// 兄弟名前空間 SRNSMudApp.Components.Tag / 自名前空間 .Item が同名型と解決されるため、
// エイリアスを名前空間の内側に置く

// IDE0010: union 型・enum の網羅的 switch に対する「Populate switch」は、
// 全ケース列挙済み・default 併記済みでも解消されない解析器の誤検知のため抑制する。
#pragma warning disable IDE0010
// CA1508: union 型 (AsyncPageState) のパターンマッチにおける解析器の誤検知のため抑制する。
#pragma warning disable CA1508

namespace SRNSMudApp.Components.Item;

/// <summary>
///     ItemDetail ページのコードビハインド。
///     マークアップ (.razor) 側は表示のみを担い、データ取得・URL クエリ同期・ダイアログ起動などの
///     UI オーケストレーションはこちらに集約する。
/// </summary>
public partial class ItemDetail
{
    // CA1034: マークアップ (.razor) 側から参照されるため public 入れ子 record のままとする。
    // CA1002: Requests はリクエスト却下時に要素削除するため List のままとする。
    [SuppressMessage("Design", "CA1034:Do not nest type. Alternatively, change its accessibility so that it is not externally visible.")]
    [SuppressMessage("Design", "CA1002:Do not expose generic lists")]
    public record ItemDetailData(
        Data.Item Item,
        List<TaggingRequestEntity> Requests,
        IReadOnlyList<TagWeightLedger> Ledgers,
        IReadOnlyList<Data.Item> Ancestors,
        IReadOnlyList<Data.Item> Replies,
        IReadOnlyList<Data.Item> Siblings,
        IReadOnlyList<Data.Item>? Quotes = null)
    {
        public IReadOnlyList<Data.Item> Quotes { get; init; } = Quotes ?? [];
    }

    [CascadingParameter] private Task<AuthenticationState> AuthState { get; set; } = default!;

    [Parameter] public int ItemId { get; set; }

    [Inject] private NavigationManager NavigationManager { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private IDialogLauncher DialogLauncher { get; set; } = null!;
    [Inject] private IJSRuntime JS { get; set; } = null!;
    [Inject] private ItemDetailViewModel ViewModel { get; set; } = null!;

    private readonly ItemDetailThreadViewModel _threadViewModel = new();

    private bool _hasScrolledToFocus;

    private AsyncPageState<ItemDetailData> _pageState = new Loading();

    private string _currentUserId = "";
    private IReadOnlyList<Data.Tag> _allTags = [];
    private IReadOnlyList<TagRelationToTag> _allTagRelationsToTags = [];

    private int? _currentUserGoodTagId;
    private int? _currentUserBadTagId;
    private int? _currentUserShinjiTagId;
    private int? _currentUserZenTagId;
    private int? _currentUserBiTagId;

    private string _newReplyText = "";
    private bool _isSubmittingReply;
    private bool _isReplyPrivate;
    private int _lastReplyItemId;

    [SupplyParameterFromQuery(Name = "tab")]
    public string? ActiveTabQuery { get; set; }

    [SupplyParameterFromQuery(Name = "requestId")]
    public int? SelectedRequestIdQuery { get; set; }

    private int _activeTabIndex;
    private TaggingRequestEntity? _selectedRequest;
    private string? _searchQuery;
    private bool _onlyMyRequests = true;

    protected override async Task OnInitializedAsync()
    {
        _activeTabIndex = ItemDetailQueryStateFactory.ToTabIndex(ActiveTabQuery);
        InitializeSearchQueryFromUri();

        await LoadDataAsync();
    }

    protected override async Task OnParametersSetAsync()
    {
        if (_pageState is Loaded<ItemDetailData> loaded && loaded.Data.Item.Id != ItemId)
        {
            await LoadDataAsync();
        }

        // URL クエリの tab 変更（同一アイテム内での遷移含む）に合わせてアクティブタブを同期する
        var tabIndex = ItemDetailQueryStateFactory.ToTabIndex(ActiveTabQuery);
        if (_activeTabIndex != tabIndex)
        {
            _activeTabIndex = tabIndex;
        }

        var state = ItemDetailQueryStateFactory.ParseFromUri(new Uri(NavigationManager.Uri));
        var currentSearch = ItemDetailQueryStateFactory.ToSearchQuery(state, _allTags);
        if (currentSearch != _searchQuery)
        {
            _searchQuery = currentSearch;
        }
    }

    private void InitializeSearchQueryFromUri()
    {
        var state = ItemDetailQueryStateFactory.ParseFromUri(new Uri(NavigationManager.Uri));
        _searchQuery = ItemDetailQueryStateFactory.ToSearchQuery(state, _allTags);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await base.OnAfterRenderAsync(firstRender);

        if (_pageState is Loaded<ItemDetailData> && !_hasScrolledToFocus)
        {
            _hasScrolledToFocus = true;
            try
            {
                await JS.InvokeVoidAsync("contentOverflowHelper.scrollToElement", $"#item-card-{ItemId}, #current-focused-item-{ItemId}");
            }
            catch (Exception ex) when (ex is JSException or JSDisconnectedException or TaskCanceledException)
            {
                // 静的プリレンダリング時や切断時の例外は無視する
            }
        }
    }

    private async Task LoadDataAsync()
    {
        _hasScrolledToFocus = false;
        _threadViewModel.ResetExpansion();

        _pageState = new Loading();
#pragma warning disable BL0012
        StateHasChanged();
#pragma warning restore BL0012

        if (AuthState is not null)
        {
            AuthenticationState authState = await AuthState;
            ViewModel.SetUserContext(authState.User);
        }

        await ViewModel.LoadDataAsync(ItemId, SelectedRequestIdQuery);
        SyncStateFromViewModel();

        if (_pageState is Loaded<ItemDetailData> loaded)
        {
            if (_lastReplyItemId != loaded.Data.Item.Id)
            {
                _lastReplyItemId = loaded.Data.Item.Id;
                _isReplyPrivate = loaded.Data.Item.IsPrivate;
            }
        }

        var state = ItemDetailQueryStateFactory.ParseFromUri(new Uri(NavigationManager.Uri));
        _searchQuery = ItemDetailQueryStateFactory.ToSearchQuery(state, _allTags);
    }

    private void SyncStateFromViewModel()
    {
        _pageState = ViewModel.PageState;
        _currentUserId = ViewModel.CurrentUserId;
        _allTags = ViewModel.AllTags;
        _allTagRelationsToTags = ViewModel.AllTagRelationsToTags;
        _selectedRequest = ViewModel.SelectedRequest;
        SyncSystemTagsFromViewModel();
    }

    private void SyncSystemTagsFromViewModel()
    {
        _currentUserGoodTagId = ViewModel.CurrentUserGoodTagId;
        _currentUserBadTagId = ViewModel.CurrentUserBadTagId;
        _currentUserShinjiTagId = ViewModel.CurrentUserShinjiTagId;
        _currentUserZenTagId = ViewModel.CurrentUserZenTagId;
        _currentUserBiTagId = ViewModel.CurrentUserBiTagId;
    }

    public async Task EnsureSystemTagsExistAsync()
    {
        bool refetch = await ViewModel.EnsureSystemTagsExistAsync();
        SyncSystemTagsFromViewModel();

        if (refetch)
        {
            await LoadDataAsync();
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "UI 層で発生した例外の内容をユーザーへ通知するために広く捕捉する")]
    private async Task SubmitReplyAsync()
    {
        if (string.IsNullOrWhiteSpace(_newReplyText) || string.IsNullOrEmpty(_currentUserId))
        {
            return;
        }

        if (_isSubmittingReply)
        {
            Snackbar.Add(DoubleSubmissionGuard.DuplicateSubmissionMessage, Severity.Info);
            return;
        }

        _isSubmittingReply = true;
        try
        {
            Data.Item? addedReply = await ViewModel.SubmitReplyAsync(ItemId, _newReplyText, _isReplyPrivate);
            if (addedReply is not null)
            {
                if (addedReply.IsDuplicate)
                {
                    Snackbar.Add(DoubleSubmissionGuard.DuplicateSubmissionMessage, Severity.Info);
                    return;
                }

                _newReplyText = "";
                var currentItem = _pageState is Loaded<ItemDetailData> loaded ? loaded.Data.Item : null;
                _isReplyPrivate = currentItem?.IsPrivate ?? false;
                _ = Snackbar.Add("リプライを送信しました。", Severity.Success);
                await LoadDataAsync();
            }
        }
        catch (Exception ex)
        {
            _ = Snackbar.Add($"リプライの送信に失敗しました: {ex.Message}", Severity.Error);
        }
        finally
        {
            _isSubmittingReply = false;
        }
    }

    private void OnSelectedRequestChanged(TaggingRequestEntity? request)
    {
        _selectedRequest = request;
        SelectedRequestIdQuery = request?.Id;
        UpdateUrlQuery();
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "UI 層で発生した例外の内容をユーザーへ通知するために広く捕捉する")]
    private async Task OpenRejectDialogAsync(TaggingRequestEntity request)
    {
        var options = new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Small, FullWidth = true };
        IDialogReference dialog = await DialogLauncher.ShowAsync<RejectRequestDialog>("リクエストを却下", options);
        DialogResult? result = await dialog.Result;

        if (result is { Canceled: false })
        {
            try
            {
                var comment = result.Data as string;
                bool success = await ViewModel.RejectRequestAsync(request, comment);
                if (success)
                {
                    _ = Snackbar.Add("リクエストを却下しました。", Severity.Success);
                    _pageState = ViewModel.PageState;
                    StateHasChanged();
                }
            }
            catch (Exception ex)
            {
                _ = Snackbar.Add($"却下に失敗しました: {ex.Message}", Severity.Error);
            }
        }
    }

    private void OnTabChanged(int index)
    {
        _activeTabIndex = index;
        ActiveTabQuery = ItemDetailQueryStateFactory.FromTabIndex(index);
        UpdateUrlQuery();

        if (index == 0)
        {
            _hasScrolledToFocus = false;
        }
    }

    private void OnSearchStringChanged(string? search)
    {
        _searchQuery = string.IsNullOrWhiteSpace(search) ? null : search;
        UpdateUrlQuery();
    }

    private void UpdateUrlQuery()
    {
        // URL 形式の知識は ItemDetailQueryState に一元化
        Dictionary<string, object?> parameters = ItemDetailQueryStateFactory.BuildParameters(
            ItemDetailQueryStateFactory.FromSearchQuery(ActiveTabQuery, SelectedRequestIdQuery, _searchQuery));
        var uri = NavigationManager.GetUriWithQueryParameters(parameters);
        NavigationManager.NavigateTo(uri, replace: false);
    }

    /// <summary>
    ///     タグ付与依頼一覧を「自分のリクエストのみ」および「付けられたタグの検索条件」で絞り込む。
    /// </summary>
    private IEnumerable<TaggingRequestEntity> GetFilteredRequests(IEnumerable<TaggingRequestEntity>? requests) =>
        ItemDetailRequestFilter.FilterRequests(requests, _currentUserId, _onlyMyRequests, _searchQuery, _allTags);

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "UI 層で発生した例外の内容をユーザーへ通知するために広く捕捉する")]
    private async Task OpenAddCommentDialogAsync(TagWeightLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        var options = new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Small, FullWidth = true };
        IDialogReference dialog = await DialogLauncher.ShowAsync<TagLedgerCommentDialog>("タグ履歴にコメントを追加", options);
        DialogResult? result = await dialog.Result;
        if (result is { Canceled: false, Data: string comment } && !string.IsNullOrWhiteSpace(comment))
        {
            try
            {
                await ViewModel.AddLedgerCommentAsync(ledger, comment);
                _ = Snackbar.Add("コメントを投稿しました", Severity.Success);
                await LoadDataAsync();
            }
            catch (Exception ex)
            {
                _ = Snackbar.Add($"コメントの投稿に失敗しました: {ex.Message}", Severity.Error);
            }
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "UI 層で発生した例外の内容をユーザーへ通知するために広く捕捉する")]
    private async Task OpenAddTagDialogAsync(TagWeightLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        var options = new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Large, FullWidth = true };
        IDialogReference dialog = await DialogLauncher.ShowAsync<Components.Tag.TagAddDialog>("タグの追加", options);
        DialogResult? result = await dialog.Result;
        if (result is { Canceled: false, Data: Data.Tag selectedTag })
        {
            try
            {
                await ViewModel.AddLedgerTagAsync(ledger, selectedTag.Id);
                _ = Snackbar.Add("タグを追加しました", Severity.Success);
                await LoadDataAsync();
            }
            catch (Exception ex)
            {
                _ = Snackbar.Add($"タグの追加に失敗しました: {ex.Message}", Severity.Error);
            }
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "UI 層で発生した例外の内容をユーザーへ通知するために広く捕捉する")]
    private async Task VoteLedgerReactionAsync(TagWeightLedger ledger, string reactionTagName, int targetWeight)
    {
        ArgumentNullException.ThrowIfNull(ledger);

        if (string.IsNullOrEmpty(_currentUserId))
        {
            _ = Snackbar.Add(ErrorMessages.LoginRequired, Severity.Warning);
            return;
        }

        try
        {
            ItemVoteResult? voteResult = await ViewModel.VoteLedgerReactionAsync(ledger, reactionTagName, targetWeight);
            if (voteResult is null)
            {
                return;
            }

            if (voteResult.Action == ItemVoteAction.Added)
            {
                var parameters = new DialogParameters
                {
                    [nameof(ReactionCommentDialog.ReactionTagName)] = reactionTagName
                };
                var options = new DialogOptions
                {
                    CloseOnEscapeKey = true,
                    MaxWidth = MaxWidth.Small,
                    FullWidth = true,
                    Position = DialogPosition.BottomRight,
                    CloseButton = true,
                    BackgroundClass = "reaction-toast-dialog-overlay"
                };
                IDialogReference dialog = await DialogLauncher.ShowAsync<ReactionCommentDialog>("リアクションを追加", parameters, options);
                DialogResult? result = await dialog.Result;
                if (result is not null && !result.Canceled && result.Data is ReactionCommentDialogResult { Saved: true } dialogResult)
                {
                    if (!string.IsNullOrWhiteSpace(dialogResult.Comment))
                    {
                        _ = await ViewModel.UpdateLedgerReactionCommentAsync(voteResult.RelationId, dialogResult.Comment);
                    }
                }
            }

            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            _ = Snackbar.Add($"リアクションの送信に失敗しました: {ex.Message}", Severity.Error);
        }
    }
}