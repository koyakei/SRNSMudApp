// CA1508: union 型 (TagSearchQuery) の網羅的パターンマッチにおける解析器の誤検知のため抑制する。
#pragma warning disable CA1508

namespace SRNSMudApp.Components.Tag;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using MudBlazor;

using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;

/// <summary>
///     タグ付与リクエスト実行結果の表現。
/// </summary>
public sealed record RequestTagAddResult(bool Success, string Message, Severity Severity, bool ShouldNotifyChanged);

/// <summary>
///     ItemTagTable コンポーネントの状態管理、タグサジェスト、フィルタ、およびタグ付与リクエストロジックを担う ViewModel。
/// </summary>
public class ItemTagTableViewModel
{
    private const int MaxSuggestionCount = 10;

    private readonly IUserDataProvider _userDataProvider;
    private readonly ITaggingContractService _taggingContractService;

    public ItemTagTableViewModel(
        IUserDataProvider userDataProvider,
        ITaggingContractService taggingContractService)
    {
        _userDataProvider = userDataProvider;
        _taggingContractService = taggingContractService;
    }

    /// <summary>
    ///     正規化名に基づいてユーザーをインクリメンタル検索する。
    /// </summary>
    public async Task<IEnumerable<ApplicationUser>> SearchUsersAsync(string? value, CancellationToken token = default)
    {
        return await _userDataProvider.SearchUsersByNormalizedNameAsync(value, token);
    }

    /// <summary>
    ///     選択された対象ユーザーに対してタグ付与リクエストを検証・送信する。
    /// </summary>
    public async Task<RequestTagAddResult> RequestSelectedTagAddAsync(
        ApplicationUser? selectedTargetUser,
        string? currentUserId,
        string searchString,
        IReadOnlyList<Data.Tag> allTags,
        int itemId)
    {
        if (selectedTargetUser == null)
        {
            return new RequestTagAddResult(false, "付与依頼先のユーザーを選択してください。", Severity.Warning, false);
        }

        if (string.IsNullOrEmpty(currentUserId))
        {
            return new RequestTagAddResult(false, "ログインしていません。", Severity.Error, false);
        }

        if (selectedTargetUser.Id == currentUserId)
        {
            return new RequestTagAddResult(false, "自分自身には依頼できません。", Severity.Warning, false);
        }

        string? targetTagName = TagSearchQuery.Parse(searchString) switch
        {
            TagNameSearch s => s.TagName,
            TagWithUserSearch s => s.TagName,
            IncompleteSearch s => s.TagName,
            _ => null
        };

        if (string.IsNullOrWhiteSpace(targetTagName))
        {
            return new RequestTagAddResult(false, "上の検索ボックスで付与を依頼するタグを選択・入力してください。", Severity.Warning, false);
        }

        var targetUserTag = allTags.FirstOrDefault(t =>
            t.OwnerId == selectedTargetUser.Id &&
            string.Equals(t.Name, targetTagName, StringComparison.OrdinalIgnoreCase));

        if (targetUserTag == null)
        {
            return new RequestTagAddResult(false, $"選択されたユーザー ({selectedTargetUser.UserName}) はタグ「{targetTagName}」を発行していません。", Severity.Warning, false);
        }

        var result = await _taggingContractService.ProposeGratisContractAsync(
            requesterUserId: currentUserId,
            tagOwnerUserId: selectedTargetUser.Id,
            targetItemId: itemId,
            requestedTagId: targetUserTag.Id,
            requestType: TaggingRequestType.Add);

        return result switch
        {
            Success<TaggingRequestEntity> => new RequestTagAddResult(true, $"{selectedTargetUser.UserName} さんにタグ「{targetTagName}」の付与リクエストを送信しました。", Severity.Success, true),
            Failure f => new RequestTagAddResult(false, $"付与リクエストの送信に失敗しました: {f.ErrorMessage}", Severity.Error, false),
            _ => new RequestTagAddResult(false, "付与リクエストの送信に失敗しました。", Severity.Error, false)
        };
    }

