using SRNSMudApp.Components.UI;

namespace SRNSMudApp.Tests.Components.UI;

/// <summary>
///     ResourceListViewModel の単体テスト。
///     システムタグ検索とスクロール先セレクタ計算を bUnit なしで検証する。
/// </summary>
public class ResourceListViewModelTests
{
    private static SRNSMudApp.Data.Tag CreateTag(int id, string name, string ownerId, bool isSystem = false) =>
        new() { Id = id, Name = name, OwnerId = ownerId, IsSystem = isSystem };

    [Fact]
    public void FindSystemTags_ReturnsCurrentUserSystemTagIds()
    {
        List<SRNSMudApp.Data.Tag> tags =
        [
            CreateTag(1, "good", "user-1", isSystem: true),
            CreateTag(2, "bad", "user-1", isSystem: true),
            CreateTag(3, "good", "user-2", isSystem: true) // 他人のタグは対象外
        ];

        var result = ResourceListViewModel.FindSystemTags(tags, "user-1");

        Assert.Equal(1, result.GoodTagId);
        Assert.Equal(2, result.BadTagId);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public void FindSystemTags_IgnoresNonSystemTags()
    {
        List<SRNSMudApp.Data.Tag> tags = [CreateTag(1, "good", "user-1", isSystem: false)];

        var result = ResourceListViewModel.FindSystemTags(tags, "user-1");

        Assert.Null(result.GoodTagId);
        Assert.False(result.IsComplete);
    }

    [Fact]
    public void FindSystemTags_ForAnonymousUser_ReturnsDefault()
    {
        List<SRNSMudApp.Data.Tag> tags = [CreateTag(1, "good", "user-1", isSystem: true)];

        Assert.Equal(default, ResourceListViewModel.FindSystemTags(tags, ""));
        Assert.Equal(default, ResourceListViewModel.FindSystemTags(null, "user-1"));
    }

    [Fact]
    public void FindReactionTags_ReturnsAllThreeReactionTagIds()
    {
        List<SRNSMudApp.Data.Tag> tags =
        [
            CreateTag(10, "真実", "user-1", isSystem: true),
            CreateTag(11, "善", "user-1", isSystem: true),
            CreateTag(12, "美", "user-1", isSystem: true),
            CreateTag(13, "真実", "user-2", isSystem: true)
        ];

        var result = ResourceListViewModel.FindReactionTags(tags, "user-1");

        Assert.Equal(10, result.ShinjiTagId);
        Assert.Equal(11, result.ZenTagId);
        Assert.Equal(12, result.BiTagId);
        Assert.True(result.IsComplete);
    }

    [Theory]
    [InlineData(10, null, "#tag-card-10")]
    [InlineData(10, 20, "#tag-card-10")]
    [InlineData(null, 20, "#item-card-20")]
    public void GetFocusSelector_PrefersTagOverItem(int? tagId, int? itemId, string expected) => Assert.Equal(expected, ResourceListViewModel.GetFocusSelector(tagId, itemId));

    [Fact]
    public void GetFocusSelector_WithNoFocus_ReturnsNull() => Assert.Null(ResourceListViewModel.GetFocusSelector(null, null));
}