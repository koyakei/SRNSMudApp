#region

using SRNSMudApp.Data;
using SRNSMudApp.Services;

#endregion

namespace SRNSMudApp.Tests.Services;

/// <summary>
///     <see cref="TagVisibilityExtensions" /> の単体テスト。
///     BAN されたユーザーがオーナーのタグが不可視となることを検証する。
/// </summary>
public sealed class TagVisibilityExtensionsTests
{
    [Fact]
    public void IsTagVisibleToUser_WhenOwnerIsNull_ReturnsTrue()
    {
        // Arrange
        var tag = new Tag { Name = "TagWithoutOwner", OwnerId = "user1", Owner = null! };

        // Act & Assert
        Assert.True(tag.IsTagVisibleToUser());
    }

    [Fact]
    public void IsTagVisibleToUser_WhenOwnerIsNotBanned_ReturnsTrue()
    {
        // Arrange
        var user = new ApplicationUser { Id = "user1", UserName = "active_user", IsBanned = false };
        var tag = new Tag { Name = "ActiveUserTag", OwnerId = user.Id, Owner = user };

        // Act & Assert
        Assert.True(tag.IsTagVisibleToUser());
    }

    [Fact]
    public void IsTagVisibleToUser_WhenOwnerIsBanned_ReturnsFalse()
    {
        // Arrange
        var bannedUser = new ApplicationUser { Id = "banned1", UserName = "banned_user", IsBanned = true };
        var tag = new Tag { Name = "BannedUserTag", OwnerId = bannedUser.Id, Owner = bannedUser };

        // Act & Assert
        Assert.False(tag.IsTagVisibleToUser());
    }

    [Fact]
    public void WhereVisibleToUser_FiltersOutTagsOwnedByBannedUsers()
    {
        // Arrange
        var bannedUser = new ApplicationUser { Id = "banned1", UserName = "banned_user", IsBanned = true };
        var activeUser = new ApplicationUser { Id = "active1", UserName = "active_user", IsBanned = false };

        var tags = new List<Tag>
        {
            new() { Id = 1, Name = "ActiveTag", OwnerId = activeUser.Id, Owner = activeUser },
            new() { Id = 2, Name = "BannedTag", OwnerId = bannedUser.Id, Owner = bannedUser },
            new() { Id = 3, Name = "SystemOrNullOwnerTag", OwnerId = "system", Owner = null! }
        }.AsQueryable();

        // Act
        var visibleTags = tags.WhereVisibleToUser().ToList();

        // Assert
        Assert.Contains(visibleTags, t => t.Id == 1);
        Assert.Contains(visibleTags, t => t.Id == 3);
        Assert.DoesNotContain(visibleTags, t => t.Id == 2);
    }
}