using SRNSMudApp.Data;

namespace SRNSMudApp.Components.Tag;

// 親名前空間 Tag より先に Data.Tag 型を解決させるため、エイリアスを置く
using Tag = SRNSMudApp.Data.Tag;

/// <summary>
///     添付タグ列の表示状態。
/// </summary>
public readonly record struct AttachedTagsDisplay(
    IReadOnlyList<TagRelationToTag> TagsToDisplay,
    int HiddenCount,
    bool HasManyTags,
    bool IsExpanded)
{
    public const int DisplayLimit = 2;
    public const int ManyTagsThreshold = 3;

    /// <summary>「閉じる」または「+N more」のラベル文字列を返す。</summary>
    public string ToggleLabel => IsExpanded ? "閉じる" : $"+{HiddenCount} more";
}

/// <summary>
///     TagTable コンポーネントに含まれる純粋なビジネスロジックを切り出した ViewModel。
///     UI への依存を持たないため、bUnit を使わずに xUnit で直接単体テストできる。
/// </summary>
public static class TagTableViewModel
{
    /// <summary>
    ///     MudTable のフィルタ条件。検索語が空の場合はすべて表示する。
    /// </summary>
    public static bool FilterFunc(Tag tag, string? search)
    {
        return string.IsNullOrWhiteSpace(search) ||
               tag.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               tag.Content?.Contains(search, StringComparison.OrdinalIgnoreCase) == true ||
               tag.Owner?.UserName?.Contains(search, StringComparison.OrdinalIgnoreCase) == true;
    }

    /// <summary>
    ///     オートコンプリート用のタグ名候補を返す。重複は除去し最大 20 件に制限する。
    /// </summary>
    public static IReadOnlyList<string> GetTagSearchSuggestions(IEnumerable<Tag>? sourceTags, string? value)
    {
        var tags = sourceTags ?? [];
        return string.IsNullOrEmpty(value)
            ? [.. tags.Select(t => t.Name).Distinct().Take(20)]
            : [.. tags
                .Where(t => t.Name.Contains(value, StringComparison.OrdinalIgnoreCase))
                .Select(t => t.Name)
                .Distinct()
                .Take(20)];
    }

    /// <summary>
    ///     対象タグに添付された（システムタグ以外の）タグ関係をウェイト降順で返す。
    /// </summary>
    public static IReadOnlyList<TagRelationToTag> GetAttachedTags(Tag tag)
    {
        return tag.TargetTagRelations?
                   .Where(tr => tr.Tag?.IsSystem == false)
                   .OrderByDescending(tr => tr.Weight)
                   .ToList()
               ?? [];
    }

    /// <summary>
    ///     展開状態を考慮して、添付タグ列に表示するタグ関係と隠し件数を計算する。
    /// </summary>
    public static AttachedTagsDisplay GetAttachedTagsDisplay(Tag tag, bool isExpanded)
    {
        var allTags = GetAttachedTags(tag);
        var hasManyTags = allTags.Count >= AttachedTagsDisplay.ManyTagsThreshold;
        var hiddenCount = hasManyTags && !isExpanded
            ? allTags.Count - AttachedTagsDisplay.DisplayLimit
            : 0;

        return new AttachedTagsDisplay(
            hasManyTags && !isExpanded
                ? [.. allTags.Take(AttachedTagsDisplay.DisplayLimit)]
                : allTags,
            hiddenCount,
            hasManyTags,
            isExpanded);
    }

    /// <summary>
    ///     現在のユーザーがそのタグを編集できるかどうかを返す。
    /// </summary>
    public static bool CanEditTag(Tag tag, string? currentUserId) => tag.OwnerId == currentUserId;

    /// <summary>
    ///     現在のユーザーがそのタグを削除できるかどうかを返す（システムタグは不可）。
    /// </summary>
    public static bool CanDeleteTag(Tag tag, string? currentUserId) =>
        tag.OwnerId == currentUserId && !tag.IsSystem;

    /// <summary>
    ///     現在のユーザーがそのタグ関連付けを解除できるかどうかを返す。
    /// </summary>
    public static bool CanRemoveRelation(TagRelationToTag relation, string? currentUserId) =>
        relation.OwnerId == currentUserId;
}
