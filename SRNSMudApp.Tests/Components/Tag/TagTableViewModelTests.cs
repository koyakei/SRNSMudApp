using SRNSMudApp.Components.Tag;
using SRNSMudApp.Data;

using Xunit;

namespace SRNSMudApp.Tests.Components.Tag;

// 親名前空間の下にある namespace Tag より先に Data.Tag 型を解決させるため、
// エイリアスを名前空間の内側に置く
using Tag = SRNSMudApp.Data.Tag;

/// <summary>
///     TagTableViewModel の単体テスト。
///     検索フィルタ・添付タグ表示計算・権限判定を bUnit なしで検証する。
/// </summary>
public class TagTableViewModelTests
{
    private static Tag CreateTag(int id = 1, string name = "Tag", string ownerId = "user-1", bool isSystem = false) =>
        new()
        {
            Id = id,
            Name = name,
            OwnerId = ownerId,
            IsSystem = isSystem,
            Content = $"content of {name}"
        };

    private static TagRelationToTag CreateRelation(
        int id, int weight = 1, string ownerId = "user-1", bool isSystemTag = false)
    {
        var tag = CreateTag(id, $"tag-{id}", ownerId, isSystemTag);
        return new TagRelationToTag { Id = id, Weight = weight, OwnerId = ownerId, Tag = tag };
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FilterFunc_WithBlankSearch_MatchesAllTags(string? search)
    {
        var tag = CreateTag();

        Assert.True(TagTableViewModel.FilterFunc(tag, search!));
    }

    [Fact]
    public void FilterFunc_ByTagName_Matches()
    {
        var tag = CreateTag(name: "Server");

        Assert.True(TagTableViewModel.FilterFunc(tag, "serv"));
    }

    [Fact]
    public void FilterFunc_ByContentOrOwnerName_Matches()
    {
        var tag = CreateTag(name: "Server");
        tag.Owner = new ApplicationUser { Id = tag.OwnerId, UserName = "alice" };

        Assert.True(TagTableViewModel.FilterFunc(tag, "content"));
        Assert.True(TagTableViewModel.FilterFunc(tag, "ALICE"));
    }

    [Fact]
    public void FilterFunc_WithNoMatch_ReturnsFalse()
    {
        var tag = CreateTag(name: "Server");

        Assert.False(TagTableViewModel.FilterFunc(tag, "database"));
    }

    [Fact]
    public void GetTagSearchSuggestions_WithEmptyValue_ReturnsDistinctNamesLimitedTo20()
    {
        List<Tag> tags = Enumerable.Range(1, 30)
            .Select(i => CreateTag(i, $"tag-{i % 25}"))
            .ToList();

        var suggestions = TagTableViewModel.GetTagSearchSuggestions(tags, "");

        Assert.Equal(20, suggestions.Count);
        Assert.All(suggestions, Assert.NotNull);
    }

    [Fact]
    public void GetTagSearchSuggestions_WithValue_FiltersByName()
    {
        List<Tag> tags = [CreateTag(1, "server"), CreateTag(2, "service"), CreateTag(3, "database")];

        var suggestions = TagTableViewModel.GetTagSearchSuggestions(tags, "ser");

        Assert.Equal(["server", "service"], suggestions);
    }

    [Fact]
    public void GetAttachedTags_ExcludesSystemTags_AndSortsByWeightDescending()
    {
        var tag = CreateTag();
        tag.TargetTagRelations =
        [
            CreateRelation(1, weight: 1),
            CreateRelation(2, weight: 5),
            CreateRelation(3, weight: 3, isSystemTag: true)
        ];

        var attached = TagTableViewModel.GetAttachedTags(tag);

        Assert.Equal([2, 1], attached.Select(tr => tr.Id));
    }

    [Fact]
    public void GetAttachedTagsDisplay_ForManyTags_CollapsesToLimitWithHiddenCount()
    {
        var tag = CreateTag();
        tag.TargetTagRelations =
        [
            CreateRelation(1, weight: 5),
            CreateRelation(2, weight: 4),
            CreateRelation(3, weight: 3)
        ];

        var display = TagTableViewModel.GetAttachedTagsDisplay(tag, isExpanded: false);

        Assert.True(display.HasManyTags);
        Assert.Equal(1, display.HiddenCount);
        Assert.Equal([1, 2], display.TagsToDisplay.Select(tr => tr.Id));
        Assert.Equal("+1 more", display.ToggleLabel);
    }

    [Fact]
    public void GetAttachedTagsDisplay_WhenExpanded_ShowsAllTags()
    {
        var tag = CreateTag();
        tag.TargetTagRelations =
        [
            CreateRelation(1, weight: 5),
            CreateRelation(2, weight: 4),
            CreateRelation(3, weight: 3)
        ];

        var display = TagTableViewModel.GetAttachedTagsDisplay(tag, isExpanded: true);

        Assert.True(display.HasManyTags);
        Assert.Equal(0, display.HiddenCount);
        Assert.Equal(3, display.TagsToDisplay.Count);
        Assert.Equal("閉じる", display.ToggleLabel);
    }

    [Fact]
    public void GetAttachedTagsDisplay_ForFewTags_ShowsAllWithoutToggle()
    {
        var tag = CreateTag();
        tag.TargetTagRelations = [CreateRelation(1), CreateRelation(2)];

        var display = TagTableViewModel.GetAttachedTagsDisplay(tag, isExpanded: false);

        Assert.False(display.HasManyTags);
        Assert.Equal(0, display.HiddenCount);
        Assert.Equal(2, display.TagsToDisplay.Count);
    }

    [Theory]
    [InlineData("user-1", true)]
    [InlineData("user-2", false)]
    public void CanEditTag_OnlyForOwner(string userId, bool expected)
    {
        Assert.Equal(expected, TagTableViewModel.CanEditTag(CreateTag(ownerId: "user-1"), userId));
    }

    [Fact]
    public void CanDeleteTag_SystemTagIsNotDeletable_EvenByOwner()
    {
        Assert.False(TagTableViewModel.CanDeleteTag(CreateTag(isSystem: true), "user-1"));
        Assert.True(TagTableViewModel.CanDeleteTag(CreateTag(isSystem: false), "user-1"));
    }

    [Theory]
    [InlineData("user-1", true)]
    [InlineData("user-2", false)]
    public void CanRemoveRelation_OnlyForRelationOwner(string userId, bool expected)
    {
        Assert.Equal(expected, TagTableViewModel.CanRemoveRelation(CreateRelation(1), userId));
    }
}
