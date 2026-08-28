namespace SRNSMudApp.Components.UI;

/// <summary>現在ユーザーの投票用システムタグ ID。</summary>
public readonly record struct SystemTagIds(int? GoodTagId, int? BadTagId)
{
    /// <summary>good/bad 両方のタグが揃っているかどうか。</summary>
    public bool IsComplete => GoodTagId.HasValue && BadTagId.HasValue;
}

/// <summary>現在ユーザーのリアクション用システムタグ ID（真実・善・美）。</summary>
public readonly record struct ReactionTagIds(int? ShinjiTagId, int? ZenTagId, int? BiTagId)
{
    /// <summary>真実・善・美すべてのタグが揃っているかどうか。</summary>
    public bool IsComplete => ShinjiTagId.HasValue && ZenTagId.HasValue && BiTagId.HasValue;
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
    public static SystemTagIds FindSystemTags(IEnumerable<Data.Tag>? tags, string? currentUserId)
    {
        if (tags == null || string.IsNullOrEmpty(currentUserId))
        {
            return default;
        }

        // CA1851: 複数回の列挙を避けるため一度材料化する
        List<Data.Tag> tagList = [.. tags];
        Data.Tag? goodTag = tagList.Find(
            t => t.OwnerId == currentUserId && t.Name == "good" && t.IsSystem);
        Data.Tag? badTag = tagList.Find(
            t => t.OwnerId == currentUserId && t.Name == "bad" && t.IsSystem);

        return new SystemTagIds(goodTag?.Id, badTag?.Id);
    }

    /// <summary>
    ///     現在ユーザー所有のリアクション用システムタグ (真実 / 善 / 美) の ID を返す。
    /// </summary>
    public static ReactionTagIds FindReactionTags(IEnumerable<Data.Tag>? tags, string? currentUserId)
    {
        if (tags == null || string.IsNullOrEmpty(currentUserId))
        {
            return default;
        }

        List<Data.Tag> tagList = [.. tags];
        Data.Tag? shinjiTag = tagList.Find(
            t => t.OwnerId == currentUserId && t.Name == "真実" && t.IsSystem);
        Data.Tag? zenTag = tagList.Find(
            t => t.OwnerId == currentUserId && t.Name == "善" && t.IsSystem);
        Data.Tag? biTag = tagList.Find(
            t => t.OwnerId == currentUserId && t.Name == "美" && t.IsSystem);

        return new ReactionTagIds(shinjiTag?.Id, zenTag?.Id, biTag?.Id);
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