using Moq;

using SRNSMudApp.Components.Tag;
using SRNSMudApp.Models;
using SRNSMudApp.Services;

using TagEntity = SRNSMudApp.Data.Tag;

namespace SRNSMudApp.Tests.Components.Tag;

/// <summary>
///     TagTreeViewModel の純粋ロジックに対する単体テスト。
///     bUnit・実DB（MSSQL Testcontainers）接続不要で高速に実行できる。
///     これらのテストは元々 TagTreeTests.cs で bUnit コンポーネントレンダリング＋
///     実MSSQLコンテナ経由で検証されていたが、対象ロジックが TagTreeViewModel に
///     切り出し済みの純粋関数であるため、ここに移行した。アサーション内容は元のテストと同一。
/// </summary>
public class TagTreeViewModelTests
{
    private const string CurrentUserId = "test-user-id";

    private static TagEntity NewTag(int id, string name, string ownerId, int? parentTagId = null, bool isSystem = false) =>
        new()
        {
            Id = id,
            Name = name,
            OwnerId = ownerId,
            ParentTagId = parentTagId,
            IsSystem = isSystem
        };

    // ============================================================
    // ツリー構造・エッジケース
    // (元 TagTreeTests.cs より移行, アサーション内容は変更していない)
    // ============================================================

    /// <summary>元テスト: JqTree_InitializesWithCorrectJson_WhenSingleRootNodeHasMultipleChildren</summary>
    [Fact]
    public void SerializeTreeData_WhenSingleRootNodeHasMultipleChildren_ProducesNestedJson()
    {
        List<TagEntity> tags =
        [
            NewTag(1, "Root", CurrentUserId),
            NewTag(2, "Child1", CurrentUserId, parentTagId: 1),
            NewTag(3, "Child2", CurrentUserId, parentTagId: 1)
        ];

        var json = TagTreeViewModel.SerializeTreeData(TagTreeViewModel.FilterTags(tags, null, CurrentUserId));

        Assert.NotNull(json);
        Assert.Contains("\"id\":1", json);
        Assert.Contains("\"children\":", json);
        Assert.Contains("\"id\":2", json);
        Assert.Contains("\"id\":3", json);
    }

    /// <summary>元テスト: JqTree_DisplaysCreatedTagTree_WhenSearchTextIsEmpty</summary>
    [Fact]
    public void SerializeTreeData_WhenSearchTextIsEmpty_IncludesOwnTreeAndOtherUserTag()
    {
        List<TagEntity> tags =
        [
            NewTag(1, "MyRoot", CurrentUserId),
            NewTag(2, "MyChild", CurrentUserId, parentTagId: 1),
            NewTag(3, "OtherRoot", "other-user-id")
        ];

        var json = TagTreeViewModel.SerializeTreeData(TagTreeViewModel.FilterTags(tags, null, CurrentUserId));

        Assert.NotNull(json);
        Assert.Contains("\"id\":1", json);
        Assert.Contains("\"name\":\"MyRoot\"", json);
        Assert.Contains("\"children\":", json);
        Assert.Contains("\"id\":2", json);
        Assert.Contains("\"name\":\"MyChild\"", json);
        // 2000件制限未満のため他ユーザーのタグも含まれる
        Assert.Contains("\"id\":3", json);
        Assert.Contains("\"name\":\"OtherRoot\"", json);
    }

    /// <summary>元テスト: JqTree_DoesNotCrash_WhenCircularReferenceExists</summary>
    [Fact]
    public void SerializeTreeData_WhenCircularReferenceExists_DoesNotThrow()
    {
        List<TagEntity> tags =
        [
            NewTag(1, "Tag1", CurrentUserId, parentTagId: 3),
            NewTag(2, "Tag2", CurrentUserId, parentTagId: 1),
            NewTag(3, "Tag3", CurrentUserId, parentTagId: 2)
        ];

        // LoadTagsAsync (TagTreeDataProvider) が行う循環参照の解除を先に適用する
        _ = TagTreeViewModel.DetectAndBreakCycles(tags);

        var json = TagTreeViewModel.SerializeTreeData(TagTreeViewModel.FilterTags(tags, null, CurrentUserId));

        Assert.NotNull(json);
    }

