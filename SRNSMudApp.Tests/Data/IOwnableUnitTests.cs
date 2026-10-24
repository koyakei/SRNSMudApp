using SRNSMudApp.Data;

namespace SRNSMudApp.Tests.Data;

/// <summary>
///     <see cref="IOwnable"/> のデフォルト実装メソッド <see cref="IOwnable.IsOwnedBy"/> に対する
///     純粋単体テスト。所有者判定ロジックの一致・不一致・境界条件を検証する。
/// </summary>
public sealed class IOwnableUnitTests
{
    private sealed class TestOwnable : IOwnable
    {
        public string OwnerId { get; set; } = string.Empty;
        public ApplicationUser Owner { get; set; } = null!;
    }

    [Theory]
    [InlineData("user-123", "user-123", true)]
    [InlineData("user-123", "user-456", false)]
    [InlineData("user-123", "", false)]
    [InlineData("user-123", "   ", false)]
    [InlineData("user-123", null, false)]
    public void IsOwnedBy_EvaluatesOwnershipCorrectly(string ownerId, string? targetUserId, bool expected)
    {
        IOwnable ownable = new TestOwnable { OwnerId = ownerId };
        bool result = ownable.IsOwnedBy(targetUserId!);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void IsOwnedBy_WhenBothOwnerIdAndTargetUserIdAreEmpty_ReturnsTrue()
    {
        IOwnable ownable = new TestOwnable { OwnerId = "" };
        Assert.True(ownable.IsOwnedBy(""));
    }

    [Fact]
    public void Item_ImplementsIOwnable_AndIsOwnedByWorksCorrectly()
    {
        var item = new Item { OwnerId = "author-abc", Content = "Test Content" };
        IOwnable ownable = item;

        Assert.True(ownable.IsOwnedBy("author-abc"));
        Assert.False(ownable.IsOwnedBy("stranger-xyz"));
    }
}