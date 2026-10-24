#region

using SRNSMudApp.Data;

#endregion

namespace SRNSMudApp.Tests.Data;

/// <summary>
///     <see cref="Tag.ApplyWeightDelta" /> の純粋単体テスト。
///     データベース接続を一切介さず、タグの CachedWeight 更新および前後値の整合性を検証する。
/// </summary>
public sealed class TagWeightUnitTests
{
    [Fact]
    public void ApplyWeightDelta_WhenPositiveDelta_IncreasesWeightAndReturnsPreviousAndNew()
    {
        // Arrange
        var tag = new Tag
        {
            Name = "csharp",
            OwnerId = "user1",
            CachedWeight = 10
        };

        // Act
        var (prev, next) = tag.ApplyWeightDelta(5);

        // Assert
        Assert.Equal(10, prev);
        Assert.Equal(15, next);
        Assert.Equal(15, tag.CachedWeight);
    }

    [Fact]
    public void ApplyWeightDelta_WhenNegativeDelta_DecreasesWeightAndReturnsPreviousAndNew()
    {
        // Arrange
        var tag = new Tag
        {
            Name = "dotnet",
            OwnerId = "user1",
            CachedWeight = 20
        };

        // Act
        var (prev, next) = tag.ApplyWeightDelta(-8);

        // Assert
        Assert.Equal(20, prev);
        Assert.Equal(12, next);
        Assert.Equal(12, tag.CachedWeight);
    }

    [Fact]
    public void ApplyWeightDelta_WhenZeroDelta_PreservesWeight()
    {
        // Arrange
        var tag = new Tag
        {
            Name = "blazor",
            OwnerId = "user1",
            CachedWeight = 15
        };

        // Act
        var (prev, next) = tag.ApplyWeightDelta(0);

        // Assert
        Assert.Equal(15, prev);
        Assert.Equal(15, next);
        Assert.Equal(15, tag.CachedWeight);
    }
}