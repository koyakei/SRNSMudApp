// IDE0010: union 型・enum の網羅的 switch に対する「Populate switch」は、
// 全ケース列挙済み・default 併記済みでも解消されない解析器の誤検知のため抑制する。
#pragma warning disable IDE0010

using Microsoft.AspNetCore.Components;

using MudBlazor;

using SRNSMudApp.Data;
using SRNSMudApp.Models;

namespace SRNSMudApp.Components.Tag;

using Item = SRNSMudApp.Data.Item;
using Tag = SRNSMudApp.Data.Tag;

/// <summary>
///     ItemTagTable のコードビハインド。
///     マークアップ (.razor) 側は表示のみを担い、サジェストとテーブルフィルタのイベント処理はこちらに集約する。
/// </summary>
public partial class ItemTagTable
{
    [Parameter] public IEnumerable<TagRelation> TagRelations { get; set; } = [];
    [Parameter] public Item Item { get; set; } = null!;
    [Parameter] public string CurrentUserId { get; set; } = "";
    [Parameter] public IReadOnlyList<Tag> AllTags { get; set; } = [];
    [Parameter] public IReadOnlyList<TagRelationToTag> AllTagRelationsToTags { get; set; } = [];
    [Parameter] public EventCallback OnDataChanged { get; set; }

    private MudAutocomplete<string>? _autocomplete;
    private string _searchString = "";

    private Task HandleValueChangedAsync(string? value)
    {
        _searchString = value ?? "";

        switch (TagSearchQuery.Parse(_searchString))
        {
            case IncompleteSearch:
                _ = ReopenMenuAfterDelayAsync();
                break;
            default:
                break;
        }

        return Task.CompletedTask;
    }

    private async Task ReopenMenuAfterDelayAsync()
    {
        await Task.Delay(100);
        await InvokeAsync(async () =>
        {
            switch (_autocomplete)
            {
                case not null:
                    await _autocomplete.FocusAsync();
                    await _autocomplete.ToggleMenuAsync();
                    StateHasChanged();
                    break;
                default:
                    break;
            }
        });
    }

    private Task<IEnumerable<string>> SearchSuggestionsAsync(string? value, CancellationToken token)
    {
        return Task.FromResult<IEnumerable<string>>(
            ItemTagTableViewModel.GetSearchSuggestions(TagRelations, value));
    }

    private bool FilterFunc(TagRelation relation) =>
        ItemTagTableViewModel.FilterFunc(relation, _searchString);
}