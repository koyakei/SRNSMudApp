using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Components.UI;
using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;

namespace SRNSMudApp.Components.Tag;

/// <summary>
///     TagTree コンポーネントに含まれる表示・ツリー操作およびデータアクセスロジックを集約する ViewModel。
///     UI への依存を持たないため、bUnit を使わずに xUnit で直接単体テストできる。
/// </summary>
public class TagTreeViewModel
{
    private readonly ITagTreeDataProvider _tagTreeData;
    private readonly ITagLockService _tagLockService;
    private HashSet<int> _lockedTagIds = [];

    public TagTreeViewModel(ITagTreeDataProvider tagTreeData, ITagLockService tagLockService)
    {
        _tagTreeData = tagTreeData;
        _tagLockService = tagLockService;
    }

    [SuppressMessage("Usage", "CA1002:Do not expose generic lists", Justification = "Tree binding requirement")]
    public List<Data.Tag> Tags { get; private set; } = [];

    [SuppressMessage("Usage", "CA1002:Do not expose generic lists", Justification = "Tree binding requirement")]
    public List<PendingTagMoveDto> PendingMoves { get; private set; } = [];
    public IReadOnlySet<int> LockedTagIds => _lockedTagIds;
    public string? CurrentUserId { get; private set; }
    public bool IsAdmin { get; private set; }

    public void SetUser(string? currentUserId, bool isAdmin)
    {
        CurrentUserId = currentUserId;
        IsAdmin = isAdmin;
    }

    public async Task LoadDataAsync()
    {
        Tags = await _tagTreeData.LoadTagsAsync();
        PendingMoves = await _tagTreeData.LoadPendingTagMovesAsync();
        var allStatus = await _tagLockService.GetAllTagsWithLockStatusAsync();
        _lockedTagIds = allStatus.Where(s => s.IsLockedEffective).Select(s => s.Id).ToHashSet();
    }