    /// <summary>元テスト: JqTree_DisplaysUpTo2000Tags_WhenDatabaseHasManyTags</summary>
    [Fact]
    public void FilterTags_WhenMoreThan2000Tags_TakesExactly2000()
    {
        List<TagEntity> tags = [];
        for (var i = 1; i <= 2500; i++)
        {
            tags.Add(NewTag(i, $"Tag {i}", CurrentUserId));
        }

        var json = TagTreeViewModel.SerializeTreeData(TagTreeViewModel.FilterTags(tags, null, CurrentUserId));

        Assert.NotNull(json);
        var idCount = json.Split("\"id\":").Length - 1;
        Assert.True(idCount is >= 2000 and <= 2000, $"Expected exactly 2000 tags, but got {idCount}");
    }

    /// <summary>元テスト: JqTree_DisplaysTag_WhenParentIsNonExistent</summary>
    [Fact]
    public void SerializeTreeData_WhenParentIsNotInFilteredList_TreatsTagAsRoot()
    {
        // システムタグの親は LoadTagsAsync 時点で除外されるため、
        // フィルタ後リストに存在しない ParentTagId を持つ状態を直接再現する
        List<TagEntity> tags = [NewTag(1, "Orphan", CurrentUserId, parentTagId: 999)];

        var json = TagTreeViewModel.SerializeTreeData(TagTreeViewModel.FilterTags(tags, null, CurrentUserId));

        Assert.Contains("\"id\":1", json);
    }

    /// <summary>元テスト: JqTree_DisplaysTag_WhenSelfReferencing</summary>
    [Fact]
    public void SerializeTreeData_WhenSelfReferencing_StillDisplaysTag()
    {
        List<TagEntity> tags = [NewTag(1, "SelfRef", CurrentUserId, parentTagId: 1)];

        _ = TagTreeViewModel.DetectAndBreakCycles(tags);

        var json = TagTreeViewModel.SerializeTreeData(TagTreeViewModel.FilterTags(tags, null, CurrentUserId));

        // ツリー構築ロジックが自己参照タグをスキップするとここで失敗する
        Assert.Contains("\"id\":1", json);
    }

    /// <summary>元テスト: JqTree_DisplaysTags_WhenDeeplyNested</summary>
    [Fact]
    public void SerializeTreeData_WhenDeeplyNested_DoesNotThrowJsonException()
    {
        List<TagEntity> tags = [];
        int? parentId = null;
        var deepestId = 0;
        // JsonSerializer の既定 MaxDepth は 64。TagTreeViewModel は 1024 を使用しているため 130 段でも失敗しないことを確認する
        for (var i = 1; i <= 130; i++)
        {
            tags.Add(NewTag(i, $"Deep{i}", CurrentUserId, parentTagId: parentId));
            parentId = i;
            deepestId = i;
        }

        var json = TagTreeViewModel.SerializeTreeData(TagTreeViewModel.FilterTags(tags, null, CurrentUserId));

        Assert.Contains($"\"id\":{deepestId}", json);
    }

    /// <summary>元テスト: JqTree_DisplaysCorrectly_WhenSingleRootNodeHasMultipleChildren (E2Eからの回帰テスト)</summary>
    [Fact]
    public void SerializeTreeData_WhenSingleRootHasThreeChildren_PreservesNestedStructure()
    {
        List<TagEntity> tags =
        [
            NewTag(1, "BugRoot", CurrentUserId),
            NewTag(2, "Child1", CurrentUserId, parentTagId: 1),
            NewTag(3, "Child2", CurrentUserId, parentTagId: 1),
            NewTag(4, "Child3", CurrentUserId, parentTagId: 1)
        ];

        var json = TagTreeViewModel.SerializeTreeData(TagTreeViewModel.FilterTags(tags, null, CurrentUserId));

        Assert.NotNull(json);
        Assert.Contains("\"name\":\"BugRoot\"", json);
        Assert.Contains("\"id\":1", json);
        Assert.Contains("\"id\":2", json);
        Assert.Contains("\"id\":3", json);
        Assert.Contains("\"id\":4", json);
        Assert.Contains("\"children\":", json);
    }

    /// <summary>元テスト: JqTree_DisplaysTags_WhenSearchFieldIsEmpty (15件)</summary>
    [Fact]
    public void FilterTags_WhenSearchTextIsEmptyAndFifteenTags_DisplaysAllFifteen()
    {
        List<TagEntity> tags = [];
        for (var i = 0; i < 15; i++)
        {
            tags.Add(NewTag(i + 1, $"EmptySearchTag_{i}", CurrentUserId));
        }

        var json = TagTreeViewModel.SerializeTreeData(TagTreeViewModel.FilterTags(tags, null, CurrentUserId));

        Assert.NotNull(json);
        Assert.NotEqual("[]", json);
        var idCount = json.Split("\"id\":").Length - 1;
        Assert.Equal(15, idCount);
        Assert.Contains("\"name\":\"EmptySearchTag_0\"", json);
        Assert.Contains("\"name\":\"EmptySearchTag_14\"", json);
    }