    /// <summary>
    ///     MudTable のフィルタ条件。TagSearchQuery に基づいてタグ名およびユーザー名で判定する。
    ///     オーナーが BAN されたタグは不可視（false）とする。
    /// </summary>
    public static bool FilterFunc(TagRelation relation, string? search)
    {
        if (relation.Tag != null && !relation.Tag.IsTagVisibleToUser())
        {
            return false;
        }

        return TagSearchQuery.Parse(search) switch
        {
            EmptySearch => true,
            IncompleteSearch incompleteSearch =>
                relation.Tag?.Name?.Equals(incompleteSearch.TagName, StringComparison.OrdinalIgnoreCase) == true
                || relation.Tag?.Name?.Contains(incompleteSearch.TagName, StringComparison.OrdinalIgnoreCase) == true,
            TagWithUserSearch tagWithUserSearch =>
                (relation.Tag?.Name?.Equals(tagWithUserSearch.TagName, StringComparison.OrdinalIgnoreCase) == true
                 || relation.Tag?.Name?.Contains(tagWithUserSearch.TagName, StringComparison.OrdinalIgnoreCase) == true)
                && MatchUser(relation, tagWithUserSearch.UserName),
            TagNameSearch tagNameSearch =>
                relation.Tag?.Name?.Contains(tagNameSearch.TagName, StringComparison.OrdinalIgnoreCase) == true
                || MatchUser(relation, tagNameSearch.TagName),
            _ => true
        };
    }

    private static bool MatchUser(TagRelation relation, string userName)
    {
        return relation.Tag?.Owner?.UserName?.Contains(userName, StringComparison.OrdinalIgnoreCase) == true
               || relation.Owner?.UserName?.Contains(userName, StringComparison.OrdinalIgnoreCase) == true;
    }

    /// <summary>
    ///     TagSearchBar と同様の 2 段階オートコンプリート候補を返す。
    ///     タグ名入力時: "{TagName} @"
    ///     IncompleteSearch (TagName @) / TagWithUserSearch: "{TagName} @{UserName}"
    /// </summary>
    public static IReadOnlyList<string> GetSearchSuggestions(IEnumerable<TagRelation>? sourceRelations, string? value)
    {
        IEnumerable<TagRelation> relations = (sourceRelations ?? []).Where(r => r.Tag == null || r.Tag.IsTagVisibleToUser());

        return TagSearchQuery.Parse(value) switch
        {
            EmptySearch => [.. relations
                .Where(r => r.Tag?.Name != null)
                .Select(r => r.Tag.Name + " @")
                .Distinct()
                .Take(MaxSuggestionCount)],

            IncompleteSearch incompleteSearch => GetUserSuggestions(relations, incompleteSearch.TagName, string.Empty),

            TagWithUserSearch tagWithUserSearch => GetUserSuggestions(relations, tagWithUserSearch.TagName, tagWithUserSearch.UserName),

            TagNameSearch tagNameSearch => [.. relations
                .Where(r => r.Tag?.Name?.Contains(tagNameSearch.TagName, StringComparison.OrdinalIgnoreCase) == true)
                .Select(r => r.Tag.Name + " @")
                .Distinct()
                .Take(MaxSuggestionCount)],

            _ => []
        };
    }

    private static IReadOnlyList<string> GetUserSuggestions(IEnumerable<TagRelation> relations, string tagName, string userSearch)
    {
        List<string> userNames = [.. relations
            .Where(r => r.Tag?.Name?.Equals(tagName, StringComparison.OrdinalIgnoreCase) == true
                        || r.Tag?.Name?.Contains(tagName, StringComparison.OrdinalIgnoreCase) == true)
            .SelectMany(r => new[] { r.Tag?.Owner?.UserName, r.Owner?.UserName })
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Distinct()
            .Where(u => string.IsNullOrWhiteSpace(userSearch) || u.Contains(userSearch, StringComparison.OrdinalIgnoreCase))
            .OfType<string>()
            .Take(MaxSuggestionCount)];

        return userNames.Count == 0 && string.IsNullOrWhiteSpace(userSearch)
            ? [tagName + " @"]
            : [.. userNames.Select(u => tagName + " @" + u)];
    }
}