using SRNSMudApp.Components.UI;

namespace SRNSMudApp.Tests.Components.UI;

// 親名前空間の下にある namespace Tag より先に Data.Tag 型を解決させるため、
// エイリアスを名前空間の内側に置く
using Tag = SRNSMudApp.Data.Tag;

/// <summary>
///     ResourceListViewModel の単体テスト。
///     システムタグ検索とスクロール先セレクタ計算を bUnit なしで検証する。
/// </summary>
public class ResourceListViewModelTests
{
    private static Tag CreateTag(int id, string name, string ownerId, bool isSystem = false) =>
        new() { Id = id, Name = name, OwnerId = ownerId, IsSystem = isSystem };

    [Fact]
    public void FindSystemTags_ReturnsCurrentUserSystemTagIds()
    {
        List<Tag> tags =
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
        List<Tag> tags = [CreateTag(1, "good", "user-1", isSystem: false)];

        var result = ResourceListViewModel.FindSystemTags(tags, "user-1");

        Assert.Null(result.GoodTagId);
        Assert.False(result.IsComplete);
    }

    [Fact]
    public void FindSystemTags_ForAnonymousUser_ReturnsDefault()
    {
        List<Tag> tags = [CreateTag(1, "good", "user-1", isSystem: true)];

        Assert.Equal(default, ResourceListViewModel.FindSystemTags(tags, ""));
        Assert.Equal(default, ResourceListViewModel.FindSystemTags(null, "user-1"));
    }

    [Theory]
    [InlineData(10, null, "#tag-card-10")]
    [InlineData(10, 20, "#tag-card-10")]
    [InlineData(null, 20, "#item-card-20")]
    public void GetFocusSelector_PrefersTagOverItem(int? tagId, int? itemId, string expected) => Assert.Equal(expected, ResourceListViewModel.GetFocusSelector(tagId, itemId));

    [Fact]
    public void GetFocusSelector_WithNoFocus_ReturnsNull() => Assert.Null(ResourceListViewModel.GetFocusSelector(null, null));
}