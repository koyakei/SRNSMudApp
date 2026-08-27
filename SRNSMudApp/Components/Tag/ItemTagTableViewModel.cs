// CA1508: union 型 (TagSearchQuery) の網羅的パターンマッチにおける解析器の誤検知のため抑制する。
#pragma warning disable CA1508

using SRNSMudApp.Data;
using SRNSMudApp.Models;

namespace SRNSMudApp.Components.Tag;

/// <summary>
///     ItemTagTable コンポーネントに含まれる純粋なビジネスロジックを切り出した ViewModel。
///     UI への依存を持たないため、bUnit を使わずに xUnit で直接単体テストできる。
/// </summary>
public static class ItemTagTableViewModel
{
    private const int MaxSuggestionCount = 10;

    /// <summary>
    ///     MudTable のフィルタ条件。TagSearchQuery に基づいてタグ名およびユーザー名で判定する。
    /// </summary>
    public static bool FilterFunc(TagRelation relation, string? search)
    {
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
        IEnumerable<TagRelation> relations = sourceRelations ?? [];

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
            .Where(u => string.IsNullOrWhiteSpace(userSearch) || u!.Contains(userSearch, StringComparison.OrdinalIgnoreCase))
            .Select(u => u!)
            .Take(MaxSuggestionCount)];

        return userNames.Count == 0 && string.IsNullOrWhiteSpace(userSearch)
            ? [tagName + " @"]
            : [.. userNames.Select(u => tagName + " @" + u)];
    }
}