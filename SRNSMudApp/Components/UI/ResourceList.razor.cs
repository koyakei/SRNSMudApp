using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

using Microsoft.JSInterop;

using SRNSMudApp.Components.Item;
using SRNSMudApp.Data;
using SRNSMudApp.Services;

// 兄弟名前空間 SRNSMudApp.Components.Tag / .Item が同名型と解決されるため、
// エイリアスを名前空間の内側に置く

// IDE0010: union 型・enum の網羅的 switch に対する「Populate switch」は、
// 全ケース列挙済み・default 併記済みでも解消されない解析器の誤検知のため抑制する。
#pragma warning disable IDE0010

namespace SRNSMudApp.Components.UI;

// 兄弟名前空間 ...Tag / .Item が同名型と解決されるため、エイリアスは名前空間の内側に置く
using Tag = SRNSMudApp.Data.Tag;
using Item = SRNSMudApp.Data.Item;

/// <summary>
///     ResourceList のコードビハインド。
///     マークアップ (.razor) 側は表示のみを担い、システムタグ解決・フォーカス管理・
///     URL クエリ同期などの UI オーケストレーションはこちらに集約する。
/// </summary>
public partial class ResourceList : IAsyncDisposable
{
    [CascadingParameter] private Task<AuthenticationState>? AuthState { get; set; }

    [Parameter] public IEnumerable<Item> Items { get; set; } = [];
    [Parameter] public IEnumerable<Tag> Tags { get; set; } = [];
    [Parameter] public EventCallback OnDataChanged { get; set; }
    [Parameter] public bool EnableUrlUpdate { get; set; } = true;

    [Inject] private IHomeDataProvider HomeData { get; set; } = null!;
    [Inject] private ISystemTagEnsurer SystemTagEnsurer { get; set; } = null!;
    [Inject] private IJSRuntime JS { get; set; } = null!;
    [Inject] private NavigationManager NavigationManager { get; set; } = null!;

    private string _currentUserId = "";

    // ===== タグツリーポップオーバー用ステート（子に渡すため取得しておく） =====
    private List<Tag> _allTags = [];
    private List<TagRelationToTag> _allTagRelationsToTags = [];

    // ===== フォーカスステート =====
    private int? _focusTagId;
    private int? _focusItemId;
    private bool _hasScrolledToFocus;

    // ===== Voting用システムタグ =====
    private int? _currentUserGoodTagId;
    private int? _currentUserBadTagId;

    protected override async Task OnInitializedAsync()
    {
        // URL 形式の知識は ItemListQueryState に一元化
        var state = ItemListQueryState.ParseFromUri(new Uri(NavigationManager.Uri));
        _focusTagId = state.FocusTagId;
        _focusItemId = state.FocusItemId;

        AuthenticationState authState = await AuthState!;
        _currentUserId = authState.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        await FetchTagsAsync();
    }

    private async Task FetchTagsAsync()
    {
        (List<Tag> tags, List<TagRelationToTag> relations) = await HomeData.GetTagsAndRelationsAsync();
        _allTags = tags;
        _allTagRelationsToTags = relations;

        SystemTagIds systemTags = ResourceListViewModel.FindSystemTags(_allTags, _currentUserId);
        _currentUserGoodTagId = systemTags.GoodTagId;
        _currentUserBadTagId = systemTags.BadTagId;
    }

    private async Task NotifyChangedAsync()
    {
        await FetchTagsAsync();
        switch (OnDataChanged.HasDelegate)
        {
            case true:
                await OnDataChanged.InvokeAsync();
                break;
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        switch (!_hasScrolledToFocus && (Items.Any() || Tags.Any()))
        {
            case true:
                _hasScrolledToFocus = true;
                await ScrollToFocusTargetAsync();
                break;
        }
    }

    /// <summary>focusTag を優先してスクロールする。タグ未指定の場合は focusItem へスクロールする。</summary>
    private async Task ScrollToFocusTargetAsync()
    {
        var selector = ResourceListViewModel.GetFocusSelector(_focusTagId, _focusItemId);
        switch (selector)
        {
            case not null:
                await TryScrollAsync(selector);
                break;
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "フォーカス先へのスクロール失敗はページ表示への影響を避けるため無視する")]
    [SuppressMessage("Roslynator", "RCS1075:Avoid empty catch clause that catches System.Exception",
        Justification = "スクロール失敗時は何もしない")]
    private async Task TryScrollAsync(string selector)
    {
        try
        {
            await JS.InvokeVoidAsync("contentOverflowHelper.scrollToElement", selector);
        }
        catch (Exception)
        {
            // ignored
        }
    }

    private void SetFocusTag(int tagId)
    {
        switch (_focusTagId == tagId)
        {
            case true: return;
        }
        _focusTagId = tagId;
        _focusItemId = null;
        UpdateFocusUrl();
    }

    private void SetFocusItem(int itemId)
    {
        switch (_focusItemId == itemId)
        {
            case true: return;
        }
        _focusItemId = itemId;
        _focusTagId = null;
        UpdateFocusUrl();
    }

    private void UpdateFocusUrl()
    {
        switch (EnableUrlUpdate)
        {
            case false: return;
        }

        // 現在の URL から状態を読み取り、focus 系パラメータのみ差し替える
        ItemListQueryState updated = ItemListQueryState.ParseFromUri(new Uri(NavigationManager.Uri)) with
        {
            FocusTagId = _focusTagId,
            FocusItemId = _focusItemId
        };
        var newUri = NavigationManager.GetUriWithQueryParameters(updated.BuildParameters());
        NavigationManager.NavigateTo(newUri, replace: true);
    }

    public async Task EnsureSystemTagsExistAsync()
    {
        (SystemTagIds ids, var refetch) = await SystemTagEnsurer.EnsureAsync(
            _currentUserId, new SystemTagIds(_currentUserGoodTagId, _currentUserBadTagId));
        _currentUserGoodTagId = ids.GoodTagId;
        _currentUserBadTagId = ids.BadTagId;

        if (refetch)
        {
            await FetchTagsAsync();
        }
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}