    // ============================================================
    // 検索フィールド空の場合にタグが表示されることを検証するテスト群
    // (元 TagTreeTests.cs より移行, アサーション内容は変更していない)
    // ============================================================

    /// <summary>元テスト: EmptySearch_DisplaysSingleFlatTag</summary>
    [Fact]
    public void FilterTags_WhenSingleFlatTag_DisplaysIt()
    {
        List<TagEntity> tags = [NewTag(1, "SoloTag", CurrentUserId)];

        var json = TagTreeViewModel.SerializeTreeData(TagTreeViewModel.FilterTags(tags, null, CurrentUserId));

        Assert.NotNull(json);
        Assert.NotEqual("[]", json);
        Assert.Contains("\"id\":1", json);
        Assert.Contains("\"name\":\"SoloTag\"", json);
    }

    /// <summary>元テスト: EmptySearch_DisplaysMultipleFlatTags</summary>
    [Fact]
    public void FilterTags_WhenMultipleFlatTags_DisplaysAll()
    {
        List<TagEntity> tags =
        [
            NewTag(1, "FlatA", CurrentUserId),
            NewTag(2, "FlatB", CurrentUserId),
            NewTag(3, "FlatC", CurrentUserId)
        ];

        var json = TagTreeViewModel.SerializeTreeData(TagTreeViewModel.FilterTags(tags, null, CurrentUserId));

        Assert.NotNull(json);
        Assert.NotEqual("[]", json);
        Assert.Contains("\"id\":1", json);
        Assert.Contains("\"id\":2", json);
        Assert.Contains("\"id\":3", json);
    }

    /// <summary>元テスト: EmptySearch_DisplaysMixedFlatAndTreeTags</summary>
    [Fact]
    public void FilterTags_WhenFlatAndTreeTagsMixed_DisplaysBoth()
    {
        List<TagEntity> tags =
        [
            NewTag(1, "FlatOnly", CurrentUserId),
            NewTag(2, "TreeRoot", CurrentUserId),
            NewTag(3, "TreeChild", CurrentUserId, parentTagId: 2)
        ];

        var json = TagTreeViewModel.SerializeTreeData(TagTreeViewModel.FilterTags(tags, null, CurrentUserId));

        Assert.NotNull(json);
        Assert.NotEqual("[]", json);
        Assert.Contains("\"id\":1", json);
        Assert.Contains("\"id\":2", json);
        Assert.Contains("\"id\":3", json);
    }

    /// <summary>元テスト: EmptySearch_DisplaysOtherUserTagsWhenNoOwnTags</summary>
    [Fact]
    public void FilterTags_WhenOnlyOtherUserTagsExist_StillDisplaysThem()
    {
        List<TagEntity> tags =
        [
            NewTag(1, "OtherUserTag1", "other-user"),
            NewTag(2, "OtherUserTag2", "other-user")
        ];

        var json = TagTreeViewModel.SerializeTreeData(TagTreeViewModel.FilterTags(tags, null, CurrentUserId));

        Assert.NotNull(json);
        Assert.NotEqual("[]", json);
        Assert.Contains("\"id\":1", json);
        Assert.Contains("\"id\":2", json);
    }

    /// <summary>元テスト: EmptySearch_DisplaysBothOwnAndOtherUserTags</summary>
    [Fact]
    public void FilterTags_WhenOwnAndOtherUserTagsMixed_DisplaysBoth()
    {
        List<TagEntity> tags =
        [
            NewTag(1, "MyTag", CurrentUserId),
            NewTag(2, "TheirTag", "someone-else")
        ];

        var json = TagTreeViewModel.SerializeTreeData(TagTreeViewModel.FilterTags(tags, null, CurrentUserId));

        Assert.NotNull(json);
        Assert.NotEqual("[]", json);
        Assert.Contains("\"id\":1", json);
        Assert.Contains("\"id\":2", json);
    }

