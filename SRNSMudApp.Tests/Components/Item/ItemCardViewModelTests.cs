#region

using SRNSMudApp.Components.UI;
using SRNSMudApp.Data;

#endregion

namespace SRNSMudApp.Tests.Components.Item;

/// <summary>
///     ItemCardViewModel の単体テスト。
///     bUnit・Blazor・DB接続不要で高速に実行できる。
///     これらのテストは以前 E2E (Playwright) でしか検証できなかったロジックをカバーする。
/// </summary>
public class ItemCardViewModelTests
{
    // ────────────────────────────────────────────────────────────
    // GetItemCardStyle
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void GetItemCardStyle_WhenFocused_ReturnsPrimaryBorderStyle()
    {
        var style = ItemCardViewModel.GetItemCardStyle(true);

        Assert.Contains("border-width: 2px", style);
        Assert.Contains("var(--mud-palette-primary)", style);
    }

    [Fact]
    public void GetItemCardStyle_WhenNotFocused_ReturnsDefaultBorderStyle()
    {
        var style = ItemCardViewModel.GetItemCardStyle(false);

        Assert.Contains("border-width: 1px", style);
        Assert.Contains("var(--mud-palette-lines-default)", style);
    }

    // ────────────────────────────────────────────────────────────
    // GetItemScore
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void GetItemScore_WithTwoGoodAndOneDownvote_ReturnsOne()
    {
        var relations = new List<TagRelation>
        {
            new()
            {
                OwnerId = "u1",
                Weight = 1,
                Tag = new SRNSMudApp.Data.Tag { Name = "good", IsSystem = true, OwnerId = "sys" }
            },
            new()
            {
                OwnerId = "u2",
                Weight = 1,
                Tag = new SRNSMudApp.Data.Tag { Name = "good", IsSystem = true, OwnerId = "sys" }
            },
            new()
            {
                OwnerId = "u3",
                Weight = -1,
                Tag = new SRNSMudApp.Data.Tag { Name = "good", IsSystem = true, OwnerId = "sys" }
            }
        };

        var score = ItemCardViewModel.GetItemScore(relations);

        Assert.Equal(1, score);
    }

    [Fact]
    public void GetItemScore_WithNull_ReturnsZero() => Assert.Equal(0, ItemCardViewModel.GetItemScore(null));

    [Fact]
    public void GetItemScore_WithNoVoteTags_ReturnsZero()
    {
        var relations = new List<TagRelation>
        {
            new()
            {
                OwnerId = "u1",
                Tag = new SRNSMudApp.Data.Tag { Name = "someTag", IsSystem = false, OwnerId = "u1" }
            }
        };

        Assert.Equal(0, ItemCardViewModel.GetItemScore(relations));
    }

    // ────────────────────────────────────────────────────────────
    // IsItemUpvoted / IsItemDownvoted
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void IsItemUpvoted_WhenUserHasGoodTag_ReturnsTrue()
    {
        const string userId = "user-1";
        const int goodTagId = 10;
        var relations = new List<TagRelation> { new() { TagId = goodTagId, OwnerId = userId } };

        Assert.True(ItemCardViewModel.IsItemUpvoted(relations, userId, goodTagId));
    }

    [Fact]
    public void IsItemUpvoted_WhenUserDoesNotHaveGoodTag_ReturnsFalse()
    {
        const string userId = "user-1";
        const int goodTagId = 10;
        var relations = new List<TagRelation> { new() { TagId = goodTagId, OwnerId = "other-user" } };

        Assert.False(ItemCardViewModel.IsItemUpvoted(relations, userId, goodTagId));
    }

    [Fact]
    public void IsItemDownvoted_WhenNoBadTagId_ReturnsFalse() =>
        Assert.False(ItemCardViewModel.IsItemDownvoted([], "user-1", null));

    // ────────────────────────────────────────────────────────────
    // GetReactionCount / IsItemReacted
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void GetReactionCount_WithMatchingReactionTag_ReturnsCount()
    {
        var relations = new List<TagRelation>
        {
            new() { Tag = new SRNSMudApp.Data.Tag { Name = "真実", IsSystem = true, OwnerId = "sys-1" }, Weight = 1, OwnerId = "u1" },
            new() { Tag = new SRNSMudApp.Data.Tag { Name = "真実", IsSystem = true, OwnerId = "sys-1" }, Weight = 1, OwnerId = "u2" },
            new() { Tag = new SRNSMudApp.Data.Tag { Name = "善", IsSystem = true, OwnerId = "sys-1" }, Weight = 1, OwnerId = "u1" },
            new() { Tag = new SRNSMudApp.Data.Tag { Name = "good", IsSystem = true, OwnerId = "sys-1" }, Weight = 1, OwnerId = "u1" }
        };

        Assert.Equal(2, ItemCardViewModel.GetReactionCount(relations, "真実"));
        Assert.Equal(1, ItemCardViewModel.GetReactionCount(relations, "善"));
        Assert.Equal(0, ItemCardViewModel.GetReactionCount(relations, "美"));
    }

