using MudBlazor;

using SRNSMudApp.Components.UI;
using SRNSMudApp.Data;

namespace SRNSMudApp.Tests.Components.UI;

/// <summary>
///     TagCardViewModel (純粋ロジック) の単体テスト。bUnit を使わずに検証する。
/// </summary>
public class TagCardViewModelTests
{
    private static SRNSMudApp.Data.Tag CreateTag(List<TagRelationToTag>? relations = null) => new() { Id = 1, Name = "root", OwnerId = "owner", TargetTagRelations = relations ?? [] };

    private static TagRelationToTag CreateRelation(int id, int tagId, string? tagName = null, bool isSystem = false,
        int weight = 1, string ownerId = "owner")
    {
        return new TagRelationToTag
        {
            Id = id,
            TagId = tagId,
            TargetTagId = 1,
            Weight = weight,
            OwnerId = ownerId,
            Tag = tagName == null ? null! : new SRNSMudApp.Data.Tag { Id = tagId, Name = tagName, IsSystem = isSystem, OwnerId = "tag-owner" }
        };
    }

    [Fact]
    public void GetTagScore_GoodMinusBad()
    {
        SRNSMudApp.Data.Tag tag = CreateTag(
        [
            CreateRelation(1, 10, "good", isSystem: true),
            CreateRelation(2, 11, "good", isSystem: true),
            CreateRelation(3, 12, "bad", isSystem: true),
            CreateRelation(4, 13, "normal")
        ]);

        Assert.Equal(1, TagCardViewModel.GetTagScore(tag));
    }

    [Fact]
    public void GetTagScore_NoRelations_ReturnsZero() => Assert.Equal(0, TagCardViewModel.GetTagScore(CreateTag()));

    [Fact]
    public void IsTagUpvoted_MatchesRelationOwnerAndSystemTag()
    {
        SRNSMudApp.Data.Tag tag = CreateTag([CreateRelation(1, 10, "good", isSystem: true, ownerId: "me")]);

        Assert.True(TagCardViewModel.IsTagUpvoted(tag, 10, "me"));
        Assert.False(TagCardViewModel.IsTagUpvoted(tag, 10, "other"));
        Assert.False(TagCardViewModel.IsTagUpvoted(tag, null, "me"));
    }

    [Fact]
    public void BuildDisplayList_ExcludesSystemTagsAndSortsByWeightDesc()
    {
        SRNSMudApp.Data.Tag tag = CreateTag(
        [
            CreateRelation(1, 10, "a", weight: 1),
            CreateRelation(2, 11, "b", weight: 5),
            CreateRelation(3, 12, "good", isSystem: true, weight: 9)
        ]);

        TagCardDisplayList display = TagCardViewModel.BuildDisplayList(tag, null, areTagsExpanded: false);

        Assert.Equal([11, 10], display.TagsToDisplay.Select(tr => tr.TagId));
        Assert.False(display.HasManyTags);
        Assert.Equal(0, display.HiddenCount);
    }

    [Fact]
    public void BuildDisplayList_WithFiveTags_CollapsesAndCountsHidden_WhenNotExpanded()
    {
        SRNSMudApp.Data.Tag tag = CreateTag(
        [
            CreateRelation(1, 10, "a"), CreateRelation(2, 11, "b"),
            CreateRelation(3, 12, "c"), CreateRelation(4, 13, "d"),
            CreateRelation(5, 14, "e")
        ]);

        TagCardDisplayList collapsed = TagCardViewModel.BuildDisplayList(tag, null, areTagsExpanded: false);
        TagCardDisplayList expanded = TagCardViewModel.BuildDisplayList(tag, null, areTagsExpanded: true);

        Assert.True(collapsed.HasManyTags);
        Assert.Equal(TagCardViewModel.DisplayLimit, collapsed.TagsToDisplay.Count);
        Assert.Equal(1, collapsed.HiddenCount);
        Assert.Equal(5, expanded.TagsToDisplay.Count);
        // 元実装と同じく、展開時も HiddenCount 自体は計算される（マークアップ側で非表示にするだけ）
        Assert.Equal(1, expanded.HiddenCount);
    }

    [Fact]
    public void BuildDisplayList_DeleteEventForMissingTag_AddsVirtualRelation()
    {
        SRNSMudApp.Data.Tag tag = CreateTag([]);
        List<TimelineEvent> events =
        [
            new()
            {
                EventType = "Delete",
                FollowedTagId = 99,
                PreviousWeight = 3,
                OwnerId = "someone",
                FollowedTag = new SRNSMudApp.Data.Tag { Id = 99, Name = "deleted-tag", OwnerId = "tag-owner" }
            }
        ];

        TagCardDisplayList display = TagCardViewModel.BuildDisplayList(tag, events, areTagsExpanded: false);

        TagRelationToTag virtualRelation = Assert.Single(display.TagsToDisplay);
        Assert.Equal(99, virtualRelation.TagId);
        Assert.Equal(3, virtualRelation.Weight);
        Assert.Equal("someone", virtualRelation.OwnerId);
    }

