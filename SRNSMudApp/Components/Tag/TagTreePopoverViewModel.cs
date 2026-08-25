#region

using SRNSMudApp.Models.Unions;

#endregion

// 親名前空間 Tag より先に Data.Tag 型を解決させるため、エイリアスを置く
namespace SRNSMudApp.Components.Tag;

using Tag = SRNSMudApp.Data.Tag;

/// <summary>タグツリーポップオーバーの 1 行分の表示データ。</summary>
public record TagTreeLine(
    int TagId,
    string Indent,
    string Icon,
    string Name,
    string Role,
    bool IsCurrent,
    bool IsParent,
    bool IsChild,
    bool IsSibling,
    bool HasChildren,
    bool IsExpanded
);

/// <summary>
///     TagTreePopoverContent コンポーネントに含まれる純粋なツリー構築ロジックを切り出した ViewModel。
///     UI への依存を持たないため、bUnit を使わずに xUnit で直接単体テストできる。
/// </summary>
public static class TagTreePopoverViewModel
{
    /// <summary>
    ///     対象タグとその祖先を自動展開対象として返す。
    /// </summary>
    public static HashSet<int> GetAutoExpandIds(Tag targetTag, IReadOnlyList<Tag> allTags)
    {
        HashSet<int> expanded = [];
        if (targetTag == null || targetTag.GetKind() is VoteTag or ReactionTag)
        {
            return expanded;
        }

        var filteredTags = allTags.Where(t => t.GetKind() is not (VoteTag or ReactionTag)).ToList();
        var current = filteredTags.FirstOrDefault(t => t.Id == targetTag.Id);
        while (current != null)
        {
            _ = expanded.Add(current.Id);
            current = filteredTags.FirstOrDefault(t => t.Id == (current.ParentTagId != 0 ? current.ParentTagId : -1));
        }

        return expanded;
    }

    /// <summary>
    ///     ルートから対象タグまでの木構造行を構築する。
    /// </summary>
    /// <param name="targetTag">着目対象のタグ。</param>
    /// <param name="allTags">全タグ（null 非保証）。</param>
    /// <param name="expandedTagIds">展開中のタグ ID。</param>
    /// <param name="enableExpand">展開操作が有効かどうか。</param>
    public static IReadOnlyList<TagTreeLine> BuildTreeLines(
        Tag targetTag,
        IReadOnlyList<Tag> allTags,
        HashSet<int> expandedTagIds,
        bool enableExpand)
    {
        var lines = new List<TagTreeLine>();
        if (targetTag == null || allTags.Count == 0 || targetTag.GetKind() is VoteTag or ReactionTag)
        {
            return lines;
        }

        var filteredTags = allTags.Where(t => t.GetKind() is not (VoteTag or ReactionTag)).ToList();
        if (filteredTags.Count == 0)
        {
            return lines;
        }

        // 対象タグのルート（最上位の祖先）を求める
        var ancestors = new HashSet<int>();
        var curr = filteredTags.FirstOrDefault(t => t.Id == (targetTag.ParentTagId != 0 ? targetTag.ParentTagId : -1));
        var rootTag = targetTag;
        while (curr != null)
        {
            _ = ancestors.Add(curr.Id);
            rootTag = curr;
            curr = filteredTags.FirstOrDefault(t => t.Id == (curr.ParentTagId != 0 ? curr.ParentTagId : -1));
        }

        var siblings = filteredTags
            .Where(t => t.ParentTagId == targetTag.ParentTagId && t.Id != targetTag.Id)
            .Select(t => t.Id)
            .ToHashSet();

        AddTreeLinesRecursive(lines, rootTag, 0, targetTag.Id, ancestors, siblings, filteredTags, expandedTagIds, enableExpand);
        return lines;
    }

    private static void AddTreeLinesRecursive(
        List<TagTreeLine> lines,
        Tag current,
        int depth,
        int targetTagId,
        HashSet<int> ancestors,
        HashSet<int> siblings,
        IReadOnlyList<Tag> allTags,
        HashSet<int> expandedTagIds,
        bool enableExpand)
    {
        var children = allTags.Where(t => t.ParentTagId == current.Id).OrderBy(t => t.Name).ToList();
        var hasChildren = children.Count > 0;
        var isExpanded = expandedTagIds.Contains(current.Id) || !enableExpand;
        var isCurrent = current.Id == targetTagId;

        var isParent = ancestors.Contains(current.Id);
        var isSibling = siblings.Contains(current.Id);
        var isChild = current.ParentTagId == targetTagId;

        var role = isCurrent ? "自身" : isParent ? "親" : isSibling ? "兄弟" : isChild ? "子" : "";

        var indent = new string(' ', depth * 2);
        var icon = GetIcon(enableExpand, hasChildren, isExpanded, isParent, isSibling, isCurrent, isChild);

        lines.Add(new TagTreeLine(current.Id, indent, icon, current.Name, role,
            isCurrent, isParent, isChild, isSibling, hasChildren, isExpanded));

        if (!isExpanded && enableExpand)
        {
            return;
        }

        foreach (Tag child in children)
        {
            if (enableExpand || isParent || isCurrent || isChild)
            {
                AddTreeLinesRecursive(lines, child, depth + 1, targetTagId,
                    ancestors, siblings, allTags, expandedTagIds, enableExpand);
            }
        }
    }

    private static string GetIcon(
        bool enableExpand,
        bool hasChildren,
        bool isExpanded,
        bool isParent,
        bool isSibling,
        bool isCurrent,
        bool isChild)
    {
        return (enableExpand, hasChildren) switch
        {
            (true, true) => isExpanded ? "▼" : "▶",
            (false, true) => isParent || isCurrent ? isExpanded ? "▼" : "▶" : isChild ? "▶" : "▷",
            _ => true switch
            {
                _ when isParent => "▷",
                _ when isSibling => "◇",
                _ when isCurrent => "◆",
                _ when isChild => "▶",
                _ => "▷"
            }
        };
    }
}