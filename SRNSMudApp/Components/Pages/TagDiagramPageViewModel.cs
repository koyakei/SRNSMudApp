using System.Diagnostics.CodeAnalysis;

using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;

using ItemEntity = SRNSMudApp.Data.Item;
using TagEntity = SRNSMudApp.Data.Tag;

namespace SRNSMudApp.Components.Pages;

/// <summary>
///     TagDiagramPage のデータフェッチ、フィルタリング計算、およびエッジ操作ロジックを集約する ViewModel。
///     UI (MudBlazor / Blazor.Diagrams) への依存を持たないため、bUnit を使わずに xUnit で直接単体テスト可能。
/// </summary>
public class TagDiagramPageViewModel
{
    private readonly ITagDiagramDataProvider _dataProvider;

    public TagDiagramPageViewModel(ITagDiagramDataProvider dataProvider)
    {
        _dataProvider = dataProvider;
    }

    public string CurrentUserId { get; set; } = "";
    public bool IsLoading { get; private set; } = true;

    [SuppressMessage("Usage", "CA1002:Do not expose generic lists", Justification = "Blazor component binding requirement")]
    public List<TagEntity> Tags { get; private set; } = [];

    [SuppressMessage("Usage", "CA1002:Do not expose generic lists", Justification = "Blazor component binding requirement")]
    public List<TagEdge> Edges { get; private set; } = [];

    [SuppressMessage("Usage", "CA1002:Do not expose generic lists", Justification = "Blazor component binding requirement")]
    public List<ItemEntity> ContextItems { get; private set; } = [];

    public HashSet<int> PinnedTagIds { get; } = [];
    public HashSet<int> HiddenTagIds { get; } = [];
    public HashSet<int> HiddenItemIds { get; } = [];

    public TagEdge? SelectedEdge { get; set; }
    public TagEntity? FocusedTag { get; set; }
    public TagEntity? SecondFocusedTag { get; set; }
    public int? LoadedContextItemId { get; set; }

    // フィルタ設定
    public bool OnlyConnectedTags { get; set; } = true;
    public bool NeighborhoodOnly { get; set; }
    public bool ContextOnly { get; set; } = true;
    public int DisplayedTagsCount { get; private set; }
    public int DisplayedEdgesCount { get; private set; }

    // エッジ作成モード関連の内部状態
    public bool IsEdgeCreationMode { get; private set; }
    public TagEntity? EdgeCreationSourceTag { get; set; }
    public TagEntity? EdgeCreationTargetTag { get; set; }
    public TagEntity? EdgeCreationAttachTag { get; private set; }
    public RightAsset? EdgeCreationAttachAsset { get; set; }

    [SuppressMessage("Usage", "CA1002:Do not expose generic lists", Justification = "Blazor component binding requirement")]
    public List<RightAsset> EdgeCreationAvailableAssets { get; } = [];

    public int EdgeCreationWeight { get; set; } = 1;
    public bool IsLoadingAttachAssets { get; private set; }
    public bool IsSelectingTargetInEdgeMode { get; set; }

    public bool CanCreateEdgeInMode =>
        !string.IsNullOrEmpty(CurrentUserId) &&
        EdgeCreationSourceTag != null &&
        EdgeCreationTargetTag != null &&
        EdgeCreationSourceTag.Id != EdgeCreationTargetTag.Id &&
        !IsLoadingAttachAssets;

