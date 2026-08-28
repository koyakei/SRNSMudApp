#region

using System.Text.Json;

#endregion

namespace SRNSMudApp.Components.Tag;

/// <summary>
///     TagTree コンポーネントに含まれる純粋な表示・ツリー操作ロジックを切り出した ViewModel。
///     UI への依存を持たないため、bUnit を使わずに xUnit で直接単体テストできる。
/// </summary>
public static class TagTreeViewModel
{
    // タグ階層が深くてもエラーにならないよう MaxDepth を十分に大きくする（CA1869: インスタンス生成はキャッシュ）
    private static readonly JsonSerializerOptions CachedSerializerOptions = new() { MaxDepth = 1024 };

    /// <summary>検索語でタグを絞り込む。空の場合は自分のタグを優先して上位 2000 件返す。</summary>
    public static IEnumerable<Data.Tag> FilterTags(IReadOnlyList<Data.Tag> tags, string? searchText, string? currentUserId)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return tags
                .OrderByDescending(t => t.OwnerId == currentUserId)
                .ThenBy(t => t.Name)
                .Take(2000);
        }

        var baseTags = tags.Where(t => t.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase)).ToList();
        HashSet<int> resultIds = [];

        foreach (Data.Tag tag in baseTags)
        {
            _ = resultIds.Add(tag.Id);

            Data.Tag current = tag;
            var stopAncestors = false;
            while (current.ParentTagId != null && !stopAncestors)
            {
                Data.Tag? parent = tags.FirstOrDefault(t => t.Id == current.ParentTagId);
                stopAncestors = parent == null || !resultIds.Add(parent.Id);
                if (!stopAncestors)
                {
                    current = parent!;
                }
            }
        }

        return tags.Where(t => resultIds.Contains(t.Id));
    }

    /// <summary>JqTree 用のツリーデータを構築する。</summary>
    public static IReadOnlyList<object> BuildTreeData(int? parentId, IEnumerable<Data.Tag> filteredTags)
        => BuildTreeDataInternal(parentId, filteredTags as IReadOnlyCollection<Data.Tag> ?? [.. filteredTags], []);

    private static List<object> BuildTreeDataInternal(int? parentId, IReadOnlyCollection<Data.Tag> tagList, HashSet<int> visitedInPath)
    {
        List<object> result = [];
        List<Data.Tag> children;

        switch (parentId)
        {
            case null:
                {
                    var allFilteredIds = tagList.Select(t => t.Id).ToHashSet();
                    children =
                    [
                        .. tagList
                        .Where(t => t.ParentTagId == null || t.ParentTagId == t.Id || !allFilteredIds.Contains(t.ParentTagId.Value))
                        .OrderBy(t => t.Name)
                    ];
                    break;
                }
            default:
                children = [.. tagList.Where(t => t.ParentTagId == parentId && t.ParentTagId != t.Id).OrderBy(t => t.Name)];
                break;
        }

        foreach (Data.Tag child in children)
        {
            if (visitedInPath.Contains(child.Id))
            {
                continue;
            }

            HashSet<int> nextVisited = [.. visitedInPath, child.Id];
            List<object> nodeChildren = BuildTreeDataInternal(child.Id, tagList, nextVisited);
            switch (nodeChildren.Count)
            {
                case 0:
                    result.Add(new
                    {
                        id = child.Id,
                        name = child.Name
                    });
                    continue;
                default:
                    break;
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

    public static string SerializeTreeData(IEnumerable<Data.Tag> filteredTags)
    {
        IReadOnlyList<object> treeData = BuildTreeData(null, filteredTags);
        return JsonSerializer.Serialize(treeData, CachedSerializerOptions);
    }

    /// <summary>target が parent 自身またはその子孫かどうかを判定する。</summary>
    public static bool IsDescendantOrSelf(IReadOnlyList<Data.Tag> tags, Data.Tag parent, Data.Tag target)
    {
        if (parent.Id == target.Id)
        {
            return true;
        }

        IEnumerable<Data.Tag> children = tags.Where(t => t.ParentTagId == parent.Id);
        return children.Any(child => IsDescendantOrSelf(tags, child, target));
    }

    /// <summary>
    ///     親子関係の循環参照を検出してメモリ上で解除する。
    ///     解除された (ParentTagId が null になった) タグ一覧を返す。
    /// </summary>
    public static IReadOnlyList<Data.Tag> DetectAndBreakCycles(IReadOnlyList<Data.Tag> tags)
    {
        HashSet<int> visited = [];
        HashSet<int> recursionStack = [];
        List<Data.Tag> repaired = [];

        foreach (Data.Tag tag in tags)
        {
            if (visited.Contains(tag.Id))
            {
                continue;
            }

            Data.Tag? current = tag;
            List<Data.Tag> path = [];

            while (current != null && !recursionStack.Contains(current.Id) && !visited.Contains(current.Id))
            {
                _ = recursionStack.Add(current.Id);
                path.Add(current);
                current = current.ParentTagId != null ? tags.FirstOrDefault(t => t.Id == current.ParentTagId) : null;
            }

            switch (current)
            {
                case not null when recursionStack.Contains(current.Id):
                    current.ParentTagId = null;
                    repaired.Add(current);
                    break;
                default:
                    break;
            }

            foreach (Data.Tag p in path)
            {
                _ = recursionStack.Remove(p.Id);
                _ = visited.Add(p.Id);
            }
        }

        return repaired;
    }
}