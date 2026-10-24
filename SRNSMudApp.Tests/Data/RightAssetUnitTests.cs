#region

using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;

#endregion

namespace SRNSMudApp.Tests.Data;

/// <summary>
///     <see cref="RightAsset" /> の消費（Consume）および燃焼（Burn）ドメインメソッドに対する純粋単体テスト。
///     データベース接続を一切介さず、残高更新・Burned 状態遷移・例外条件を高速に検証する。
/// </summary>
public sealed class RightAssetUnitTests
{
    private static RightAsset CreateAsset(int amount = 10, bool isBurned = false) =>
        new()
        {
            Id = 1,
            OwnerId = "user1",
            TargetTagId = 100,
            Amount = amount,
            IsBurned = isBurned
        };

    [Fact]
    public void Consume_WhenPartialAmount_DecrementsAmountAndPreservesNotBurned()
    {
        // Arrange
        RightAsset asset = CreateAsset(amount: 10);

        // Act
        asset.Consume(weightToConsume: 4);

        // Assert
        Assert.Equal(6, asset.Amount);
        Assert.False(asset.IsBurned);
        Assert.True(asset.Status is NotBurned);
    }

    [Fact]
    public void Consume_WhenExactAmount_SetsAmountZeroAndMarksBurned()
    {
        // Arrange
        RightAsset asset = CreateAsset(amount: 5);
        DateTime burnedTime = new(2026, 10, 24, 8, 0, 0, DateTimeKind.Utc);

        // Act
        asset.Consume(weightToConsume: 5, burnedAt: burnedTime);

        // Assert
        Assert.Equal(0, asset.Amount);
        Assert.True(asset.IsBurned);
        Assert.True(asset.Status is Burned b && b.BurnedAt == burnedTime);
    }

    [Fact]
    public void Consume_WhenAmountExceedsRemaining_SetsAmountZeroAndMarksBurned()
    {
        // Arrange
        RightAsset asset = CreateAsset(amount: 3);
        DateTime burnedTime = new(2026, 10, 24, 8, 0, 0, DateTimeKind.Utc);

        // Act
        asset.Consume(weightToConsume: 10, burnedAt: burnedTime);

        // Assert
        Assert.Equal(0, asset.Amount);
        Assert.True(asset.IsBurned);
        Assert.True(asset.Status is Burned b && b.BurnedAt == burnedTime);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-5)]
    public void Consume_WhenZeroOrNegative_ThrowsArgumentOutOfRangeException(int invalidWeight)
    {
        // Arrange
        RightAsset asset = CreateAsset(amount: 10);

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => asset.Consume(invalidWeight));
    }

    [Fact]
    public void Burn_SetsAmountToZeroAndMarksBurned()
    {
        // Arrange
        RightAsset asset = CreateAsset(amount: 20);
        DateTime burnedTime = new(2026, 10, 24, 9, 30, 0, DateTimeKind.Utc);

        // Act
        asset.Burn(burnedAt: burnedTime);

        // Assert
        Assert.Equal(0, asset.Amount);
        Assert.True(asset.IsBurned);
        Assert.True(asset.Status is Burned b && b.BurnedAt == burnedTime);
    }

    [Fact]
    public void CanBeConsumedBy_WhenValid_ReturnsTrue()
    {
        // Arrange
        RightAsset asset = CreateAsset(amount: 10);

        // Act & Assert
        Assert.True(asset.CanBeConsumedBy(ownerId: "user1", targetTagId: 100, amount: 5));
        Assert.True(asset.CanBeConsumedBy(ownerId: "user1", targetTagId: 100, amount: 10));
    }

    [Theory]
    [InlineData("wrong_user", 100, 5)]  // 所有者違い
    [InlineData("user1", 999, 5)]        // タグID違い
    [InlineData("user1", 100, 11)]       // 残高不足
    [InlineData("user1", 100, 0)]        // 0消費
    [InlineData("user1", 100, -1)]       // 負の消費
    public void CanBeConsumedBy_WhenInvalidConditions_ReturnsFalse(string ownerId, int targetTagId, int amount)
    {
        // Arrange
        RightAsset asset = CreateAsset(amount: 10);

        // Act & Assert
        Assert.False(asset.CanBeConsumedBy(ownerId, targetTagId, amount));
    }

    [Fact]
    public void CanBeConsumedBy_WhenBurned_ReturnsFalse()
    {
        // Arrange
        RightAsset asset = CreateAsset(amount: 10, isBurned: true);

        // Act & Assert
        Assert.False(asset.CanBeConsumedBy(ownerId: "user1", targetTagId: 100, amount: 5));
    }
}