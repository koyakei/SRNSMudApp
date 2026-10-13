// CA1508: union 型 (ItemListFilter) の網羅的パターンマッチにおける解析器の誤検知のため抑制する。
#pragma warning disable CA1508
// IDE1006: マークアップ側で参照されるプライベートプロパティ名のアンダースコアプレフィックスを維持するため抑制する。
#pragma warning disable IDE1006

namespace SRNSMudApp.Components.Item;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

using SRNSMudApp.Data;

/// <summary>
///     ItemList ページのコードビハインド。
///     マークアップ (.razor) 側は表示のみを担い、ビジネスロジックは <see cref="ItemListViewModel"/> に委譲する。
/// </summary>
public sealed partial class ItemList : IDisposable
{
    [CascadingParameter] private Task<AuthenticationState> AuthState { get; set; } = default!;

    [Inject] private ItemListViewModel ViewModel { get; set; } = null!;
    [Inject] private IJSRuntime JS { get; set; } = null!;
    [Inject] private NavigationManager NavigationManager { get; set; } = null!;

    private TagSearchViewModel _tagSearchViewModel => ViewModel.TagSearch;
    private IReadOnlyList<Item> _items => ViewModel.Items;
    private IReadOnlyList<Data.Tag> _foundTags => ViewModel.FoundTags;
    private IReadOnlyList<SortCondition> _sortConditions => ViewModel.SortConditions;

    protected override async Task OnInitializedAsync()
    {
        ViewModel.StateChanged += OnStateChanged;
        ViewModel.QueryStateChanged += UpdateUrlQuery;

        string? currentUserId = null;
        var isAdmin = false;
        if (AuthState is not null)
        {
            var auth = await AuthState;
            currentUserId = auth.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            isAdmin = auth.User.IsInRole("Admin");
        }

        ViewModel.SetUser(currentUserId, isAdmin);
        await ViewModel.InitializeAsync(new Uri(NavigationManager.Uri));
    }

    private void OnStateChanged() => StateHasChanged();

    private void UpdateUrlQuery()
    {
        ItemListQueryState updated = ViewModel.BuildQueryState(new Uri(NavigationManager.Uri));
        var newUri = NavigationManager.GetUriWithQueryParameters(updated.BuildParameters());
        NavigationManager.NavigateTo(newUri, replace: true);
    }

    private async Task LoadDataAsync()
    {
        await ViewModel.LoadDataAsync();
    }

    private async Task OnSortTargetTagAdded(Data.Tag? tag)
    {
        if (await ViewModel.AddSortConditionAsync(tag))
        {
            UpdateUrlQuery();
        }
    }

    private async Task ToggleSortOrder(SortCondition condition)
    {
        await ViewModel.ToggleSortOrderAsync(condition);
        UpdateUrlQuery();
    }

    private async Task RemoveSortCondition(SortCondition condition)
    {
        await ViewModel.RemoveSortConditionAsync(condition);
        UpdateUrlQuery();
    }

    private Task<IEnumerable<Data.Tag>> SearchSortTagsAsync(string? value, CancellationToken _)
    {
        return Task.FromResult(ViewModel.SearchSortTags(value));
    }

    private async Task ExportToJsonAsync()
    {
        var json = await ViewModel.ExportToJsonAsync();

        try
        {
            await JS.InvokeVoidAsync("window.downloadFileFromText", "search_results.json", json);
        }
        catch (JSDisconnectedException)
        {
            // 回線切断後のダウンロード要求は無視する
        }
        catch (JSException)
        {
            // JS interop によるダウンロード失敗は無視する（ページ表示への影響を避ける）
        }
    }

    private void NavigateToTagDiagram()
    {
        if (_items.Count == 0)
        {
            return;
        }

        NavigationManager.NavigateTo(ViewModel.GetTagDiagramUrl());
    }

    public void Dispose()
    {
        ViewModel.StateChanged -= OnStateChanged;
        ViewModel.QueryStateChanged -= UpdateUrlQuery;
        ViewModel.Dispose();
        GC.SuppressFinalize(this);
    }
}