    [Fact]
    public void IsItemReacted_WhenUserHasReactionTag_ReturnsTrue()
    {
        const string userId = "user-1";
        const int shinjiTagId = 100;
        var relations = new List<TagRelation> { new() { TagId = shinjiTagId, OwnerId = userId, Weight = 1 } };

        Assert.True(ItemCardViewModel.IsItemReacted(relations, userId, shinjiTagId));
    }

    [Fact]
    public void IsItemReacted_WhenUserDoesNotHaveReactionTag_ReturnsFalse()
    {
        const string userId = "user-1";
        const int shinjiTagId = 100;
        var relations = new List<TagRelation> { new() { TagId = shinjiTagId, OwnerId = "other-user", Weight = 1 } };

        Assert.False(ItemCardViewModel.IsItemReacted(relations, userId, shinjiTagId));
    }

    // ────────────────────────────────────────────────────────────
    // CanModifyRelation
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void CanModifyRelation_WhenOwnerMatches_ReturnsTrue() =>
        Assert.True(ItemCardViewModel.CanModifyRelation("user-1", "user-1"));

    [Fact]
    public void CanModifyRelation_WhenOwnerDiffers_ReturnsFalse() =>
        Assert.False(ItemCardViewModel.CanModifyRelation("user-1", "user-2"));

    [Fact]
    public void CanModifyRelation_WhenCurrentUserIsEmpty_ReturnsFalse() =>
        Assert.False(ItemCardViewModel.CanModifyRelation("user-1", ""));

    // ────────────────────────────────────────────────────────────
    // ExtractUrls
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void ExtractUrls_WithSingleUrl_ReturnsIt()
    {
        IReadOnlyList<string> urls = ItemCardViewModel.ExtractUrls("Hello https://example.com world");

        Assert.Single(urls);
        Assert.Equal("https://example.com", urls[0]);
    }

    [Fact]
    public void ExtractUrls_WithDuplicateUrls_ReturnsDeduplicated()
    {
        IReadOnlyList<string> urls = ItemCardViewModel.ExtractUrls("https://a.com and https://a.com again");

        Assert.Single(urls);
    }

    [Fact]
    public void ExtractUrls_WithNoUrl_ReturnsEmpty() => Assert.Empty(ItemCardViewModel.ExtractUrls("No URLs here"));

    [Fact]
    public void ExtractUrls_WithNull_ReturnsEmpty() => Assert.Empty(ItemCardViewModel.ExtractUrls(null));

    [Fact]
    public void ExtractUrls_WithMultipleUrls_ReturnsAll()
    {
        var text = "See https://example.com and https://github.com for details";
        IReadOnlyList<string> urls = ItemCardViewModel.ExtractUrls(text);

        Assert.Equal(2, urls.Count);
        Assert.Contains("https://example.com", urls);
        Assert.Contains("https://github.com", urls);
    }

    // ────────────────────────────────────────────────────────────
    // GetShortOwnerName
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void GetShortOwnerName_WithShortName_ReturnsAsIs() =>
        Assert.Equal("alice", ItemCardViewModel.GetShortOwnerName("alice"));

    [Fact]
    public void GetShortOwnerName_WithLongName_TruncatesTo7Chars() =>
        Assert.Equal("longuse", ItemCardViewModel.GetShortOwnerName("longusernamefoo"));

    [Fact]
    public void GetShortOwnerName_WithNull_ReturnsUnknown() =>
        Assert.Equal("不明", ItemCardViewModel.GetShortOwnerName(null));

    // ────────────────────────────────────────────────────────────
    // GetTagDisplayWeight
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void GetTagDisplayWeight_WithNoEvent_ReturnsRelationWeight()
    {
        var relation = new TagRelation { OwnerId = "u1", Weight = 5 };
        Assert.Equal("5", ItemCardViewModel.GetTagDisplayWeight(relation, null));
    }

    [Fact]
    public void GetTagDisplayWeight_WithUpdateEvent_ReturnsDiffFormat()
    {
        var relation = new TagRelation { OwnerId = "u1", Weight = 3 };
        var ev = new TimelineEvent { OwnerId = "u1", EventType = "Update", PreviousWeight = 2, NewWeight = 3 };

        Assert.Equal("2 → 3", ItemCardViewModel.GetTagDisplayWeight(relation, ev));
    }

    [Fact]
    public void GetTagDisplayWeight_WithDeleteEvent_ReturnsPreviousWeight()
    {
        var relation = new TagRelation { OwnerId = "u1", Weight = 2 };
        var ev = new TimelineEvent { OwnerId = "u1", EventType = "Delete", PreviousWeight = 2 };

        Assert.Equal("2", ItemCardViewModel.GetTagDisplayWeight(relation, ev));
    }
}