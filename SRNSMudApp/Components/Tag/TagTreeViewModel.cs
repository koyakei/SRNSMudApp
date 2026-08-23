#region

using System.Text.Json;

#endregion

namespace SRNSMudApp.Components.Tag;

// 兄弟名前空間 SRNSMudApp.Components.Tag より先に Data.Tag 型を解決させるため、
// using を名前空間の内側に置く

using Tag = SRNSMudApp.Data.Tag;

/// <summary>
///     TagTree コンポーネントに含まれる純粋な表示・ツリー操作ロジックを切り出した ViewModel。
///     UI への依存を持たないため、bUnit を使わずに xUnit で直接単体テストできる。
/// </summary>
public static class TagTreeViewModel
{
    /// <summary>検索語でタグを絞り込む。空の場合は自分のタグを優先して上位 2000 件返す。</summary>
    public static IEnumerable<Tag> FilterTags(List<Tag> tags, string? searchText, string? currentUserId)
    {
        switch (string.IsNullOrWhiteSpace(searchText))
        {
            case true:
                return tags
                    .OrderByDescending(t => t.OwnerId == currentUserId)
                    .ThenBy(t => t.Name)
                    .Take(2000);
        }

        var baseTags = tags.Where(t => t.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase)).ToList();
        HashSet<int> resultIds = [];

        foreach (Tag tag in baseTags)
        {
            resultIds.Add(tag.Id);

            Tag current = tag;
            var stopAncestors = false;
            while (current.ParentTagId != null && !stopAncestors)
            {
                Tag? parent = tags.FirstOrDefault(t => t.Id == current.ParentTagId);
                stopAncestors = parent == null || !resultIds.Add(parent.Id);
                switch (stopAncestors)
                {
                    case false:
                        current = parent!;
                        break;
                }
            }
        }

        return tags.Where(t => resultIds.Contains(t.Id));
    }

    /// <summary>JqTree 用のツリーデータを構築する。</summary>
    public static List<object> BuildTreeData(int? parentId, IEnumerable<Tag> filteredTags)
    {
        IReadOnlyCollection<Tag> tagList = filteredTags as IReadOnlyCollection<Tag> ?? [.. filteredTags];
        List<object> result = [];
        List<Tag> children;

        switch (parentId)
        {
            case null:
                {
                    var allFilteredIds = tagList.Select(t => t.Id).ToHashSet();
                    children =
                    [
                        .. tagList
                        .Where(t => t.ParentTagId == null || !allFilteredIds.Contains(t.ParentTagId.Value))
                        .OrderBy(t => t.Name)
                    ];
                    break;
                }
            default:
                children = [.. tagList.Where(t => t.ParentTagId == parentId).OrderBy(t => t.Name)];
                break;
        }

        foreach (Tag child in children)
        {
            List<object> nodeChildren = BuildTreeData(child.Id, tagList);
            switch (nodeChildren.Count)
            {
                case 0:
                    result.Add(new
                    {
                        id = child.Id,
                        name = child.Name
                    });
                    continue;
            }

            result.Add(new
            {
                id = child.Id,
                name = child.Name,
                children = nodeChildren
            });
        }

        return result;
    }

    public static string SerializeTreeData(IEnumerable<Tag> filteredTags)
    {
        List<object> treeData = BuildTreeData(null, filteredTags);
        // タグ階層が深くてもエラーにならないよう MaxDepth を十分に大きくする
        JsonSerializerOptions options = new() { MaxDepth = 1024 };
        return JsonSerializer.Serialize(treeData, options);
    }

    /// <summary>target が parent 自身またはその子孫かどうかを判定する。</summary>
    public static bool IsDescendantOrSelf(List<Tag> tags, Tag parent, Tag target)
    {
        switch (parent.Id == target.Id)
        {
            case true: return true;
        }

        IEnumerable<Tag> children = tags.Where(t => t.ParentTagId == parent.Id);
        return children.Any(child => IsDescendantOrSelf(tags, child, target));
    }

    /// <summary>
    ///     親子関係の循環参照を検出してメモリ上で解除する。
    ///     解除された (ParentTagId が null になった) タグ一覧を返す。
    /// </summary>
    public static List<Tag> DetectAndBreakCycles(List<Tag> tags)
    {
        HashSet<int> visited = [];
        HashSet<int> recursionStack = [];
        List<Tag> repaired = [];

        foreach (Tag tag in tags)
        {
            switch (visited.Contains(tag.Id))
            {
                case true: continue;
            }

            Tag? current = tag;
            List<Tag> path = [];

            while (current != null && !recursionStack.Contains(current.Id) && !visited.Contains(current.Id))
            {
                recursionStack.Add(current.Id);
                path.Add(current);
                current = current.ParentTagId != null ? tags.FirstOrDefault(t => t.Id == current.ParentTagId) : null;
            }

            switch (current)
            {
                case not null when recursionStack.Contains(current.Id):
                    current.ParentTagId = null;
                    repaired.Add(current);
                    break;
            }

            foreach (Tag p in path)
            {
                recursionStack.Remove(p.Id);
                visited.Add(p.Id);
            }
        }

        return repaired;
    }
}