    /// <summary>
    ///     ダイアグラムのデータ（タグ・エッジ・アイテム）を再読み込みする。
    /// </summary>
    public async Task ReloadDiagramAsync(int? queryItemId, bool preserveExtraVisibleTags = false)
    {
        var preservedExtraIds = preserveExtraVisibleTags ? PinnedTagIds.ToHashSet() : null;
        var preservedHiddenTagIds = preserveExtraVisibleTags ? HiddenTagIds.ToHashSet() : null;
        var preservedHiddenItemIds = preserveExtraVisibleTags ? HiddenItemIds.ToHashSet() : null;
        IsLoading = true;
        SelectedEdge = null;
        PinnedTagIds.Clear();
        HiddenTagIds.Clear();
        HiddenItemIds.Clear();

        try
        {
            Tags = await _dataProvider.LoadAllTagsAsync();
            IReadOnlyList<TagEdge> edgeList = await _dataProvider.LoadAllEdgesAsync();
            Edges = edgeList.ToList();

            if (preservedExtraIds != null)
            {
                foreach (int id in preservedExtraIds)
                {
                    _ = PinnedTagIds.Add(id);
                }
            }

            if (preservedHiddenTagIds != null)
            {
                foreach (int id in preservedHiddenTagIds)
                {
                    _ = HiddenTagIds.Add(id);
                }
            }

            if (preservedHiddenItemIds != null)
            {
                foreach (int id in preservedHiddenItemIds)
                {
                    _ = HiddenItemIds.Add(id);
                }
            }

            if (queryItemId.HasValue)
            {
                LoadedContextItemId = queryItemId.Value;
                List<int> contextTagIds = await _dataProvider.GetContextTagIdsForItemAsync(queryItemId.Value);
                foreach (int id in contextTagIds)
                {
                    _ = PinnedTagIds.Add(id);
                }
                ContextItems = await _dataProvider.GetContextItemsAsync(queryItemId.Value);
            }
            else
            {
                ContextItems = [];
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    ///     コンテキストアイテム ID が変更された場合にコンテキストタグとアイテムを更新する。
    /// </summary>
    public async Task<bool> UpdateContextItemAsync(int? queryItemId)
    {
        if (queryItemId == LoadedContextItemId)
        {
            return false;
        }

        LoadedContextItemId = queryItemId;
        if (queryItemId.HasValue)
        {
            List<int> contextTagIds = await _dataProvider.GetContextTagIdsForItemAsync(queryItemId.Value);
            foreach (int id in contextTagIds)
            {
                _ = PinnedTagIds.Add(id);
            }
            ContextItems = await _dataProvider.GetContextItemsAsync(queryItemId.Value);
        }
        else
        {
            ContextItems = [];
        }

        return true;
    }

    /// <summary>
    ///     クエリパラメータ（タグ1, タグ2, エッジ）を反映する。変更があった場合 true を返す。
    /// </summary>
    public bool ApplyQueryParameters(int? queryTag1, int? queryTag2, int? queryEdge)
    {
        bool changed = false;

        TagEntity? newTag1 = queryTag1.HasValue ? Tags.FirstOrDefault(t => t.Id == queryTag1.Value) : null;
        if ((FocusedTag?.Id ?? 0) != (newTag1?.Id ?? 0))
        {
            FocusedTag = newTag1;
            changed = true;
        }

        TagEntity? newTag2 = queryTag2.HasValue ? Tags.FirstOrDefault(t => t.Id == queryTag2.Value) : null;
        if ((SecondFocusedTag?.Id ?? 0) != (newTag2?.Id ?? 0))
        {
            SecondFocusedTag = newTag2;
            changed = true;
        }

        TagEdge? newEdge = queryEdge.HasValue ? Edges.FirstOrDefault(e => e.Id == queryEdge.Value) : null;
        if ((SelectedEdge?.Id ?? 0) != (newEdge?.Id ?? 0))
        {
            SelectedEdge = newEdge;
            changed = true;
        }

        return changed;
    }

    /// <summary>
    ///     現在のフィルタ設定（エッジ接続、周辺近傍、アイテムコンテキスト、ピン留め）に基づき
    ///     表示すべきタグ一覧を算出する。
    /// </summary>
    public IReadOnlyList<TagEntity> GetTagsToDisplay(int? queryItemId)
    {
        List<TagEdge> activeEdges = Edges
            .Where(e => !HiddenTagIds.Contains(e.SourceTagId) && !HiddenTagIds.Contains(e.TargetTagId))
            .ToList();
        DisplayedEdgesCount = activeEdges.Count;

        HashSet<int> connectedTagIds = activeEdges
            .SelectMany(e => new[] { e.SourceTagId, e.TargetTagId })
            .ToHashSet();

        IEnumerable<TagEntity> tagsToDisplay;

        if (queryItemId.HasValue && ContextOnly && PinnedTagIds.Count > 0)
        {
            tagsToDisplay = Tags.Where(t => PinnedTagIds.Contains(t.Id)
                || (FocusedTag != null && t.Id == FocusedTag.Id)
                || (SecondFocusedTag != null && t.Id == SecondFocusedTag.Id));
        }
        else if (NeighborhoodOnly && (FocusedTag != null || SecondFocusedTag != null))
        {
            HashSet<int> focusIds = [];
            if (FocusedTag != null)
            {
                _ = focusIds.Add(FocusedTag.Id);
            }
            if (SecondFocusedTag != null)
            {
                _ = focusIds.Add(SecondFocusedTag.Id);
            }

            HashSet<int> neighborIds = activeEdges
                .Where(e => focusIds.Contains(e.SourceTagId) || focusIds.Contains(e.TargetTagId))
                .SelectMany(e => new[] { e.SourceTagId, e.TargetTagId })
                .Concat(focusIds)
                .Concat(PinnedTagIds)
                .ToHashSet();

            tagsToDisplay = Tags.Where(t => neighborIds.Contains(t.Id));
        }
        else if (OnlyConnectedTags)
        {
            tagsToDisplay = Tags.Where(t => connectedTagIds.Contains(t.Id)
                || (FocusedTag != null && t.Id == FocusedTag.Id)
                || (SecondFocusedTag != null && t.Id == SecondFocusedTag.Id)
                || PinnedTagIds.Contains(t.Id));
        }
        else
        {
            tagsToDisplay = Tags;
        }

        if (HiddenTagIds.Count > 0)
        {
            tagsToDisplay = tagsToDisplay.Where(t => !HiddenTagIds.Contains(t.Id));
        }

        List<TagEntity> list = tagsToDisplay.ToList();
        DisplayedTagsCount = list.Count;
        return list;
    }

    public async Task<Result<TagEdge>> CreateEdgeAsync(int sourceTagId, int targetTagId)
    {
        return await _dataProvider.CreateEdgeAsync(sourceTagId, targetTagId, CurrentUserId);
    }

    public async Task<Result<bool>> DeleteEdgeAsync(int edgeId)
    {
        return await _dataProvider.DeleteEdgeAsync(edgeId, CurrentUserId);
    }

    public async Task<Result<TagEdgeTagAttachment>> AttachTagToEdgeAsync(int edgeId, int tagId, int rightAssetId, int weight)
    {
        return await _dataProvider.AttachTagToEdgeAsync(edgeId, tagId, rightAssetId, CurrentUserId, weight);
    }

    public async Task<Result<bool>> DetachTagFromEdgeAsync(int attachmentId)
    {
        return await _dataProvider.DetachTagFromEdgeAsync(attachmentId, CurrentUserId);
    }

    public async Task SetEdgeCreationAttachTagAsync(TagEntity? tag)
    {
        EdgeCreationAttachTag = tag;
        EdgeCreationAttachAsset = null;
        EdgeCreationAvailableAssets.Clear();

        if (tag == null || string.IsNullOrWhiteSpace(CurrentUserId))
        {
            return;
        }

        IsLoadingAttachAssets = true;
        try
        {
            List<RightAsset>? assets = await _dataProvider.GetAvailableRightAssetsAsync(CurrentUserId, tag.Id);
            if (assets != null)
            {
                EdgeCreationAvailableAssets.AddRange(assets);
            }
            EdgeCreationAttachAsset = EdgeCreationAvailableAssets.FirstOrDefault();
            EdgeCreationWeight = 1;
        }
        finally
        {
            IsLoadingAttachAssets = false;
        }
    }

    public void StartEdgeCreationMode()
    {
        IsEdgeCreationMode = true;
        IsSelectingTargetInEdgeMode = false;
        EdgeCreationSourceTag = null;
        EdgeCreationTargetTag = null;
    }

    public async Task<int> EnterEdgeCreationModeAsync()
    {
        StartEdgeCreationMode();
        int newlyAdded = 0;
        if (FocusedTag != null)
        {
            newlyAdded = PinChildTags(FocusedTag.Id);
            await SetEdgeCreationAttachTagAsync(FocusedTag);
        }
        else
        {
            EdgeCreationAttachTag = null;
            EdgeCreationAttachAsset = null;
            EdgeCreationAvailableAssets.Clear();
        }
        return newlyAdded;
    }

    public void ExitEdgeCreationMode()
    {
        IsEdgeCreationMode = false;
        EdgeCreationSourceTag = null;
        EdgeCreationTargetTag = null;
        EdgeCreationAttachTag = null;
        EdgeCreationAttachAsset = null;
        EdgeCreationAvailableAssets.Clear();
        IsSelectingTargetInEdgeMode = false;
    }

    public void SwapFocusedTags()
    {
        (FocusedTag, SecondFocusedTag) = (SecondFocusedTag, FocusedTag);
    }

    public void SwapEdgeCreationTags()
    {
        (EdgeCreationSourceTag, EdgeCreationTargetTag) = (EdgeCreationTargetTag, EdgeCreationSourceTag);
    }

    public void ClearFirstTag()
    {
        FocusedTag = SecondFocusedTag;
        SecondFocusedTag = null;
        if (FocusedTag == null)
        {
            ClearFocus();
        }
    }

    public void ClearSecondTag()
    {
        SecondFocusedTag = null;
    }

    public void ClearFocus()
    {
        FocusedTag = null;
        SecondFocusedTag = null;
        NeighborhoodOnly = false;
        ExitEdgeCreationMode();
    }

    /// <summary>
    ///     指定したタグを画面表示から消す（非表示にする）。
    /// </summary>
    /// <param name="tagId">非表示にするタグID。</param>
    public void HideTag(int tagId)
    {
        _ = HiddenTagIds.Add(tagId);
        _ = PinnedTagIds.Remove(tagId);

        if (FocusedTag?.Id == tagId)
        {
            ClearFirstTag();
        }
        else if (SecondFocusedTag?.Id == tagId)
        {
            ClearSecondTag();
        }

        if (SelectedEdge?.SourceTagId == tagId || SelectedEdge?.TargetTagId == tagId)
        {
            SelectedEdge = null;
        }

        if (EdgeCreationSourceTag?.Id == tagId)
        {
            EdgeCreationSourceTag = null;
        }
        if (EdgeCreationTargetTag?.Id == tagId)
        {
            EdgeCreationTargetTag = null;
        }
        if (EdgeCreationAttachTag?.Id == tagId)
        {
            EdgeCreationAttachTag = null;
            EdgeCreationAttachAsset = null;
            EdgeCreationAvailableAssets.Clear();
        }
    }

    /// <summary>
    ///     指定したアイテムノードを画面表示から消す（非表示にする）。
    /// </summary>
    /// <param name="itemId">非表示にするアイテムID。</param>
    public void HideItem(int itemId)
    {
        _ = HiddenItemIds.Add(itemId);
    }

    /// <summary>
    ///     非表示にされたすべてのタグおよびアイテムノードを再表示する。
    /// </summary>
    public void RestoreHiddenNodes()
    {
        HiddenTagIds.Clear();
        HiddenItemIds.Clear();
    }

    public void HandleTagSelection(TagEntity tag)
    {
        ArgumentNullException.ThrowIfNull(tag);

        _ = HiddenTagIds.Remove(tag.Id);
        _ = PinnedTagIds.Add(tag.Id);
        if (FocusedTag == null)
        {
            FocusedTag = tag;
        }
        else if (FocusedTag.Id == tag.Id)
        {
            // 始点と同じタグの場合は何もしない
            return;
        }
        else if (SecondFocusedTag?.Id == tag.Id)
        {
            // 終点と同じタグの場合も何もしない
            return;
        }
        else
        {
            // 異なるタグの場合は終点（2つ目）に指定
            SecondFocusedTag = tag;
        }
    }

    public void OnTagFocusedFromSearch(TagEntity? tag)
    {
        if (tag == null)
        {
            ClearFirstTag();
            return;
        }

        _ = HiddenTagIds.Remove(tag.Id);
        _ = PinnedTagIds.Add(tag.Id);
        if (FocusedTag == null)
        {
            FocusedTag = tag;
        }
        else if (FocusedTag.Id == tag.Id)
        {
            // 変更なし
        }
        else if (SecondFocusedTag == null)
        {
            SecondFocusedTag = tag;
        }
        else
        {
            FocusedTag = tag;
        }
    }

    public void OnSecondTagFocusedFromSearch(TagEntity? tag)
    {
        if (tag != null)
        {
            _ = HiddenTagIds.Remove(tag.Id);
            _ = PinnedTagIds.Add(tag.Id);
        }
        SecondFocusedTag = tag;
    }

    public int PinChildTags(int parentId)
    {
        List<TagEntity> children = Tags.Where(t => t.ParentTagId == parentId).ToList();
        int newlyAdded = 0;
        foreach (TagEntity child in children)
        {
            _ = HiddenTagIds.Remove(child.Id);
            if (PinnedTagIds.Add(child.Id))
            {
                newlyAdded++;
            }
        }
        return newlyAdded;
    }

    public void SelectChildAsSource(TagEntity child)
    {
        EdgeCreationSourceTag = child;
        IsSelectingTargetInEdgeMode = true;
    }

    public void SelectChildAsTarget(TagEntity child)
    {
        EdgeCreationTargetTag = child;
    }

    public void OnEdgeCreationSourceChanged(TagEntity? tag)
    {
        EdgeCreationSourceTag = tag;
        if (tag != null)
        {
            IsSelectingTargetInEdgeMode = true;
        }
    }

    public void OnEdgeCreationTargetChanged(TagEntity? tag)
    {
        EdgeCreationTargetTag = tag;
    }

    public async Task<Result<(TagEdge Edge, TagEdgeTagAttachment? Attachment)>> CreateEdgeInModeAsync()
    {
        if (!CanCreateEdgeInMode)
        {
            return new Failure("エッジ作成条件を満たしていません。");
        }

        TagEntity sourceTag = EdgeCreationSourceTag!;
        TagEntity targetTag = EdgeCreationTargetTag!;

        Result<TagEdge> createResult = await _dataProvider.CreateEdgeAsync(sourceTag.Id, targetTag.Id, CurrentUserId);
        switch (createResult)
        {
            case Failure f:
                return f;
            case Success<TagEdge> s:
                TagEdge createdEdge = s.Value;
                TagEdgeTagAttachment? createdAttachment = null;

                if (EdgeCreationAttachTag != null && EdgeCreationAttachAsset != null)
                {
                    Result<TagEdgeTagAttachment> attachResult = await _dataProvider.AttachTagToEdgeAsync(
                        createdEdge.Id, EdgeCreationAttachTag.Id, EdgeCreationAttachAsset.Id, CurrentUserId, EdgeCreationWeight);

                    switch (attachResult)
                    {
                        case Success<TagEdgeTagAttachment> attSuccess:
                            createdAttachment = attSuccess.Value;
                            break;
                        case Failure:
                            // タグ紐付けが失敗してもエッジ作成自体は成功しているため続行
                            break;
                        default:
                            break;
                    }
                }

                TagEntity? savedFocus = FocusedTag;
                TagEntity? savedSecondFocus = SecondFocusedTag;

                await ReloadDiagramAsync(LoadedContextItemId, preserveExtraVisibleTags: true);

                FocusedTag = savedFocus;
                SecondFocusedTag = savedSecondFocus;
                SelectedEdge = Edges.FirstOrDefault(e => e.Id == createdEdge.Id);

                EdgeCreationSourceTag = null;
                EdgeCreationTargetTag = null;
                IsSelectingTargetInEdgeMode = false;

                if (EdgeCreationAttachTag != null)
                {
                    await SetEdgeCreationAttachTagAsync(EdgeCreationAttachTag);
                }

                return new Success<(TagEdge, TagEdgeTagAttachment?)>((createdEdge, createdAttachment));
            default:
                return new Failure("予期しないエラーが発生しました。");
        }
    }

    public TagEdge? GetExistingEdgeBetween(int id1, int id2) =>
        Edges.FirstOrDefault(e =>
            (e.SourceTagId == id1 && e.TargetTagId == id2) ||
            (e.SourceTagId == id2 && e.TargetTagId == id1));

    public int GetOutgoingEdgesCount(int tagId) => Edges.Count(e => e.SourceTagId == tagId);
    public int GetIncomingEdgesCount(int tagId) => Edges.Count(e => e.TargetTagId == tagId);
    public int GetChildTagsCount(int tagId) => Tags.Count(t => t.ParentTagId == tagId);
    public IReadOnlyList<TagEntity> GetChildTags(int parentId) => Tags.Where(t => t.ParentTagId == parentId).ToList();
}