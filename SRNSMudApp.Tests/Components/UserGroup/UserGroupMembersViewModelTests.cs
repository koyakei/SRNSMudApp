#region

using Moq;

using SRNSMudApp.Components.UserGroup;
using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;

using UserGroupEntity = SRNSMudApp.Data.UserGroup;

#endregion

namespace SRNSMudApp.Tests.Components.UserGroup;

public class UserGroupMembersViewModelTests
{
    private readonly Mock<IUserGroupDataProvider> _userGroupDataProviderMock = new();
    private readonly Mock<IUserDataProvider> _userDataProviderMock = new();
    private readonly UserGroupMembersViewModel _sut;

    public UserGroupMembersViewModelTests()
    {
        _sut = new UserGroupMembersViewModel(
            _userGroupDataProviderMock.Object,
            _userDataProviderMock.Object);
    }

    [Fact]
    public void Constructor_NullArguments_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new UserGroupMembersViewModel(null!, _userDataProviderMock.Object));
        Assert.Throws<ArgumentNullException>(() => new UserGroupMembersViewModel(_userGroupDataProviderMock.Object, null!));
    }

    [Fact]
    public void InitialState_IsConfiguredProperly()
    {
        Assert.Null(_sut.Group);
        Assert.Empty(_sut.CurrentUserId);
        Assert.Empty(_sut.Members);
        Assert.Null(_sut.SelectedUser);
        Assert.True(_sut.IsLoading);
        Assert.False(_sut.IsAdding);
        Assert.False(_sut.IsRemoving);
        Assert.False(_sut.IsOwner);
        Assert.False(_sut.CanAddMember);
    }

    [Fact]
    public async Task InitializeAsync_NullGroup_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _sut.InitializeAsync(null!, "user-1"));
    }

    [Fact]
    public async Task InitializeAsync_LoadsAndSortsMembers_OwnerFirst()
    {
        // Arrange
        var group = new UserGroupEntity { Id = 1, Name = "Alpha", OwnerId = "owner-1" };
        var member1 = new UserGroupMember
        {
            Id = 1,
            UserId = "user-2",
            OwnerId = "owner-1",
            CreatedDate = new DateTime(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            User = new ApplicationUser { Id = "user-2", UserName = "Bob" }
        };
        var member2 = new UserGroupMember
        {
            Id = 2,
            UserId = "owner-1",
            OwnerId = "owner-1",
            CreatedDate = new DateTime(2025, 1, 3, 0, 0, 0, DateTimeKind.Utc),
            User = new ApplicationUser { Id = "owner-1", UserName = "Alice" }
        };
        var groupWithMembers = new UserGroupEntity
        {
            Id = 1,
            Name = "Alpha",
            OwnerId = "owner-1",
            Members = [member1, member2]
        };

        _userGroupDataProviderMock.Setup(p => p.GetUserGroupByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(groupWithMembers);

        // Act
        await _sut.InitializeAsync(group, "owner-1");

        // Assert
        Assert.Same(group, _sut.Group);
        Assert.Equal("owner-1", _sut.CurrentUserId);
        Assert.True(_sut.IsOwner);
        Assert.False(_sut.IsLoading);
        Assert.Equal(2, _sut.Members.Count);
        // オーナーが先頭に来ること
        Assert.Equal("owner-1", _sut.Members[0].UserId);
        Assert.Equal("user-2", _sut.Members[1].UserId);
    }

    [Fact]
    public async Task SearchUsersAsync_WhenEmptyQuery_ReturnsEmpty()
    {
        var result = await _sut.SearchUsersAsync("");
        Assert.Empty(result);
    }

    [Fact]
    public async Task SearchUsersAsync_FiltersOutExistingMembers()
    {
        // Arrange
        var group = new UserGroupEntity { Id = 1, Name = "Alpha", OwnerId = "owner-1" };
        var existingMember = new UserGroupMember
        {
            Id = 1,
            UserId = "user-existing",
            OwnerId = "owner-1",
            User = new ApplicationUser { Id = "user-existing", UserName = "ExistingUser" }
        };
        group.Members = [existingMember];
        _userGroupDataProviderMock.Setup(p => p.GetUserGroupByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(group);

        await _sut.InitializeAsync(group, "owner-1");

        var candidates = new List<ApplicationUser>
        {
            new() { Id = "user-existing", UserName = "ExistingUser" },
            new() { Id = "user-new", UserName = "NewUser" }
        };

        _userDataProviderMock.Setup(u => u.SearchUsersAsync("user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidates);

        // Act
        var result = (await _sut.SearchUsersAsync("user")).ToList();

        // Assert
        Assert.Single(result);
        Assert.Equal("user-new", result[0].Id);
    }

    [Fact]
    public async Task AddMemberAsync_WhenNoGroup_ReturnsFailure()
    {
        _sut.SelectedUser = new ApplicationUser { Id = "u1" };
        var result = await _sut.AddMemberAsync();

        Assert.True(result is Failure);
        if (result is Failure failure)
        {
            Assert.Contains("グループが指定されていません", failure.ErrorMessage);
        }
    }

    [Fact]
    public async Task AddMemberAsync_WhenNoSelectedUser_ReturnsFailure()
    {
        var group = new UserGroupEntity { Id = 1, Name = "Alpha", OwnerId = "owner-1" };
        _userGroupDataProviderMock.Setup(p => p.GetUserGroupByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(group);
        await _sut.InitializeAsync(group, "owner-1");

        _sut.SelectedUser = null;
        var result = await _sut.AddMemberAsync();

        Assert.True(result is Failure);
        if (result is Failure failure)
        {
            Assert.Contains("追加するユーザーを選択してください", failure.ErrorMessage);
        }
    }

    [Fact]
    public async Task AddMemberAsync_WhenNotOwner_ReturnsFailure()
    {
        var group = new UserGroupEntity { Id = 1, Name = "Alpha", OwnerId = "owner-1" };
        _userGroupDataProviderMock.Setup(p => p.GetUserGroupByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(group);
        await _sut.InitializeAsync(group, "not-the-owner");

        _sut.SelectedUser = new ApplicationUser { Id = "u2", UserName = "Bob" };
        var result = await _sut.AddMemberAsync();

        Assert.True(result is Failure);
        if (result is Failure failure)
        {
            Assert.Contains("メンバーを追加する権限がありません", failure.ErrorMessage);
        }
    }

    [Fact]
    public async Task AddMemberAsync_WhenValid_CallsAddMemberAsyncAndReloads()
    {
        // Arrange
        var group = new UserGroupEntity { Id = 1, Name = "Alpha", OwnerId = "owner-1" };
        _userGroupDataProviderMock.Setup(p => p.GetUserGroupByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(group);
        await _sut.InitializeAsync(group, "owner-1");

        var userToAdd = new ApplicationUser { Id = "u2", UserName = "Bob" };
        _sut.SelectedUser = userToAdd;

        _userGroupDataProviderMock.Setup(p => p.AddMemberAsync(1, "u2", "owner-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _sut.AddMemberAsync();

        // Assert
        Assert.True(result is Success<bool>);
        Assert.Null(_sut.SelectedUser);
        Assert.False(_sut.IsAdding);
        _userGroupDataProviderMock.Verify(p => p.AddMemberAsync(1, "u2", "owner-1", It.IsAny<CancellationToken>()), Times.Once);
        _userGroupDataProviderMock.Verify(p => p.GetUserGroupByIdAsync(1, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task AddMemberAsync_WhenProviderFails_ReturnsFailure()
    {
        // Arrange
        var group = new UserGroupEntity { Id = 1, Name = "Alpha", OwnerId = "owner-1" };
        _userGroupDataProviderMock.Setup(p => p.GetUserGroupByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(group);
        await _sut.InitializeAsync(group, "owner-1");

        _sut.SelectedUser = new ApplicationUser { Id = "u2", UserName = "Bob" };
        _userGroupDataProviderMock.Setup(p => p.AddMemberAsync(1, "u2", "owner-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _sut.AddMemberAsync();

        // Assert
        Assert.True(result is Failure);
        if (result is Failure failure)
        {
            Assert.Contains("メンバーの追加に失敗しました", failure.ErrorMessage);
        }

        Assert.False(_sut.IsAdding);
    }

    [Fact]
    public async Task RemoveMemberAsync_WhenNoGroup_ReturnsFailure()
    {
        var result = await _sut.RemoveMemberAsync("u1");

        Assert.True(result is Failure);
        if (result is Failure failure)
        {
            Assert.Contains("グループが指定されていません", failure.ErrorMessage);
        }
    }

    [Fact]
    public async Task RemoveMemberAsync_WhenUserIdEmpty_ReturnsFailure()
    {
        var group = new UserGroupEntity { Id = 1, Name = "Alpha", OwnerId = "owner-1" };
        _userGroupDataProviderMock.Setup(p => p.GetUserGroupByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(group);
        await _sut.InitializeAsync(group, "owner-1");

        var result = await _sut.RemoveMemberAsync("   ");

        Assert.True(result is Failure);
        if (result is Failure failure)
        {
            Assert.Contains("削除対象ユーザーが指定されていません", failure.ErrorMessage);
        }
    }

    [Fact]
    public async Task RemoveMemberAsync_WhenValid_CallsRemoveMemberAsyncAndReloads()
    {
        // Arrange
        var group = new UserGroupEntity { Id = 1, Name = "Alpha", OwnerId = "owner-1" };
        _userGroupDataProviderMock.Setup(p => p.GetUserGroupByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(group);
        await _sut.InitializeAsync(group, "owner-1");

        _userGroupDataProviderMock.Setup(p => p.RemoveMemberAsync(1, "u2", "owner-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _sut.RemoveMemberAsync("u2");

        // Assert
        Assert.True(result is Success<bool>);
        Assert.False(_sut.IsRemoving);
        _userGroupDataProviderMock.Verify(p => p.RemoveMemberAsync(1, "u2", "owner-1", It.IsAny<CancellationToken>()), Times.Once);
        _userGroupDataProviderMock.Verify(p => p.GetUserGroupByIdAsync(1, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task RemoveMemberAsync_WhenProviderFails_ReturnsFailure()
    {
        // Arrange
        var group = new UserGroupEntity { Id = 1, Name = "Alpha", OwnerId = "owner-1" };
        _userGroupDataProviderMock.Setup(p => p.GetUserGroupByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(group);
        await _sut.InitializeAsync(group, "owner-1");

        _userGroupDataProviderMock.Setup(p => p.RemoveMemberAsync(1, "u2", "owner-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _sut.RemoveMemberAsync("u2");

        // Assert
        Assert.True(result is Failure);
        if (result is Failure failure)
        {
            Assert.Contains("メンバーの削除に失敗しました", failure.ErrorMessage);
        }

        Assert.False(_sut.IsRemoving);
    }

    [Fact]
    public async Task InitializeAsync_WithGroupId_LoadsAndSortsMembers()
    {
        // Arrange
        var group = new UserGroupEntity
        {
            Id = 10,
            Name = "Beta",
            OwnerId = "owner-10",
            Members =
            [
                new UserGroupMember { UserId = "m1", OwnerId = "owner-10", CreatedDate = DateTime.UtcNow.AddMinutes(5) },
                new UserGroupMember { UserId = "owner-10", OwnerId = "owner-10", CreatedDate = DateTime.UtcNow }
            ]
        };
        _userGroupDataProviderMock.Setup(p => p.GetUserGroupByIdAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(group);

        // Act
        await _sut.InitializeAsync(10, "owner-10");

        // Assert
        Assert.Equal(group, _sut.Group);
        Assert.Equal("owner-10", _sut.CurrentUserId);
        Assert.True(_sut.IsOwner);
        Assert.Equal(2, _sut.Members.Count);
        Assert.Equal("owner-10", _sut.Members[0].UserId);
        Assert.False(_sut.IsLoading);
    }

    [Fact]
    public async Task InitializeAsync_WithGroupId_WhenNotFound_SetsNullGroup()
    {
        // Arrange
        _userGroupDataProviderMock.Setup(p => p.GetUserGroupByIdAsync(99, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserGroupEntity?)null);

        // Act
        await _sut.InitializeAsync(99, "user-1");

        // Assert
        Assert.Null(_sut.Group);
        Assert.Empty(_sut.Members);
        Assert.False(_sut.IsOwner);
        Assert.False(_sut.IsLoading);
    }

    [Fact]
    public async Task CanAddMember_WhenSelectedUserProvidedAndIsOwner_ReturnsTrue()
    {
        // Arrange
        var group = new UserGroupEntity { Id = 1, Name = "Alpha", OwnerId = "owner-1" };
        _userGroupDataProviderMock.Setup(p => p.GetUserGroupByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(group);

        await _sut.InitializeAsync(group, "owner-1");
        _sut.SelectedUser = new ApplicationUser { Id = "u2", UserName = "Bob" };

        // Assert
        Assert.True(_sut.CanAddMember);
    }

    [Fact]
    public async Task CanAddMember_WhenNotOwner_ReturnsFalseEvenWithSelectedUser()
    {
        // Arrange
        var group = new UserGroupEntity { Id = 1, Name = "Alpha", OwnerId = "owner-1" };
        _userGroupDataProviderMock.Setup(p => p.GetUserGroupByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(group);

        await _sut.InitializeAsync(group, "non-owner-user");
        _sut.SelectedUser = new ApplicationUser { Id = "u2", UserName = "Bob" };

        // Assert
        Assert.False(_sut.CanAddMember);
    }

    [Fact]
    public async Task CanAddMember_WhenSelectedUserIsNull_ReturnsFalse()
    {
        // Arrange
        var group = new UserGroupEntity { Id = 1, Name = "Alpha", OwnerId = "owner-1" };
        _userGroupDataProviderMock.Setup(p => p.GetUserGroupByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(group);

        await _sut.InitializeAsync(group, "owner-1");
        _sut.SelectedUser = null;

        // Assert
        Assert.False(_sut.CanAddMember);
    }

    [Fact]
    public async Task AddMemberAsync_WhenAlreadyAdding_ReturnsFailureWithDuplicateSubmissionMessage()
    {
        // Arrange
        var group = new UserGroupEntity { Id = 1, Name = "Alpha", OwnerId = "owner-1" };
        _userGroupDataProviderMock.Setup(p => p.GetUserGroupByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(group);

        await _sut.InitializeAsync(group, "owner-1");
        _sut.SelectedUser = new ApplicationUser { Id = "u2", UserName = "Bob" };

        var tcs = new TaskCompletionSource<bool>();
        _userGroupDataProviderMock.Setup(p => p.AddMemberAsync(1, "u2", "owner-1", It.IsAny<CancellationToken>()))
            .Returns(tcs.Task);

        // Act: 最初の追加操作を開始
        var firstTask = _sut.AddMemberAsync();
        Assert.True(_sut.IsAdding);

        // 二重追加操作を実行
        var secondResult = await _sut.AddMemberAsync();

        // Assert: 二重追加は即座にFailureとなり重複メッセージを返す
        Assert.True(secondResult is Failure);
        if (secondResult is Failure failure)
        {
            Assert.Equal(SRNSMudApp.Client.Services.DoubleSubmissionGuard.DuplicateSubmissionMessage, failure.ErrorMessage);
        }

        tcs.SetResult(true);
        var firstResult = await firstTask;
        Assert.True(firstResult is Success<bool>);
        Assert.False(_sut.IsAdding);

        _userGroupDataProviderMock.Verify(p => p.AddMemberAsync(1, "u2", "owner-1", It.IsAny<CancellationToken>()), Times.Once);
    }
}