    /// <summary>元テスト: EmptySearch_DisplaysSingleRootNoChildren</summary>
    [Fact]
    public void FilterTags_WhenSingleRootWithNoChildren_DisplaysIt()
    {
        List<TagEntity> tags = [NewTag(1, "LonelyRoot", CurrentUserId)];

        var json = TagTreeViewModel.SerializeTreeData(TagTreeViewModel.FilterTags(tags, null, CurrentUserId));

        Assert.NotNull(json);
        Assert.NotEqual("[]", json);
        Assert.Contains("\"name\":\"LonelyRoot\"", json);
    }

    /// <summary>元テスト: EmptySearch_DisplaysMultipleRootsWithChildren</summary>
    [Fact]
    public void FilterTags_WhenMultipleRootsEachHaveChildren_DisplaysAll()
    {
        List<TagEntity> tags =
        [
            NewTag(1, "RootA", CurrentUserId),
            NewTag(2, "ChildA1", CurrentUserId, parentTagId: 1),
            NewTag(3, "ChildA2", CurrentUserId, parentTagId: 1),
            NewTag(4, "RootB", CurrentUserId),
            NewTag(5, "ChildB1", CurrentUserId, parentTagId: 4)
        ];

        var json = TagTreeViewModel.SerializeTreeData(TagTreeViewModel.FilterTags(tags, null, CurrentUserId));

        Assert.NotNull(json);
        Assert.NotEqual("[]", json);
        Assert.Contains("\"id\":1", json);
        Assert.Contains("\"id\":2", json);
        Assert.Contains("\"id\":3", json);
        Assert.Contains("\"id\":4", json);
        Assert.Contains("\"id\":5", json);
    }

    /// <summary>元テスト: EmptySearch_JsonIsNotEmptyArray (10件)</summary>
    [Fact]
    public void FilterTags_WhenTenFlatTags_JsonContainsExactlyTenIds()
    {
        List<TagEntity> tags = [];
        for (var i = 0; i < 10; i++)
        {
            tags.Add(NewTag(i + 1, $"CountTag{i}", CurrentUserId));
        }

        var json = TagTreeViewModel.SerializeTreeData(TagTreeViewModel.FilterTags(tags, null, CurrentUserId));

        Assert.NotNull(json);
        Assert.NotEqual("[]", json);
        var idCount = json.Split("\"id\":").Length - 1;
        Assert.Equal(10, idCount);
    }

    /// <summary>元テスト: EmptySearch_DisplaysThreeLevelTree</summary>
    [Fact]
    public void FilterTags_WhenThreeLevelTree_PreservesNesting()
    {
        List<TagEntity> tags =
        [
            NewTag(1, "Grandparent", CurrentUserId),
            NewTag(2, "Parent", CurrentUserId, parentTagId: 1),
            NewTag(3, "Child", CurrentUserId, parentTagId: 2)
        ];

        var json = TagTreeViewModel.SerializeTreeData(TagTreeViewModel.FilterTags(tags, null, CurrentUserId));

        Assert.NotNull(json);
        Assert.NotEqual("[]", json);
        Assert.Contains("\"id\":1", json);
        Assert.Contains("\"id\":2", json);
        Assert.Contains("\"id\":3", json);
        Assert.Contains("\"children\":", json);
    }

    [Fact]
    public void SerializeTreeData_WithPendingMoves_AppendsPendingMoveNodeUnderParent()
    {
        List<TagEntity> tags =
        [
            NewTag(1, "Parent", "user1")
        ];

        List<PendingTagMoveDto> pendingMoves =
        [
            new(RequestId: 42, TagId: 99, TagName: "RequestedChild", NewParentTagId: 1, RequesterUserId: CurrentUserId, TagOwnerUserId: "user2")
        ];

        var json = TagTreeViewModel.SerializeTreeData(tags, pendingMoves, CurrentUserId);

        Assert.NotNull(json);
        Assert.Contains("\"id\":\"move-req-42\"", json);
        Assert.Contains("\"name\":\"RequestedChild\"", json);
        Assert.Contains("\"isPendingMove\":true", json);
        Assert.Contains("\"requestId\":42", json);
        Assert.Contains("\"targetTagId\":99", json);
        Assert.Contains("\"canCancel\":true", json);
        Assert.Contains("\"children\":", json);
    }

