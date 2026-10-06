using Moq;

using SRNSMudApp.Components.Pages;
using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;

using TagEntity = SRNSMudApp.Data.Tag;

namespace SRNSMudApp.Tests.Components.Pages;

/// <summary>
///     TagDiagramPageViewModel の単体テスト。
///     bUnit や Blazor.Diagrams を用いずに、タグ・エッジの取得、フィルタリング計算、
///     フォーカス管理、エッジ作成モードのロジックを検証する。
/// </summary>
public sealed class TagDiagramPageViewModelTests
{
    private const string CurrentUserId = "user-test-1";
    private readonly Mock<ITagDiagramDataProvider> _dataProviderMock = new();
    private readonly TagDiagramPageViewModel _sut;

    public TagDiagramPageViewModelTests()
    {
        _sut = new TagDiagramPageViewModel(_dataProviderMock.Object)
        {
            CurrentUserId = CurrentUserId
        };
    }

    private static TagEntity CreateTag(int id, string name = "Tag", int? parentId = null) =>
        new() { Id = id, Name = name, OwnerId = CurrentUserId, ParentTagId = parentId };

    private static TagEdge CreateEdge(int id, int sourceId, int targetId) =>
        new() { Id = id, SourceTagId = sourceId, TargetTagId = targetId, OwnerId = CurrentUserId };

    private static SRNSMudApp.Data.Item CreateItem(int id, string content = "Item") =>
        new() { Id = id, Content = content, OwnerId = CurrentUserId };

    private static RightAsset CreateAsset(int id, int tagId) =>
        new() { Id = id, TargetTagId = tagId, OwnerId = CurrentUserId };

    private static TagEdgeTagAttachment CreateAttachment(int id, int edgeId, int tagId) =>
        new() { Id = id, TagEdgeId = edgeId, TagId = tagId, OwnerId = CurrentUserId };

    [Fact]
    public async Task ReloadDiagramAsync_PopulatesTagsEdgesAndContext_WhenQueryItemIdProvided()
    {
        // Arrange
        TagEntity tag1 = CreateTag(1, "Tag 1");
        TagEntity tag2 = CreateTag(2, "Tag 2");
        TagEdge edge = CreateEdge(10, 1, 2);
        SRNSMudApp.Data.Item item = CreateItem(100, "Item 100");

        _dataProviderMock.Setup(p => p.LoadAllTagsAsync()).ReturnsAsync([tag1, tag2]);
        _dataProviderMock.Setup(p => p.LoadAllEdgesAsync()).ReturnsAsync([edge]);
        _dataProviderMock.Setup(p => p.GetContextTagIdsForItemAsync(100)).ReturnsAsync([1]);
        _dataProviderMock.Setup(p => p.GetContextItemsAsync(100)).ReturnsAsync([item]);

        // Act
        await _sut.ReloadDiagramAsync(100);

        // Assert
        Assert.False(_sut.IsLoading);
        Assert.Equal(2, _sut.Tags.Count);
        Assert.Single(_sut.Edges);
        Assert.Single(_sut.ContextItems);
        Assert.Contains(1, _sut.PinnedTagIds);
        Assert.Equal(100, _sut.LoadedContextItemId);
    }

    [Fact]
    public async Task ReloadDiagramAsync_PreservesExtraVisibleTags_WhenPreserveFlagIsTrue()
    {
        // Arrange
        _ = _sut.PinnedTagIds.Add(99);
        _dataProviderMock.Setup(p => p.LoadAllTagsAsync()).ReturnsAsync([]);
        _dataProviderMock.Setup(p => p.LoadAllEdgesAsync()).ReturnsAsync([]);

        // Act
        await _sut.ReloadDiagramAsync(null, preserveExtraVisibleTags: true);

        // Assert
        Assert.Contains(99, _sut.PinnedTagIds);
    }

