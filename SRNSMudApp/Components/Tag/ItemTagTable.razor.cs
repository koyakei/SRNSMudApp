// IDE0010: union 型・enum の網羅的 switch に対する「Populate switch」は、
// 全ケース列挙済み・default 併記済みでも解消されない解析器の誤検知のため抑制する。
#pragma warning disable IDE0010, CA1508

using Microsoft.AspNetCore.Components;

using MudBlazor;

using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;

namespace SRNSMudApp.Components.Tag;

/// <summary>
///     ItemTagTable のコードビハインド。
///     マークアップ (.razor) 側は表示のみを担い、サジェストとテーブルフィルタのイベント処理はこちらに集約する。
/// </summary>
public partial class ItemTagTable
{
    [Parameter] public IEnumerable<TagRelation> TagRelations { get; set; } = [];
    [Parameter] public Data.Item Item { get; set; } = null!;
    [Parameter] public string CurrentUserId { get; set; } = "";
    [Parameter] public IReadOnlyList<Data.Tag> AllTags { get; set; } = [];
    [Parameter] public IReadOnlyList<TagRelationToTag> AllTagRelationsToTags { get; set; } = [];
    [Parameter] public EventCallback OnDataChanged { get; set; }
    [Parameter] public string? SearchString { get; set; }
    [Parameter] public EventCallback<string?> SearchStringChanged { get; set; }

    [Inject] private IUserDataProvider UserDataProvider { get; set; } = null!;
    [Inject] private TaggingContractService TaggingContractService { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private ApplicationUser? _selectedTargetUser;

    private MudAutocomplete<string>? _autocomplete;
    private string _searchString = "";

    protected override void OnParametersSet()
    {
        if (SearchString != null && SearchString != _searchString)
        {
            _searchString = SearchString;
        }
        else if (SearchString == null && !string.IsNullOrEmpty(_searchString) && SearchStringChanged.HasDelegate)
        {
            _searchString = "";
        }
    }

    private async Task HandleValueChangedAsync(string? value)
    {
        _searchString = value ?? "";
        await SearchStringChanged.InvokeAsync(string.IsNullOrWhiteSpace(_searchString) ? null : _searchString);

        switch (TagSearchQuery.Parse(_searchString))
        {
            case IncompleteSearch:
                _ = ReopenMenuAfterDelayAsync();
                break;
            default:
                break;
        }
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

    private Task<IEnumerable<string>> SearchSuggestionsAsync(string? value, CancellationToken _)
    {
        return Task.FromResult<IEnumerable<string>>(
            ItemTagTableViewModel.GetSearchSuggestions(TagRelations, value));
    }

    private bool FilterFunc(TagRelation relation) =>
        ItemTagTableViewModel.FilterFunc(relation, _searchString);

    private async Task<IEnumerable<ApplicationUser>> SearchUsersAsync(string? value, CancellationToken token)
    {
        return await UserDataProvider.SearchUsersByNormalizedNameAsync(value, token);
    }

    private async Task RequestSelectedTagAddAsync()
    {
        if (_selectedTargetUser == null)
        {
            Snackbar.Add("付与依頼先のユーザーを選択してください。", Severity.Warning);
            return;
        }

        if (string.IsNullOrEmpty(CurrentUserId))
        {
            Snackbar.Add("ログインしていません。", Severity.Error);
            return;
        }

        if (_selectedTargetUser.Id == CurrentUserId)
        {
            Snackbar.Add("自分自身には依頼できません。", Severity.Warning);
            return;
        }

        string? targetTagName = TagSearchQuery.Parse(_searchString) switch
        {
            TagNameSearch s => s.TagName,
            TagWithUserSearch s => s.TagName,
            IncompleteSearch s => s.TagName,
            _ => null
        };

        if (string.IsNullOrWhiteSpace(targetTagName))
        {
            Snackbar.Add("上の検索ボックスで付与を依頼するタグを選択・入力してください。", Severity.Warning);
            return;
        }

        Data.Tag? targetUserTag = AllTags.FirstOrDefault(t =>
            t.OwnerId == _selectedTargetUser.Id &&
            string.Equals(t.Name, targetTagName, StringComparison.OrdinalIgnoreCase));

        if (targetUserTag == null)
        {
            Snackbar.Add($"選択されたユーザー ({_selectedTargetUser.UserName}) はタグ「{targetTagName}」を発行していません。", Severity.Warning);
            return;
        }

        var result = await TaggingContractService.ProposeGratisContractAsync(
            requesterUserId: CurrentUserId,
            tagOwnerUserId: _selectedTargetUser.Id,
            targetItemId: Item.Id,
            requestedTagId: targetUserTag.Id,
            requestType: TaggingRequestType.Add);

        switch (result)
        {
            case Success<TaggingRequestEntity>:
                Snackbar.Add($"{_selectedTargetUser.UserName} さんにタグ「{targetTagName}」の付与リクエストを送信しました。", Severity.Success);
                await OnDataChanged.InvokeAsync();
                break;
            case Failure f:
                Snackbar.Add($"付与リクエストの送信に失敗しました: {f.ErrorMessage}", Severity.Error);
                break;
        }
    }
}