    [Fact]
    public void GetChipDisplayInfo_DeletedEvent_RendersStrikethroughColors()
    {
        TagRelationToTag relation = CreateRelation(1, 10, "a");
        TimelineEvent ev = new() { EventType = "Delete", PreviousWeight = 4, OwnerId = "someone" };

        TagCardChipDisplayInfo info = TagCardViewModel.GetChipDisplayInfo(relation, ev, isMyTag: true, index: 0);

        Assert.True(info.IsDeleted);
        Assert.Equal("#E0E0E0", info.BackgroundColor);
        Assert.Equal("#9E9E9E", info.TextColor);
        Assert.Equal("4", info.DisplayWeight);
    }

    [Fact]
    public void GetChipDisplayInfo_UpdateEvent_ShowsTransitionAndSuccessColor()
    {
        TagRelationToTag relation = CreateRelation(1, 10, "a");
        TimelineEvent ev = new() { EventType = "Update", PreviousWeight = 2, NewWeight = 6, OwnerId = "someone" };

        TagCardChipDisplayInfo info = TagCardViewModel.GetChipDisplayInfo(relation, ev, isMyTag: false, index: 0);

        Assert.True(info.IsUpdated);
        Assert.True(info.WeightIncreased);
        Assert.Equal(Color.Success, info.AddButtonColor);
        Assert.Equal("2 → 6", info.DisplayWeight);
    }

    [Fact]
    public void GetChipDisplayInfo_PlainMyTag_UsesMyTagPalette()
    {
        TagRelationToTag relation = CreateRelation(1, 10, "a");

        TagCardChipDisplayInfo mine = TagCardViewModel.GetChipDisplayInfo(relation, null, isMyTag: true, index: 0);
        TagCardChipDisplayInfo others = TagCardViewModel.GetChipDisplayInfo(relation, null, isMyTag: false, index: 0);

        Assert.Equal("#EEEDFE", mine.BackgroundColor);
        Assert.Equal("#26215C", mine.TextColor);
        Assert.Equal("#FFF9C4", others.BackgroundColor);
        Assert.Equal("#5C4B00", others.TextColor);
    }

    [Fact]
    public void HasParentCycle_DetectsDirectAndIndirectCycles()
    {
        SRNSMudApp.Data.Tag child = new() { Id = 1, Name = "child", OwnerId = "u1" };
        SRNSMudApp.Data.Tag parent = new() { Id = 2, ParentTagId = 1, Name = "parent", OwnerId = "u1" };

        Assert.True(TagCardViewModel.HasParentCycle(parent, child, [child, parent]));

        SRNSMudApp.Data.Tag grandChild = new() { Id = 3, ParentTagId = 2, Name = "grand", OwnerId = "u1" };
        Assert.True(TagCardViewModel.HasParentCycle(grandChild, parent, [parent, grandChild]));

        SRNSMudApp.Data.Tag unrelated = new() { Id = 4, ParentTagId = 5, Name = "unrelated", OwnerId = "u1" };
        Assert.False(TagCardViewModel.HasParentCycle(unrelated, child, [child, parent]));
    }

    [Theory]
    [InlineData(null, "不明")]
    [InlineData("", "不明")]
    [InlineData("short", "short")]
    [InlineData("verylongname", "verylon")]
    public void GetShortOwnerName_TruncatesToSevenChars(string? name, string expected) => Assert.Equal(expected, TagCardViewModel.GetShortOwnerName(name));

    [Theory]
    [InlineData("user-1", "user-1", true)]
    [InlineData("owner-x", "user-1", false)]
    public void IsRelationOwner_ComparesOwnerAndUser(string relationOwnerId, string currentUserId, bool expected) => Assert.Equal(expected, TagCardViewModel.IsRelationOwner(relationOwnerId, currentUserId));

    [Fact]
    public void IsSelfParent_DetectsSelfReference()
    {
        SRNSMudApp.Data.Tag tag = new() { Id = 1, Name = "self", OwnerId = "u1" };
        SRNSMudApp.Data.Tag other = new() { Id = 2, Name = "other", OwnerId = "u1" };

        Assert.True(TagCardViewModel.IsSelfParent(tag, tag));
        Assert.False(TagCardViewModel.IsSelfParent(tag, other));
    }

    [Theory]
    [InlineData(10, 10, true)]
    [InlineData(10, 20, false)]
    public void IsSameTagChange_DetectsNoOpChange(int currentTagId, int newTagId, bool expected) => Assert.Equal(expected, TagCardViewModel.IsSameTagChange(currentTagId, newTagId));

    [Theory]
    [InlineData(5, 5, false)]
    [InlineData(5, 7, true)]
    [InlineData(7, 5, true)]
    public void HasWeightChange_DetectsWeightDelta(int currentWeight, int newWeight, bool expected) => Assert.Equal(expected, TagCardViewModel.HasWeightChange(currentWeight, newWeight));
}