    [Fact]
    public async Task UpdateContextItemAsync_WhenSameItemId_ReturnsFalseWithoutCallingDataProvider()
    {
        // Arrange
        _sut.LoadedContextItemId = 10;

        // Act
        bool changed = await _sut.UpdateContextItemAsync(10);

        // Assert
        Assert.False(changed);
        _dataProviderMock.Verify(p => p.GetContextTagIdsForItemAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task UpdateContextItemAsync_WhenDifferentItemId_UpdatesContextAndReturnsTrue()
    {
        // Arrange
        _sut.LoadedContextItemId = 10;
        _dataProviderMock.Setup(p => p.GetContextTagIdsForItemAsync(20)).ReturnsAsync([5]);
        _dataProviderMock.Setup(p => p.GetContextItemsAsync(20)).ReturnsAsync([CreateItem(20)]);

        // Act
        bool changed = await _sut.UpdateContextItemAsync(20);

        // Assert
        Assert.True(changed);
        Assert.Equal(20, _sut.LoadedContextItemId);
        Assert.Contains(5, _sut.PinnedTagIds);
        Assert.Single(_sut.ContextItems);
    }

    [Fact]
    public async Task UpdateContextItemAsync_WhenNull_ClearsContextItemsAndReturnsTrue()
    {
        // Arrange
        _sut.LoadedContextItemId = 10;

        // Act
        bool changed = await _sut.UpdateContextItemAsync(null);

        // Assert
        Assert.True(changed);
        Assert.Null(_sut.LoadedContextItemId);
        Assert.Empty(_sut.ContextItems);
    }

    [Fact]
    public void ApplyQueryParameters_UpdatesTagsAndEdge_AndReturnsTrueWhenChanged()
    {
        // Arrange
        TagEntity tag1 = CreateTag(1, "Tag 1");
        TagEntity tag2 = CreateTag(2, "Tag 2");
        TagEdge edge = CreateEdge(5, 1, 2);

        _sut.Tags.AddRange([tag1, tag2]);
        _sut.Edges.Add(edge);

        // Act
        bool changed = _sut.ApplyQueryParameters(1, 2, 5);

        // Assert
        Assert.True(changed);
        Assert.Equal(tag1, _sut.FocusedTag);
        Assert.Equal(tag2, _sut.SecondFocusedTag);
        Assert.Equal(edge, _sut.SelectedEdge);

        // Re-apply same -> should return false
        bool secondChanged = _sut.ApplyQueryParameters(1, 2, 5);
        Assert.False(secondChanged);
    }

    [Fact]
    public void GetTagsToDisplay_WhenContextOnlyWithQueryItem_ReturnsOnlyContextAndFocusedTags()
    {
        // Arrange
        TagEntity tag1 = CreateTag(1, "Tag 1");
        TagEntity tag2 = CreateTag(2, "Tag 2");
        TagEntity tag3 = CreateTag(3, "Tag 3");
        _sut.Tags.AddRange([tag1, tag2, tag3]);
        _ = _sut.PinnedTagIds.Add(1);
        _sut.ContextOnly = true;

        // Act
        IReadOnlyList<TagEntity> result = _sut.GetTagsToDisplay(queryItemId: 100);

        // Assert
        Assert.Single(result);
        Assert.Equal(1, result[0].Id);
        Assert.Equal(1, _sut.DisplayedTagsCount);
    }

    [Fact]
    public void GetTagsToDisplay_WhenNeighborhoodOnlyWithFocusedTag_ReturnsConnectedNeighbors()
    {
        // Arrange
        TagEntity tag1 = CreateTag(1, "Tag 1");
        TagEntity tag2 = CreateTag(2, "Tag 2");
        TagEntity tag3 = CreateTag(3, "Tag 3");
        _sut.Tags.AddRange([tag1, tag2, tag3]);
        _sut.Edges.Add(CreateEdge(1, 1, 2));
        _sut.FocusedTag = tag1;
        _sut.NeighborhoodOnly = true;

        // Act
        IReadOnlyList<TagEntity> result = _sut.GetTagsToDisplay(queryItemId: null);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains(result, t => t.Id == 1);
        Assert.Contains(result, t => t.Id == 2);
        Assert.DoesNotContain(result, t => t.Id == 3);
    }

    [Fact]
    public void GetTagsToDisplay_WhenOnlyConnectedTags_ReturnsOnlyConnectedTagsAndPinned()
    {
        // Arrange
        TagEntity tag1 = CreateTag(1, "Tag 1");
        TagEntity tag2 = CreateTag(2, "Tag 2");
        TagEntity tag3 = CreateTag(3, "Tag 3");
        _sut.Tags.AddRange([tag1, tag2, tag3]);
        _sut.Edges.Add(CreateEdge(1, 1, 2));
        _sut.OnlyConnectedTags = true;
        _sut.NeighborhoodOnly = false;
        _sut.ContextOnly = false;

        // Act
        IReadOnlyList<TagEntity> result = _sut.GetTagsToDisplay(queryItemId: null);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains(result, t => t.Id == 1);
        Assert.Contains(result, t => t.Id == 2);
        Assert.DoesNotContain(result, t => t.Id == 3);
    }

    [Fact]
    public void GetTagsToDisplay_WhenNoFilter_ReturnsAllTags()
    {
        // Arrange
        TagEntity tag1 = CreateTag(1, "Tag 1");
        TagEntity tag2 = CreateTag(2, "Tag 2");
        _sut.Tags.AddRange([tag1, tag2]);
        _sut.OnlyConnectedTags = false;
        _sut.NeighborhoodOnly = false;
        _sut.ContextOnly = false;

        // Act
        IReadOnlyList<TagEntity> result = _sut.GetTagsToDisplay(queryItemId: null);

        // Assert
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task CreateEdgeAsync_DelegatesToDataProvider()
    {
        // Arrange
        TagEdge expected = CreateEdge(10, 1, 2);
        _dataProviderMock.Setup(p => p.CreateEdgeAsync(1, 2, CurrentUserId))
            .ReturnsAsync(new Success<TagEdge>(expected));

        // Act
        Result<TagEdge> result = await _sut.CreateEdgeAsync(1, 2);

        // Assert
        Assert.True(result is Success<TagEdge> s && s.Value.Id == 10);
    }

    [Fact]
    public async Task DeleteEdgeAsync_DelegatesToDataProvider()
    {
        // Arrange
        _dataProviderMock.Setup(p => p.DeleteEdgeAsync(10, CurrentUserId))
            .ReturnsAsync(new Success<bool>(true));

        // Act
        Result<bool> result = await _sut.DeleteEdgeAsync(10);

        // Assert
        Assert.True(result is Success<bool> s && s.Value);
    }

    [Fact]
    public async Task AttachTagToEdgeAsync_DelegatesToDataProvider()
    {
        // Arrange
        TagEdgeTagAttachment expected = CreateAttachment(5, 10, 1);
        _dataProviderMock.Setup(p => p.AttachTagToEdgeAsync(10, 1, 100, CurrentUserId, 2))
            .ReturnsAsync(new Success<TagEdgeTagAttachment>(expected));

        // Act
        Result<TagEdgeTagAttachment> result = await _sut.AttachTagToEdgeAsync(10, 1, 100, 2);

        // Assert
        Assert.True(result is Success<TagEdgeTagAttachment> s && s.Value.Id == 5);
    }

    [Fact]
    public async Task DetachTagFromEdgeAsync_DelegatesToDataProvider()
    {
        // Arrange
        _dataProviderMock.Setup(p => p.DetachTagFromEdgeAsync(5, CurrentUserId))
            .ReturnsAsync(new Success<bool>(true));

        // Act
        Result<bool> result = await _sut.DetachTagFromEdgeAsync(5);

        // Assert
        Assert.True(result is Success<bool> s && s.Value);
    }

    [Fact]
    public async Task SetEdgeCreationAttachTagAsync_LoadsAvailableAssets()
    {
        // Arrange
        TagEntity tag = CreateTag(1, "Tag 1");
        RightAsset asset = CreateAsset(50, 1);
        _dataProviderMock.Setup(p => p.GetAvailableRightAssetsAsync(CurrentUserId, 1))
            .ReturnsAsync([asset]);

        // Act
        await _sut.SetEdgeCreationAttachTagAsync(tag);

        // Assert
        Assert.Equal(tag, _sut.EdgeCreationAttachTag);
        Assert.Equal(asset, _sut.EdgeCreationAttachAsset);
        Assert.Single(_sut.EdgeCreationAvailableAssets);
        Assert.Equal(1, _sut.EdgeCreationWeight);

        // When tag is null
        await _sut.SetEdgeCreationAttachTagAsync(null);
        Assert.Null(_sut.EdgeCreationAttachTag);
        Assert.Null(_sut.EdgeCreationAttachAsset);
        Assert.Empty(_sut.EdgeCreationAvailableAssets);
    }

    [Fact]
    public async Task EnterEdgeCreationModeAsync_WhenFocusedTagHasChildren_PinsChildrenAndSetsAttachTag()
    {
        // Arrange
        TagEntity parentTag = CreateTag(1, "Parent");
        TagEntity childTag = CreateTag(2, "Child", parentId: 1);
        _sut.Tags.AddRange([parentTag, childTag]);
        _sut.FocusedTag = parentTag;

        _dataProviderMock.Setup(p => p.GetAvailableRightAssetsAsync(CurrentUserId, 1))
            .ReturnsAsync([]);

        // Act
        int newlyAdded = await _sut.EnterEdgeCreationModeAsync();

        // Assert
        Assert.True(_sut.IsEdgeCreationMode);
        Assert.Equal(1, newlyAdded);
        Assert.Contains(2, _sut.PinnedTagIds);
        Assert.Equal(parentTag, _sut.EdgeCreationAttachTag);
    }

    [Fact]
    public void ExitEdgeCreationMode_ResetsAllModeStates()
    {
        // Arrange
        _sut.StartEdgeCreationMode();
        _sut.EdgeCreationSourceTag = CreateTag(1);
        _sut.EdgeCreationTargetTag = CreateTag(2);

        // Act
        _sut.ExitEdgeCreationMode();

        // Assert
        Assert.False(_sut.IsEdgeCreationMode);
        Assert.Null(_sut.EdgeCreationSourceTag);
        Assert.Null(_sut.EdgeCreationTargetTag);
        Assert.Null(_sut.EdgeCreationAttachTag);
        Assert.Null(_sut.EdgeCreationAttachAsset);
        Assert.Empty(_sut.EdgeCreationAvailableAssets);
        Assert.False(_sut.IsSelectingTargetInEdgeMode);
    }

    [Fact]
    public async Task CreateEdgeInModeAsync_WhenCanCreateEdgeIsFalse_ReturnsFailure()
    {
        // Arrange - No tags set
        _sut.StartEdgeCreationMode();

        // Act
        Result<(TagEdge Edge, TagEdgeTagAttachment? Attachment)> result = await _sut.CreateEdgeInModeAsync();

        // Assert
        Assert.True(result is Failure);
    }

    [Fact]
    public async Task CreateEdgeInModeAsync_WhenSuccessfulWithAttachment_CreatesEdgeAndAttachment()
    {
        // Arrange
        TagEntity tag1 = CreateTag(1, "Tag 1");
        TagEntity tag2 = CreateTag(2, "Tag 2");
        TagEntity attachTag = CreateTag(3, "Attach Tag");
        RightAsset attachAsset = CreateAsset(90, 3);
        TagEdge createdEdge = CreateEdge(50, 1, 2);
        TagEdgeTagAttachment createdAttachment = CreateAttachment(60, 50, 3);

        _sut.Tags.AddRange([tag1, tag2, attachTag]);
        _sut.StartEdgeCreationMode();
        _sut.EdgeCreationSourceTag = tag1;
        _sut.EdgeCreationTargetTag = tag2;

        _dataProviderMock.Setup(p => p.GetAvailableRightAssetsAsync(CurrentUserId, 3))
            .ReturnsAsync([attachAsset]);

        await _sut.SetEdgeCreationAttachTagAsync(attachTag);
        _sut.EdgeCreationAttachAsset = attachAsset;

        _dataProviderMock.Setup(p => p.CreateEdgeAsync(1, 2, CurrentUserId))
            .ReturnsAsync(new Success<TagEdge>(createdEdge));
        _dataProviderMock.Setup(p => p.AttachTagToEdgeAsync(50, 3, 90, CurrentUserId, 1))
            .ReturnsAsync(new Success<TagEdgeTagAttachment>(createdAttachment));
        _dataProviderMock.Setup(p => p.LoadAllTagsAsync())
            .ReturnsAsync([tag1, tag2, attachTag]);
        _dataProviderMock.Setup(p => p.LoadAllEdgesAsync())
            .ReturnsAsync([createdEdge]);

        // Act
        Result<(TagEdge Edge, TagEdgeTagAttachment? Attachment)> result = await _sut.CreateEdgeInModeAsync();

        // Assert
        Assert.True(result is Success<(TagEdge Edge, TagEdgeTagAttachment? Attachment)> s
            && s.Value.Edge.Id == 50
            && s.Value.Attachment?.Id == 60);
        Assert.Equal(50, _sut.SelectedEdge?.Id);
        Assert.Null(_sut.EdgeCreationSourceTag);
        Assert.Null(_sut.EdgeCreationTargetTag);
    }

    [Fact]
    public void ClearFirstTag_ShiftsSecondFocusedTagToFirst()
    {
        // Arrange
        TagEntity tag1 = CreateTag(1, "Tag 1");
        TagEntity tag2 = CreateTag(2, "Tag 2");
        _sut.FocusedTag = tag1;
        _sut.SecondFocusedTag = tag2;

        // Act
        _sut.ClearFirstTag();

        // Assert
        Assert.Equal(tag2, _sut.FocusedTag);
        Assert.Null(_sut.SecondFocusedTag);

        // Clear again -> clears all focus
        _sut.ClearFirstTag();
        Assert.Null(_sut.FocusedTag);
    }

    [Fact]
    public void ClearSecondTag_And_ClearFocus()
    {
        // Arrange
        TagEntity tag1 = CreateTag(1, "Tag 1");
        TagEntity tag2 = CreateTag(2, "Tag 2");
        _sut.FocusedTag = tag1;
        _sut.SecondFocusedTag = tag2;
        _sut.NeighborhoodOnly = true;

        // Act
        _sut.ClearSecondTag();

        // Assert
        Assert.Null(_sut.SecondFocusedTag);
        Assert.Equal(tag1, _sut.FocusedTag);

        // Act
        _sut.ClearFocus();

        // Assert
        Assert.Null(_sut.FocusedTag);
        Assert.Null(_sut.SecondFocusedTag);
        Assert.False(_sut.NeighborhoodOnly);
    }

    [Fact]
    public void HandleTagSelection_SetsFirstOrSecondTag()
    {
        // Arrange
        TagEntity tag1 = CreateTag(1, "Tag 1");
        TagEntity tag2 = CreateTag(2, "Tag 2");

        // Act 1: Select first
        _sut.HandleTagSelection(tag1);
        Assert.Equal(tag1, _sut.FocusedTag);
        Assert.Null(_sut.SecondFocusedTag);
        Assert.Contains(1, _sut.PinnedTagIds);

        // Act 2: Select same -> no-op
        _sut.HandleTagSelection(tag1);
        Assert.Equal(tag1, _sut.FocusedTag);
        Assert.Null(_sut.SecondFocusedTag);

        // Act 3: Select second
        _sut.HandleTagSelection(tag2);
        Assert.Equal(tag1, _sut.FocusedTag);
        Assert.Equal(tag2, _sut.SecondFocusedTag);
        Assert.Contains(2, _sut.PinnedTagIds);
    }

    [Fact]
    public void OnTagFocusedFromSearch_SetsFocusedTag()
    {
        // Arrange
        TagEntity tag1 = CreateTag(1, "Tag 1");
        TagEntity tag2 = CreateTag(2, "Tag 2");

        // Act 1: Null clears first
        _sut.OnTagFocusedFromSearch(null);
        Assert.Null(_sut.FocusedTag);

        // Act 2: Set first
        _sut.OnTagFocusedFromSearch(tag1);
        Assert.Equal(tag1, _sut.FocusedTag);

        // Act 3: Set second
        _sut.OnTagFocusedFromSearch(tag2);
        Assert.Equal(tag1, _sut.FocusedTag);
        Assert.Equal(tag2, _sut.SecondFocusedTag);

        // Act 4: Set third when both are present -> replaces first
        TagEntity tag3 = CreateTag(3, "Tag 3");
        _sut.OnTagFocusedFromSearch(tag3);
        Assert.Equal(tag3, _sut.FocusedTag);
        Assert.Equal(tag2, _sut.SecondFocusedTag);
    }

    [Fact]
    public void SwapFocusedTags_And_SwapEdgeCreationTags()
    {
        // Arrange
        TagEntity tag1 = CreateTag(1);
        TagEntity tag2 = CreateTag(2);

        _sut.FocusedTag = tag1;
        _sut.SecondFocusedTag = tag2;
        _sut.SwapFocusedTags();
        Assert.Equal(tag2, _sut.FocusedTag);
        Assert.Equal(tag1, _sut.SecondFocusedTag);

        _sut.EdgeCreationSourceTag = tag1;
        _sut.EdgeCreationTargetTag = tag2;
        _sut.SwapEdgeCreationTags();
        Assert.Equal(tag2, _sut.EdgeCreationSourceTag);
        Assert.Equal(tag1, _sut.EdgeCreationTargetTag);
    }

    [Fact]
    public void EdgeCounts_And_ChildTags_CalculateCorrectly()
    {
        // Arrange
        TagEntity tag1 = CreateTag(1, "Tag 1", parentId: null);
        TagEntity tag2 = CreateTag(2, "Tag 2", parentId: 1);
        TagEntity tag3 = CreateTag(3, "Tag 3", parentId: 1);
        _sut.Tags.AddRange([tag1, tag2, tag3]);

        TagEdge edge1 = CreateEdge(1, 1, 2);
        TagEdge edge2 = CreateEdge(2, 2, 1);
        _sut.Edges.AddRange([edge1, edge2]);

        // Act & Assert
        Assert.Equal(1, _sut.GetOutgoingEdgesCount(1));
        Assert.Equal(1, _sut.GetIncomingEdgesCount(1));
        Assert.Equal(2, _sut.GetChildTagsCount(1));
        Assert.Equal(2, _sut.GetChildTags(1).Count);
        Assert.NotNull(_sut.GetExistingEdgeBetween(1, 2));
        Assert.Null(_sut.GetExistingEdgeBetween(1, 3));
    }

    [Fact]
    public void HideTag_AddsToHiddenTagIds_AndClearsFocusAndPinnedTags()
    {
        // Arrange
        TagEntity tag1 = CreateTag(1, "Tag 1");
        TagEntity tag2 = CreateTag(2, "Tag 2");
        _sut.Tags.AddRange([tag1, tag2]);
        _sut.FocusedTag = tag1;
        _sut.SecondFocusedTag = tag2;
        _ = _sut.PinnedTagIds.Add(tag1.Id);
        _sut.SelectedEdge = CreateEdge(10, 1, 2);

        // Act
        _sut.HideTag(1);

        // Assert
        Assert.Contains(1, _sut.HiddenTagIds);
        Assert.DoesNotContain(1, _sut.PinnedTagIds);
        Assert.Equal(tag2, _sut.FocusedTag);
        Assert.Null(_sut.SecondFocusedTag);
        Assert.Null(_sut.SelectedEdge);
    }

    [Fact]
    public void GetTagsToDisplay_ExcludesHiddenTags()
    {
        // Arrange
        TagEntity tag1 = CreateTag(1, "Tag 1");
        TagEntity tag2 = CreateTag(2, "Tag 2");
        TagEntity tag3 = CreateTag(3, "Tag 3");
        _sut.Tags.AddRange([tag1, tag2, tag3]);
        _sut.OnlyConnectedTags = false;

        // Act
        _sut.HideTag(2);
        IReadOnlyList<TagEntity> result = _sut.GetTagsToDisplay(queryItemId: null);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains(result, t => t.Id == 1);
        Assert.DoesNotContain(result, t => t.Id == 2);
        Assert.Contains(result, t => t.Id == 3);
        Assert.Equal(2, _sut.DisplayedTagsCount);
    }

    [Fact]
    public void GetTagsToDisplay_WhenTagHidden_ExcludesConnectedEdgesAndUpdatesDisplayedEdgesCount()
    {
        // Arrange
        TagEntity tag1 = CreateTag(1, "Tag 1");
        TagEntity tag2 = CreateTag(2, "Tag 2");
        TagEntity tag3 = CreateTag(3, "Tag 3");
        _sut.Tags.AddRange([tag1, tag2, tag3]);
        _sut.Edges.Add(CreateEdge(10, 1, 2));
        _sut.Edges.Add(CreateEdge(20, 1, 3));
        _sut.OnlyConnectedTags = true;

        // Act
        _sut.HideTag(2);
        IReadOnlyList<TagEntity> result = _sut.GetTagsToDisplay(queryItemId: null);

        // Assert: tag2 is hidden, edge (1->2) is excluded, only edge (1->3) remains active
        Assert.DoesNotContain(result, t => t.Id == 2);
        Assert.Contains(result, t => t.Id == 1);
        Assert.Contains(result, t => t.Id == 3);
        Assert.Equal(1, _sut.DisplayedEdgesCount);
    }

    [Fact]
    public void RestoreHiddenNodes_ClearsHiddenTagIdsAndHiddenItemIds()
    {
        // Arrange
        _sut.HideTag(1);
        _sut.HideItem(100);
        Assert.Single(_sut.HiddenTagIds);
        Assert.Single(_sut.HiddenItemIds);

        // Act
        _sut.RestoreHiddenNodes();

        // Assert
        Assert.Empty(_sut.HiddenTagIds);
        Assert.Empty(_sut.HiddenItemIds);
    }

    [Fact]
    public void OnTagFocusedFromSearch_RemovesTagFromHiddenTagIds()
    {
        // Arrange
        TagEntity tag1 = CreateTag(1, "Tag 1");
        _sut.Tags.Add(tag1);
        _sut.HideTag(1);
        Assert.Contains(1, _sut.HiddenTagIds);

        // Act
        _sut.OnTagFocusedFromSearch(tag1);

        // Assert
        Assert.DoesNotContain(1, _sut.HiddenTagIds);
        Assert.Contains(1, _sut.PinnedTagIds);
        Assert.Equal(tag1, _sut.FocusedTag);
    }

    [Fact]
    public void PinChildTags_RemovesChildrenFromHiddenTagIds()
    {
        // Arrange
        TagEntity parent = CreateTag(1, "Parent");
        TagEntity child = CreateTag(2, "Child", parentId: 1);
        _sut.Tags.AddRange([parent, child]);
        _sut.HideTag(2);
        Assert.Contains(2, _sut.HiddenTagIds);

        // Act
        int newlyAdded = _sut.PinChildTags(1);

        // Assert
        Assert.Equal(1, newlyAdded);
        Assert.DoesNotContain(2, _sut.HiddenTagIds);
        Assert.Contains(2, _sut.PinnedTagIds);
    }
}