    public bool IsTagLocked(int tagId) => _lockedTagIds.Contains(tagId);

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "例外をUI向けメッセージに変換するため")]
    public async Task<TagCardActionResult> AddChildTagAsync(int parentId, string name, string? content)
    {
        if (string.IsNullOrEmpty(CurrentUserId))
        {
            return TagCardActionResult.Warning("ログインが必要です。");
        }

        var isRestricted = await _tagLockService.IsChildCreationRestrictedAsync(parentId);
        if (isRestricted && !IsAdmin)
        {
            return TagCardActionResult.Warning("選択された親タグ配下（または兄弟）はロックされているため子タグを作成できません。");
        }

        var trimmedName = name.Trim();
        Data.Tag? existingTag = Tags.FirstOrDefault(t => string.Equals(t.Name.Trim(), trimmedName, StringComparison.OrdinalIgnoreCase));
        if (existingTag != null)
        {
            return TagCardActionResult.Error("同じ名前のタグが既に存在します。", existingTag.Id);
        }

        Data.Tag newTag = new()
        {
            Name = name,
            Content = content ?? string.Empty,
            ParentTagId = parentId,
            OwnerId = CurrentUserId,
            CachedWeight = 0,
            CreatedDate = DateTime.UtcNow,
            UpdatedDate = DateTime.UtcNow
        };

        try
        {
            await _tagTreeData.AddTagAsync(newTag);
            await LoadDataAsync();
            return TagCardActionResult.Success($"'{name}' を追加しました。");
        }
        catch (DbUpdateException ex) when (
            ex.InnerException?.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase) == true
            || ex.InnerException?.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase) == true)
        {
            await LoadDataAsync();
            Data.Tag? dup = Tags.FirstOrDefault(t => string.Equals(t.Name.Trim(), trimmedName, StringComparison.OrdinalIgnoreCase));
            return TagCardActionResult.Error("同じ名前のタグが既に存在します。", dup?.Id);
        }
        catch (Exception ex)
        {
            return TagCardActionResult.Error($"エラーが発生しました: {ex.Message}");
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "例外をUI向けメッセージに変換するため")]
    [SuppressMessage("Maintainability", "CA1508:Avoid dead code", Justification = "Result<T> pattern matching false positive")]
    public async Task<TagCardActionResult> MoveTagAsync(int movedNodeId, int targetNodeId, string position)
    {
        Data.Tag? movedItem = Tags.Find(t => t.Id == movedNodeId);
        Data.Tag? targetItem = Tags.Find(t => t.Id == targetNodeId);

        if (movedItem == null || targetItem == null)
        {
            return TagCardActionResult.NoOp();
        }

        if (IsDescendantOrSelf(Tags, movedItem, targetItem))
        {
            return TagCardActionResult.Warning($"'{movedItem.Name}' を自身の配下 '{targetItem.Name}' に移動することはできません。");
        }

        int? newParentTagId = position switch
        {
            "inside" => targetItem.Id,
            "before" or "after" => targetItem.ParentTagId,
            _ => movedItem.ParentTagId
        };

        if (newParentTagId == movedItem.ParentTagId)
        {
            return TagCardActionResult.NoOp();
        }

        if (!IsAdmin)
        {
            if (await _tagLockService.IsTagOrSiblingLockedAsync(movedItem.Id))
            {
                return TagCardActionResult.Warning($"タグ「{movedItem.Name}」またはその兄弟タグはロックされているため移動できません。");
            }

            if (newParentTagId.HasValue && await _tagLockService.IsChildCreationRestrictedAsync(newParentTagId.Value))
            {
                return TagCardActionResult.Warning("移動先の親タグ配下（または兄弟）はロックされているため移動できません。");
            }
        }

        if (!string.IsNullOrEmpty(movedItem.OwnerId) && movedItem.OwnerId != CurrentUserId && !IsAdmin)
        {
            if (string.IsNullOrEmpty(CurrentUserId))
            {
                return TagCardActionResult.Warning("ログインしていないため、移動リクエストを送信できません。");
            }

            try
            {
                Result<TaggingRequestEntity> requestResult = await _tagTreeData.RequestTagMoveAsync(
                    CurrentUserId, movedItem.Id, newParentTagId);

                return requestResult switch
                {
                    Success<TaggingRequestEntity> =>
                        TagCardActionResult.Success($"タグ「{movedItem.Name}」の移動リクエストが通りました。"),
                    Failure f =>
                        TagCardActionResult.Error($"移動リクエストの送信に失敗しました: {f.ErrorMessage}"),
                    _ => TagCardActionResult.NoOp()
                };
            }
            catch (Exception ex)
            {
                return TagCardActionResult.Error($"移動リクエスト送信中にエラーが発生しました: {ex.Message}");
            }
        }

        movedItem.ParentTagId = newParentTagId;

        try
        {
            if (await _tagTreeData.UpdateParentAsync(movedItem.Id, movedItem.ParentTagId))
            {
                return TagCardActionResult.Success($"タグ「{movedItem.Name}」の移動リクエストが通りました。");
            }

            return TagCardActionResult.NoOp();
        }
        catch (Exception ex)
        {
            return TagCardActionResult.Error($"保存時にエラーが発生しました: {ex.Message}");
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "例外をUI向けメッセージに変換するため")]
    [SuppressMessage("Maintainability", "CA1508:Avoid dead code", Justification = "Result<T> pattern matching false positive")]
    public async Task<TagCardActionResult> CancelMoveRequestAsync(int requestId)
    {
        if (string.IsNullOrEmpty(CurrentUserId))
        {
            return TagCardActionResult.NoOp();
        }

        try
        {
            Result<string> result = await _tagTreeData.CancelTagMoveAsync(requestId, CurrentUserId);
            return result switch
            {
                Success<string> s => TagCardActionResult.Success(s.Value),
                Failure f => TagCardActionResult.Error($"キャンセルに失敗しました: {f.ErrorMessage}"),
                _ => TagCardActionResult.NoOp()
            };
        }
        catch (Exception ex)
        {
            return TagCardActionResult.Error($"キャンセル処理中にエラーが発生しました: {ex.Message}");
        }
    }

    public async Task<TagTreeDeleteResult> DeleteTagsAsync(IReadOnlyList<int> selectedIds)
    {
        if (string.IsNullOrEmpty(CurrentUserId) || selectedIds.Count == 0)
        {
            return new TagTreeDeleteResult(false, 0, [], []);
        }

        TagTreeDeleteResult result = await _tagTreeData.DeleteTagsAsync(CurrentUserId, selectedIds, IsAdmin);
        if (result.HasDeleted)
        {
            await LoadDataAsync();
        }

        return result;
    }

    // タグ階層が深くてもエラーにならないよう MaxDepth を十分に大きくする（CA1869: インスタンス生成はキャッシュ）
    private static readonly JsonSerializerOptions CachedSerializerOptions = new() { MaxDepth = 1024 };

    /// <summary>
    ///     検索語に合致する表示可能タグのID集合を取得する。
    ///     検索語が空または空白の場合は空の集合を返す。
    /// </summary>
    public static IReadOnlySet<int> GetMatchingTagIds(IEnumerable<Data.Tag> tags, string? searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return new HashSet<int>();
        }

        return tags
            .Where(t => t.IsTagVisibleToUser() && t.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase))
            .Select(t => t.Id)
            .ToHashSet();
    }

    /// <summary>
    ///     検索語でタグを絞り込む。空の場合は自分のタグを優先して上位 2000 件返す。BAN されたユーザーのタグは除外する。
    ///     検索語が指定されている場合、合致したタグ自身、その祖先ノード（ルートまでの経路）、およびその配下のサブノード（子孫）を含める。
    /// </summary>
    public static IEnumerable<Data.Tag> FilterTags(IReadOnlyList<Data.Tag> tags, string? searchText, string? currentUserId)
    {
        var visibleTags = tags.Where(t => t.IsTagVisibleToUser()).ToList();
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return visibleTags
                .OrderByDescending(t => t.OwnerId == currentUserId)
                .ThenBy(t => t.Name)
                .Take(2000);
        }

        var matchingIds = GetMatchingTagIds(visibleTags, searchText);
        if (matchingIds.Count == 0)
        {
            return [];
        }

        HashSet<int> resultIds = [.. matchingIds];

        // 祖先ノード（Ancestors）を追加
        foreach (var tagId in matchingIds)
        {
            Data.Tag? current = visibleTags.Find(t => t.Id == tagId);
            while (current?.ParentTagId != null)
            {
                Data.Tag? parent = visibleTags.Find(t => t.Id == current.ParentTagId);
                if (parent == null || !resultIds.Add(parent.Id))
                {
                    break;
                }
                current = parent;
            }
        }

        // サブノード（Descendants）を追加
        var childrenLookup = visibleTags
            .Where(t => t.ParentTagId != null && t.ParentTagId != t.Id)
            .ToLookup(t => t.ParentTagId!.Value);

        Queue<int> queue = new(matchingIds);
        while (queue.Count > 0)
        {
            var currentId = queue.Dequeue();
            foreach (var child in childrenLookup[currentId])
            {
                if (resultIds.Add(child.Id))
                {
                    queue.Enqueue(child.Id);
                }
            }
        }

        return visibleTags.Where(t => resultIds.Contains(t.Id));
    }

    /// <summary>JqTree 用のツリーデータを構築する。</summary>
    public static IReadOnlyList<object> BuildTreeData(int? parentId, IEnumerable<Data.Tag> filteredTags)
        => BuildTreeData(parentId, filteredTags, null, null, null, null);

    public static IReadOnlyList<object> BuildTreeData(
        int? parentId,
        IEnumerable<Data.Tag> filteredTags,
        IReadOnlyList<PendingTagMoveDto>? pendingMoves = null,
        string? currentUserId = null,
        IReadOnlySet<int>? lockedTagIds = null,
        IReadOnlySet<int>? highlightedTagIds = null)
        => BuildTreeDataInternal(
            parentId,
            filteredTags as IReadOnlyCollection<Data.Tag> ?? [.. filteredTags],
            pendingMoves ?? [],
            currentUserId,
            lockedTagIds,
            highlightedTagIds,
            []);

    private static List<object> BuildTreeDataInternal(
        int? parentId,
        IReadOnlyCollection<Data.Tag> tagList,
        IReadOnlyList<PendingTagMoveDto> pendingMoves,
        string? currentUserId,
        IReadOnlySet<int>? lockedTagIds,
        IReadOnlySet<int>? highlightedTagIds,
        HashSet<int> visitedInPath)
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

            var isLocked = (lockedTagIds != null && lockedTagIds.Contains(child.Id)) ||
                           child.IsLocked ||
                           child.Name == Data.Tag.RootTagName;
            var isHighlighted = highlightedTagIds != null && highlightedTagIds.Contains(child.Id);

            HashSet<int> nextVisited = [.. visitedInPath, child.Id];
            List<object> nodeChildren = BuildTreeDataInternal(child.Id, tagList, pendingMoves, currentUserId, lockedTagIds, highlightedTagIds, nextVisited);
            switch (nodeChildren.Count)
            {
                case 0:
                    result.Add(new
                    {
                        id = child.Id,
                        name = child.Name,
                        isLocked,
                        isHighlighted
                    });
                    continue;
                default:
                    break;
            }

            result.Add(new
            {
                id = child.Id,
                name = child.Name,
                isLocked,
                isHighlighted,
                children = nodeChildren
            });
        }

        // 移動申請中のノードをこの親ノード直下に生やす
        IEnumerable<PendingTagMoveDto> matchingMoves = pendingMoves.Where(m => m.NewParentTagId == parentId);
        foreach (PendingTagMoveDto move in matchingMoves)
        {
            var canCancel = !string.IsNullOrEmpty(currentUserId) &&
                            (currentUserId == move.RequesterUserId || currentUserId == move.TagOwnerUserId);

            result.Add(new
            {
                id = $"move-req-{move.RequestId}",
                name = move.TagName,
                isPendingMove = true,
                requestId = move.RequestId,
                targetTagId = move.TagId,
                canCancel
            });
        }

        return result;
    }

    public static string SerializeTreeData(IEnumerable<Data.Tag> filteredTags)
        => SerializeTreeData(filteredTags, null, null, null, null);

    public static string SerializeTreeData(
        IEnumerable<Data.Tag> filteredTags,
        IReadOnlyList<PendingTagMoveDto>? pendingMoves = null,
        string? currentUserId = null,
        IReadOnlySet<int>? lockedTagIds = null,
        IReadOnlySet<int>? highlightedTagIds = null)
    {
        IReadOnlyList<object> treeData = BuildTreeData(null, filteredTags, pendingMoves, currentUserId, lockedTagIds, highlightedTagIds);
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