    [Fact]
    public void SerializeTreeData_WithPendingMoves_WhenDifferentUser_SetsCanCancelFalse()
    {
        List<TagEntity> tags =
        [
            NewTag(1, "Parent", "user1")
        ];

        List<PendingTagMoveDto> pendingMoves =
        [
            new(RequestId: 43, TagId: 99, TagName: "RequestedChild", NewParentTagId: 1, RequesterUserId: "other-user", TagOwnerUserId: "another-user")
        ];

        var json = TagTreeViewModel.SerializeTreeData(tags, pendingMoves, CurrentUserId);

        Assert.NotNull(json);
        Assert.Contains("\"id\":\"move-req-43\"", json);
        Assert.Contains("\"canCancel\":false", json);
    }

    [Fact]
    public void SerializeTreeData_WhenTagIsLocked_OutputsIsLockedTrue()
    {
        var lockedTag = NewTag(1, "LockedTag", CurrentUserId);
        lockedTag.IsLocked = true;

        var normalTag = NewTag(2, "NormalTag", CurrentUserId);

        var rootTag = NewTag(3, TagEntity.RootTagName, CurrentUserId);

        List<TagEntity> tags = [lockedTag, normalTag, rootTag];
        HashSet<int> lockedTagIds = [2];

        var json = TagTreeViewModel.SerializeTreeData(tags, null, CurrentUserId, lockedTagIds);

        Assert.NotNull(json);
        Assert.Contains("\"id\":1,\"name\":\"LockedTag\",\"isLocked\":true", json);
        Assert.Contains("\"id\":2,\"name\":\"NormalTag\",\"isLocked\":true", json);
        Assert.Contains("\"id\":3,", json);
        Assert.Contains("\"isLocked\":true", json);
    }

    [Fact]
    public void SerializeTreeData_WhenTagIsNotLocked_OutputsIsLockedFalse()
    {
        var normalTag = NewTag(1, "NormalTag", CurrentUserId);

        List<TagEntity> tags = [normalTag];

        var json = TagTreeViewModel.SerializeTreeData(tags, null, CurrentUserId);

        Assert.NotNull(json);
        Assert.Contains("\"id\":1,\"name\":\"NormalTag\",\"isLocked\":false", json);
    }

