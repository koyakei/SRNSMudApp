using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

using MudBlazor;

using SRNSMudApp.Components.UI;
using SRNSMudApp.Data;
using SRNSMudApp.Services;
using SRNSMudApp.Services.Dialogs;

// 兄弟名前空間 SRNSMudApp.Components.Tag / 自名前空間 .Item が同名型と解決されるため、
// エイリアスを名前空間の内側に置く

// IDE0010: union 型・enum の網羅的 switch に対する「Populate switch」は、
// 全ケース列挙済み・default 併記済みでも解消されない解析器の誤検知のため抑制する。
#pragma warning disable IDE0010

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
    public record ItemDetailData(Data.Item Item, List<TaggingRequestEntity> Requests, IReadOnlyList<TagWeightLedger> Ledgers);

    [CascadingParameter] private Task<AuthenticationState>? AuthState { get; set; }

    [Parameter] public int ItemId { get; set; }

    [Inject] private IItemDetailDataProvider DetailData { get; set; } = null!;
    [Inject] private NavigationManager NavigationManager { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private ITaggingContractService TaggingContractService { get; set; } = null!;
    [Inject] private ITaggingService TaggingService { get; set; } = null!;
    [Inject] private IDialogLauncher DialogLauncher { get; set; } = null!;

    private AsyncPageState<ItemDetailData> _pageState = new Loading();

    private string _currentUserId = "";
    private IReadOnlyList<Data.Tag> _allTags = [];
    private IReadOnlyList<TagRelationToTag> _allTagRelationsToTags = [];

    [SupplyParameterFromQuery(Name = "tab")]
    public string? ActiveTabQuery { get; set; }

    [SupplyParameterFromQuery(Name = "requestId")]
    public int? SelectedRequestIdQuery { get; set; }

    private int _activeTabIndex;
    private TaggingRequestEntity? _selectedRequest;
    private string? _searchQuery;

    protected override async Task OnInitializedAsync()
    {
        _activeTabIndex = ItemDetailQueryState.ToTabIndex(ActiveTabQuery);
        InitializeSearchQueryFromUri();

        await LoadDataAsync();
    }

    protected override async Task OnParametersSetAsync()
    {
        switch (_pageState)
        {
            case Loaded<ItemDetailData> loaded when loaded.Data.Item.Id != ItemId:
                await LoadDataAsync();
                break;
        }

        var state = ItemDetailQueryState.ParseFromUri(new Uri(NavigationManager.Uri));
        FilterEntry? filter = state.Filters.Count > 0 ? state.Filters[0] : null;
        var currentSearch = filter != null ? TagFilterQueryCodec.ToSearchString(filter, _allTags) : null;
        if (currentSearch != _searchQuery)
        {
            _searchQuery = currentSearch;
        }
    }

    private void InitializeSearchQueryFromUri()
    {
        var state = ItemDetailQueryState.ParseFromUri(new Uri(NavigationManager.Uri));
        FilterEntry? filter = state.Filters.Count > 0 ? state.Filters[0] : null;
        _searchQuery = filter != null ? TagFilterQueryCodec.ToSearchString(filter, _allTags) : null;
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "データ取得時に発生した例外をエラー状態として画面表示するために広く捕捉する")]
    private async Task LoadDataAsync()
    {
        try
        {
            _pageState = new Loading();
#pragma warning disable BL0012
            // ローディングスピナーを即座に描画させるため意図的に呼び出す
            StateHasChanged();
#pragma warning restore BL0012

            ItemDetailPageData? data = await DetailData.GetItemDetailAsync(ItemId);

            switch (data)
            {
                case null:
                    _pageState = new Empty("アイテムが見つかりません。");
                    return;
            }

            List<TaggingRequestEntity>? requests = (await TaggingContractService.GetRequestsByItemIdAsync(ItemId))?.ToList();

            switch (SelectedRequestIdQuery.HasValue && requests != null)
            {
                case true:
                    _selectedRequest = requests?.FirstOrDefault(r => r.Id == SelectedRequestIdQuery.Value);
                    break;
            }

            switch (AuthState)
            {
                case not null:
                    AuthenticationState authState = await AuthState;
                    _currentUserId = authState.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
                    break;
            }

            _allTags = data.AllTags;
            _allTagRelationsToTags = data.AllTagRelationsToTags;

            // タグ一覧取得後に TagId ベースのフィルタ文字列を解決する
            var state = ItemDetailQueryState.ParseFromUri(new Uri(NavigationManager.Uri));
            FilterEntry? filter = state.Filters.Count > 0 ? state.Filters[0] : null;
            if (filter != null)
            {
                _searchQuery = TagFilterQueryCodec.ToSearchString(filter, _allTags);
            }

            _pageState = new Loaded<ItemDetailData>(new ItemDetailData(data.Item, requests ?? [], data.Ledgers));
        }
        catch (Exception ex)
        {
            _pageState = new Failed(ex);
        }
    }

    private void OnSelectedRequestChanged(TaggingRequestEntity? request)
    {
        _selectedRequest = request;
        SelectedRequestIdQuery = request?.Id;
        UpdateUrlQuery();
    }

    // Removed RemoveItemTagAsync as it's now handled inside ItemTagChip

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "UI 層で発生した例外の内容をユーザーへ通知するために広く捕捉する")]
    private async Task OpenRejectDialogAsync(TaggingRequestEntity request)
    {
        AuthenticationState authState = await AuthState;
        var currentUserId = authState.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        switch (currentUserId)
        {
            case null: return;
        }

        var options = new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Small, FullWidth = true };
        IDialogReference dialog = await DialogLauncher.ShowAsync<RejectRequestDialog>("リクエストを却下", options);
        DialogResult? result = await dialog.Result;

        switch (result)
        {
            case { Canceled: false }:
                try
                {
                    var comment = result.Data as string;
                    await TaggingService.RejectRequestAsync(request.Id, currentUserId, comment);
                    _ = Snackbar.Add("リクエストを却下しました。", Severity.Success);

                    switch (_pageState)
                    {
                        case Loaded<ItemDetailData> loaded:
                            _ = loaded.Data.Requests.Remove(request);
                            StateHasChanged();
                            break;
                    }
                }
                catch (Exception ex)
                {
                    _ = Snackbar.Add($"却下に失敗しました: {ex.Message}", Severity.Error);
                }
                break;
        }
    }

    private void OnTabChanged(int index)
    {
        _activeTabIndex = index;
        ActiveTabQuery = ItemDetailQueryState.FromTabIndex(index);
        UpdateUrlQuery();
    }

    private void OnSearchStringChanged(string? search)
    {
        _searchQuery = string.IsNullOrWhiteSpace(search) ? null : search;
        UpdateUrlQuery();
    }

    private void UpdateUrlQuery()
    {
        FilterEntry? filter = TagFilterQueryCodec.FromSearchString(_searchQuery);
        IReadOnlyList<FilterEntry> filters = filter != null ? [filter] : [];

        // URL 形式の知識は ItemDetailQueryState に一元化
        Dictionary<string, object?> parameters =
            new ItemDetailQueryState
            {
                ActiveTab = ActiveTabQuery,
                SelectedRequestId = SelectedRequestIdQuery,
                Filters = filters
            }
                .BuildParameters();
        var uri = NavigationManager.GetUriWithQueryParameters(parameters);
        NavigationManager.NavigateTo(uri, replace: false);
    }
}