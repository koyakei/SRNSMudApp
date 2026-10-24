#region

using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Services;

#endregion

namespace SRNSMudApp.Tests.Services;

/// <summary>
///     <see cref="ItemVisibilityExtensions" /> のドメイン可視性判定に対する高速な分離単体テスト。
///     DB 接続不要な guard 句、管理者フラグ、非公開設定、匿名ユーザー拒否、投稿者自身による閲覧判定をテストする。
/// </summary>
public sealed class ItemVisibilityExtensionsUnitTests
{
    private readonly ApplicationDbContext _dummyContext = new(new DbContextOptions<ApplicationDbContext>());

    [Fact]
    public async Task IsItemVisibleToUserAsync_WhenItemIsNull_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            ItemVisibilityExtensions.IsItemVisibleToUserAsync(null!, _dummyContext, "user1"));
    }

    [Fact]
    public async Task IsItemVisibleToUserAsync_WhenContextIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var item = new Item { Content = "Test", OwnerId = "user1" };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            item.IsItemVisibleToUserAsync(null!, "user1"));
    }

    [Fact]
    public async Task IsItemVisibleToUserAsync_WhenAdmin_ReturnsTrueEvenIfAdminHiddenOrPrivate()
    {
        // Arrange: 管理者の場合、非公開かつ管理者非公開であっても閲覧可能
        var item = new Item
        {
            Content = "Admin Hidden Private",
            OwnerId = "author1",
            IsPrivate = true,
            IsAdminHidden = true
        };

        // Act
        bool visible = await item.IsItemVisibleToUserAsync(_dummyContext, "admin_user", isAdmin: true);

        // Assert
        Assert.True(visible);
    }

    [Fact]
    public async Task IsItemVisibleToUserAsync_WhenAdminHiddenAndNotAdmin_ReturnsFalseEvenForAuthor()
    {
        // Arrange: 管理者非公開の場合、投稿者本人であっても一般ユーザーなら閲覧不可
        var item = new Item
        {
            Content = "Admin Hidden Item",
            OwnerId = "author1",
            IsAdminHidden = true,
            IsPrivate = false
        };

        // Act
        bool visible = await item.IsItemVisibleToUserAsync(_dummyContext, "author1", isAdmin: false);

        // Assert
        Assert.False(visible);
    }

    [Fact]
    public async Task IsItemVisibleToUserAsync_WhenItemIsNotPrivate_ReturnsTrueForAnonymousOrAnyUser()
    {
        // Arrange: パブリックアイテム
        var item = new Item
        {
            Content = "Public Item",
            OwnerId = "author1",
            IsPrivate = false,
            IsAdminHidden = false
        };

        // Act & Assert
        Assert.True(await item.IsItemVisibleToUserAsync(_dummyContext, null, isAdmin: false));
        Assert.True(await item.IsItemVisibleToUserAsync(_dummyContext, string.Empty, isAdmin: false));
        Assert.True(await item.IsItemVisibleToUserAsync(_dummyContext, "stranger", isAdmin: false));
    }

    [Fact]
    public async Task IsItemVisibleToUserAsync_WhenItemIsPrivateAndUserIsNullOrEmpty_ReturnsFalse()
    {
        // Arrange: プライベートアイテムに未認証ユーザーがアクセス
        var item = new Item
        {
            Content = "Private Item",
            OwnerId = "author1",
            IsPrivate = true,
            IsAdminHidden = false
        };

        // Act & Assert
        Assert.False(await item.IsItemVisibleToUserAsync(_dummyContext, null, isAdmin: false));
        Assert.False(await item.IsItemVisibleToUserAsync(_dummyContext, string.Empty, isAdmin: false));
    }

    [Fact]
    public async Task IsItemVisibleToUserAsync_WhenItemIsPrivateAndUserMatchesOwner_ReturnsTrue()
    {
        // Arrange: プライベートアイテムに投稿者本人がアクセス
        var item = new Item
        {
            Content = "Private Item",
            OwnerId = "author1",
            IsPrivate = true,
            IsAdminHidden = false
        };

        // Act
        bool visible = await item.IsItemVisibleToUserAsync(_dummyContext, "author1", isAdmin: false);

        // Assert
        Assert.True(visible);
    }

    [Fact]
    public void WhereVisibleToUser_WhenQueryIsNull_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            ItemVisibilityExtensions.WhereVisibleToUser(null!, _dummyContext, "user1"));
    }

    [Fact]
    public void WhereVisibleToUser_WhenContextIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var query = new List<Item>().AsQueryable();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            query.WhereVisibleToUser(null!, "user1"));
    }
}