    [Fact]
    public async Task DeleteTagsAsync_WhenSelectedIdsEmpty_ReturnsEmptyResultImmediately()
    {
        var dataMock = new Mock<ITagTreeDataProvider>();
        var lockMock = new Mock<ITagLockService>();
        var sut = new TagTreeViewModel(dataMock.Object, lockMock.Object);
        sut.SetUser(CurrentUserId, false);

        var result = await sut.DeleteTagsAsync([]);

        Assert.False(result.HasDeleted);
        Assert.Equal(0, result.DeletedCount);
        dataMock.Verify(d => d.DeleteTagsAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<int>>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task DeleteTagsAsync_WhenCurrentUserIdEmpty_ReturnsEmptyResultImmediately()
    {
        var dataMock = new Mock<ITagTreeDataProvider>();
        var lockMock = new Mock<ITagLockService>();
        var sut = new TagTreeViewModel(dataMock.Object, lockMock.Object);
        sut.SetUser(null, false);

        var result = await sut.DeleteTagsAsync([1, 2]);

        Assert.False(result.HasDeleted);
        Assert.Equal(0, result.DeletedCount);
        dataMock.Verify(d => d.DeleteTagsAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<int>>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task DeleteTagsAsync_WhenValid_CallsDataProviderAndReloadsData()
    {
        var dataMock = new Mock<ITagTreeDataProvider>();
        var lockMock = new Mock<ITagLockService>();
        dataMock.Setup(d => d.DeleteTagsAsync(CurrentUserId, It.IsAny<IReadOnlyList<int>>(), false))
            .ReturnsAsync(new TagTreeDeleteResult(true, 2, [], []));
        dataMock.Setup(d => d.LoadTagsAsync()).ReturnsAsync([]);
        dataMock.Setup(d => d.LoadPendingTagMovesAsync()).ReturnsAsync([]);
        lockMock.Setup(l => l.GetAllTagsWithLockStatusAsync(default)).ReturnsAsync([]);

        var sut = new TagTreeViewModel(dataMock.Object, lockMock.Object);
        sut.SetUser(CurrentUserId, false);

        var result = await sut.DeleteTagsAsync([1, 2]);

        Assert.True(result.HasDeleted);
        Assert.Equal(2, result.DeletedCount);
        dataMock.Verify(d => d.DeleteTagsAsync(CurrentUserId, It.Is<IReadOnlyList<int>>(ids => ids.Count == 2), false), Times.Once);
        dataMock.Verify(d => d.LoadTagsAsync(), Times.Once);
    }

    [Fact]
    public void FilterTags_WhenTagOwnerIsBanned_ExcludesBannedOwnerTag()
    {
        var bannedUser = new SRNSMudApp.Data.ApplicationUser { Id = "banned-user", IsBanned = true };
        var activeUser = new SRNSMudApp.Data.ApplicationUser { Id = "active-user", IsBanned = false };

        var bannedTag = new TagEntity { Id = 10, Name = "BannedTag", OwnerId = bannedUser.Id, Owner = bannedUser };
        var activeTag = new TagEntity { Id = 20, Name = "ActiveTag", OwnerId = activeUser.Id, Owner = activeUser };

        List<TagEntity> tags = [bannedTag, activeTag];

        var filtered = TagTreeViewModel.FilterTags(tags, null, activeUser.Id).ToList();

        Assert.Contains(filtered, t => t.Id == activeTag.Id);
        Assert.DoesNotContain(filtered, t => t.Id == bannedTag.Id);
    }

    [Fact]
    public void FilterTags_WhenSearchTextMatchesNode_IncludesHitNodeAncestorsAndDescendants()
    {
        // 階層: Root(1) -> Parent(2) -> HitTag(3) -> Child(4) -> GrandChild(5)
        // 別系統: Unrelated(6) -> UnrelatedChild(7)
        List<TagEntity> tags =
        [
            NewTag(1, "Root", CurrentUserId),
            NewTag(2, "Parent", CurrentUserId, parentTagId: 1),
            NewTag(3, "HitTag", CurrentUserId, parentTagId: 2),
            NewTag(4, "Child", CurrentUserId, parentTagId: 3),
            NewTag(5, "GrandChild", CurrentUserId, parentTagId: 4),
            NewTag(6, "Unrelated", CurrentUserId),
            NewTag(7, "UnrelatedChild", CurrentUserId, parentTagId: 6)
        ];

        var filtered = TagTreeViewModel.FilterTags(tags, "HitTag", CurrentUserId).ToList();

        // 祖先 (1, 2)、ヒットノード自身 (3)、サブノード（子・孫 4, 5）が含まれること
        Assert.Contains(filtered, t => t.Id == 1);
        Assert.Contains(filtered, t => t.Id == 2);
        Assert.Contains(filtered, t => t.Id == 3);
        Assert.Contains(filtered, t => t.Id == 4);
        Assert.Contains(filtered, t => t.Id == 5);

        // 無関係なノードは含まれないこと
        Assert.DoesNotContain(filtered, t => t.Id == 6);
        Assert.DoesNotContain(filtered, t => t.Id == 7);
    }

    [Fact]
    public void GetMatchingTagIds_WhenSearchTextMatches_ReturnsOnlyDirectMatches()
    {
        List<TagEntity> tags =
        [
            NewTag(1, "Root", CurrentUserId),
            NewTag(2, "HitTag", CurrentUserId, parentTagId: 1),
            NewTag(3, "ChildOfHit", CurrentUserId, parentTagId: 2),
            NewTag(4, "AnotherHitTag", CurrentUserId)
        ];

        var matchingIds = TagTreeViewModel.GetMatchingTagIds(tags, "HitTag");

        Assert.Equal(2, matchingIds.Count);
        Assert.Contains(2, matchingIds);
        Assert.Contains(4, matchingIds);
        Assert.DoesNotContain(1, matchingIds);
        Assert.DoesNotContain(3, matchingIds);
    }

    [Fact]
    public void SerializeTreeData_WhenHighlightedTagIdsProvided_OutputsIsHighlightedTrueForMatchedTagsOnly()
    {
        List<TagEntity> tags =
        [
            NewTag(1, "Parent", CurrentUserId),
            NewTag(2, "HitTag", CurrentUserId, parentTagId: 1),
            NewTag(3, "Child", CurrentUserId, parentTagId: 2)
        ];

        HashSet<int> highlightedIds = [2];

        var json = TagTreeViewModel.SerializeTreeData(tags, null, CurrentUserId, null, highlightedIds);

        Assert.NotNull(json);
        Assert.Contains("\"id\":1,\"name\":\"Parent\",\"isLocked\":false,\"isHighlighted\":false", json);
        Assert.Contains("\"id\":2,\"name\":\"HitTag\",\"isLocked\":false,\"isHighlighted\":true", json);
        Assert.Contains("\"id\":3,\"name\":\"Child\",\"isLocked\":false,\"isHighlighted\":false", json);
    }
}