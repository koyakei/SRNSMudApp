namespace SRNSMudApp.Components.UI;

// 兄弟名前空間の下にある namespace Tag より先に Data.Tag 型を解決させるため、
// エイリアスを名前空間の内側に置く

using Tag = SRNSMudApp.Data.Tag;

/// <summary>現在ユーザーの投票用システムタグ ID。</summary>
public readonly record struct SystemTagIds(int? GoodTagId, int? BadTagId)
{
    /// <summary>good/bad 両方のタグが揃っているかどうか。</summary>
    public bool IsComplete => GoodTagId.HasValue && BadTagId.HasValue;
}

/// <summary>
///     ResourceList コンポーネントに含まれる純粋なロジックを切り出した ViewModel。
///     UI への依存を持たないため、bUnit を使わずに xUnit で直接単体テストできる。
/// </summary>
public static class ResourceListViewModel
{
    /// <summary>
    ///     現在ユーザー所有の投票用システムタグ (good / bad) の ID を返す。
    /// </summary>
    public static SystemTagIds FindSystemTags(IEnumerable<Tag>? tags, string? currentUserId)
    {
        if (tags == null || string.IsNullOrEmpty(currentUserId))
        {
            return default;
        }

        var goodTag = tags.FirstOrDefault(
            t => t.OwnerId == currentUserId && t.Name == "good" && t.IsSystem);
        var badTag = tags.FirstOrDefault(
            t => t.OwnerId == currentUserId && t.Name == "bad" && t.IsSystem);

        return new SystemTagIds(goodTag?.Id, badTag?.Id);
    }

    /// <summary>
    ///     フォーカス対象に応じたスクロール先セレクタを返す。
    ///     タグを優先し、未指定の場合はアイテムへフォールバックする。どちらも無ければ null。
    /// </summary>
    public static string? GetFocusSelector(int? focusTagId, int? focusItemId)
    {
        return (focusTagId, focusItemId) switch
        {
            (int tagId, _) => $"#tag-card-{tagId}",
            (_, int itemId) => $"#item-card-{itemId}",
            _ => null
        };
    }
}
