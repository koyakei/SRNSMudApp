using SRNSMudApp.Components.Tag;
using SRNSMudApp.Data;

using Xunit;

namespace SRNSMudApp.Tests.Components.Tag;

// 親名前空間の下にある namespace Tag より先に Data.Tag 型を解決させるため、
// エイリアスを名前空間の内側に置く
using Tag = SRNSMudApp.Data.Tag;

/// <summary>
///     TagTreePopoverViewModel の単体テスト。
///     自動展開 ID 計算とツリー行構築を bUnit なしで検証する。
/// </summary>
public class TagTreePopoverViewModelTests
{
    private static Tag CreateTag(int id, string name, int? parentId = null) =>
        new() { Id = id, Name = name, OwnerId = "user-1", ParentTagId = parentId };

    /// <summary>root(1) ─ child(2) ─ grandchild(3)、sibling(4) は child の兄弟。</summary>
    private static List<Tag> CreateSampleTags() =>
    [
        CreateTag(1, "root"),
        CreateTag(2, "child", 1),
        CreateTag(3, "grandchild", 2),
        CreateTag(4, "sibling", 1)
    ];

    [Fact]
    public void GetAutoExpandIds_IncludesTargetAndAllAncestors()
    {
        var tags = CreateSampleTags();
        var target = tags.Single(t => t.Id == 3);

        var ids = TagTreePopoverViewModel.GetAutoExpandIds(target, tags);

        Assert.Equal([3, 2, 1], ids);
    }

    [Fact]
    public void BuildTreeLines_StartsFromRoot_WithCurrentRoleOnTarget()
    {
        var tags = CreateSampleTags();
        var target = tags.Single(t => t.Id == 3);
        HashSet<int> expanded = [1, 2]; // root と child を展開済みにする

        var lines = TagTreePopoverViewModel.BuildTreeLines(target, tags, expanded, enableExpand: true);

        Assert.Equal([1, 2, 3, 4], lines.Select(l => l.TagId));
        Assert.Equal("root", lines[0].Name);
        Assert.True(lines[0].IsParent);
        Assert.Equal("親", lines[0].Role);
        Assert.True(lines[2].IsCurrent);
        Assert.Equal("自身", lines[2].Role);
    }

    [Fact]
    public void BuildTreeLines_WhenExpandDisabled_ShowsAllTags()
    {
        var tags = CreateSampleTags();
        var target = tags.Single(t => t.Id == 3);

        var lines = TagTreePopoverViewModel.BuildTreeLines(target, tags, [], enableExpand: false);

        // 展開操作無効時は全ノードが常に表示される
        Assert.Equal([1, 2, 3, 4], lines.Select(l => l.TagId));
        Assert.All(lines, l => Assert.True(l.IsExpanded));
    }

    [Fact]
    public void BuildTreeLines_WhenNodeCollapsed_ChildrenAreHidden()
    {
        var tags = CreateSampleTags();
        var target = tags.Single(t => t.Id == 1);
        HashSet<int> expanded = []; // 何も展開していない

        var lines = TagTreePopoverViewModel.BuildTreeLines(target, tags, expanded, enableExpand: true);

        Assert.Equal([1], lines.Select(l => l.TagId));
        Assert.Equal("▶", lines[0].Icon);
    }

    [Fact]
    public void BuildTreeLines_WhenNodeExpanded_ChildrenAreVisible()
    {
        var tags = CreateSampleTags();
        var target = tags.Single(t => t.Id == 1);
        HashSet<int> expanded = [1];

        var lines = TagTreePopoverViewModel.BuildTreeLines(target, tags, expanded, enableExpand: true);

        Assert.Equal([1, 2, 4], lines.Select(l => l.TagId));
        Assert.Equal("▼", lines[0].Icon);
        // 子タグはインデントが深くなる
        Assert.Equal(new string(' ', 2), lines[1].Indent);
    }

    [Fact]
    public void BuildTreeLines_ForEmptyTagList_ReturnsNoLines()
    {
        var tag = CreateTag(1, "root");

        var lines = TagTreePopoverViewModel.BuildTreeLines(tag, [], [], enableExpand: true);

        Assert.Empty(lines);
    }
}