#region

using SRNSMudApp.Components.Tag;

#endregion

namespace SRNSMudApp.Tests.Components.Tag;

using Tag = SRNSMudApp.Data.Tag;

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

    private static Tag NewTag(int id, string name, string ownerId, int? parentTagId = null, bool isSystem = false) =>
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
        List<Tag> tags =
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
        List<Tag> tags =
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
    }

    /// <summary>元テスト: JqTree_DoesNotCrash_WhenCircularReferenceExists</summary>
    [Fact]
    public void SerializeTreeData_WhenCircularReferenceExists_DoesNotThrow()
    {
        List<Tag> tags =
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
        List<Tag> tags = [];
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
        List<Tag> tags = [NewTag(1, "Orphan", CurrentUserId, parentTagId: 999)];

        var json = TagTreeViewModel.SerializeTreeData(TagTreeViewModel.FilterTags(tags, null, CurrentUserId));

        Assert.Contains("\"id\":1", json);
    }

    /// <summary>元テスト: JqTree_DisplaysTag_WhenSelfReferencing</summary>
    [Fact]
    public void SerializeTreeData_WhenSelfReferencing_StillDisplaysTag()
    {
        List<Tag> tags = [NewTag(1, "SelfRef", CurrentUserId, parentTagId: 1)];

        _ = TagTreeViewModel.DetectAndBreakCycles(tags);

        var json = TagTreeViewModel.SerializeTreeData(TagTreeViewModel.FilterTags(tags, null, CurrentUserId));

        // ツリー構築ロジックが自己参照タグをスキップするとここで失敗する
        Assert.Contains("\"id\":1", json);
    }

    /// <summary>元テスト: JqTree_DisplaysTags_WhenDeeplyNested</summary>
    [Fact]
    public void SerializeTreeData_WhenDeeplyNested_DoesNotThrowJsonException()
    {
        List<Tag> tags = [];
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
        List<Tag> tags =
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
        List<Tag> tags = [];
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
        List<Tag> tags = [NewTag(1, "SoloTag", CurrentUserId)];

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
        List<Tag> tags =
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
        List<Tag> tags =
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
        List<Tag> tags =
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
        List<Tag> tags =
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
        List<Tag> tags = [NewTag(1, "LonelyRoot", CurrentUserId)];

        var json = TagTreeViewModel.SerializeTreeData(TagTreeViewModel.FilterTags(tags, null, CurrentUserId));

        Assert.NotNull(json);
        Assert.NotEqual("[]", json);
        Assert.Contains("\"name\":\"LonelyRoot\"", json);
    }

    /// <summary>元テスト: EmptySearch_DisplaysMultipleRootsWithChildren</summary>
    [Fact]
    public void FilterTags_WhenMultipleRootsEachHaveChildren_DisplaysAll()
    {
        List<Tag> tags =
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
        List<Tag> tags = [];
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
        List<Tag> tags =
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
}