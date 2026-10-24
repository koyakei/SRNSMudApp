#region

using Moq;

using SRNSMudApp.Components.UserGroup;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;

using UserGroupEntity = SRNSMudApp.Data.UserGroup;

#endregion

namespace SRNSMudApp.Tests.Components.UserGroup;

public class UserGroupCreateEditViewModelTests
{
    private readonly Mock<IUserGroupDataProvider> _userGroupDataProviderMock = new();
    private readonly UserGroupCreateEditViewModel _sut;

    public UserGroupCreateEditViewModelTests()
    {
        _sut = new UserGroupCreateEditViewModel(_userGroupDataProviderMock.Object);
    }

    [Fact]
    public void Constructor_NullProvider_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new UserGroupCreateEditViewModel(null!));
    }

    [Fact]
    public void InitialState_IsConfiguredProperly()
    {
        Assert.Null(_sut.Group);
        Assert.Empty(_sut.CurrentUserId);
        Assert.Empty(_sut.Name);
        Assert.Empty(_sut.Description);
        Assert.False(_sut.IsSaving);
        Assert.False(_sut.IsEditMode);
        Assert.False(_sut.CanSave);
    }

    [Fact]
    public void Initialize_NewGroup_ConfiguresForCreate()
    {
        _sut.Initialize(null, "user-1");

        Assert.Null(_sut.Group);
        Assert.Equal("user-1", _sut.CurrentUserId);
        Assert.Empty(_sut.Name);
        Assert.Empty(_sut.Description);
        Assert.False(_sut.IsSaving);
        Assert.False(_sut.IsEditMode);
        Assert.False(_sut.CanSave);
    }

    [Fact]
    public void Initialize_ExistingGroup_ConfiguresForEdit()
    {
        var group = new UserGroupEntity
        {
            Id = 10,
            Name = "DevTeam",
            Description = "Developers",
            OwnerId = "user-1"
        };

        _sut.Initialize(group, "user-1");

        Assert.Same(group, _sut.Group);
        Assert.Equal("user-1", _sut.CurrentUserId);
        Assert.Equal("DevTeam", _sut.Name);
        Assert.Equal("Developers", _sut.Description);
        Assert.False(_sut.IsSaving);
        Assert.True(_sut.IsEditMode);
        Assert.True(_sut.CanSave);
    }

    [Theory]
    [InlineData("Group A", "user-1", true)]
    [InlineData("", "user-1", false)]
    [InlineData("   ", "user-1", false)]
    [InlineData("Group A", "", false)]
    [InlineData("Group A", "   ", false)]
    public void CanSave_ValidatesConditions(string name, string userId, bool expected)
    {
        _sut.Initialize(null, userId);
        _sut.Name = name;

        Assert.Equal(expected, _sut.CanSave);
    }

    [Fact]
    public async Task SaveAsync_WhenUserIdEmpty_ReturnsFailure()
    {
        _sut.Initialize(null, "");
        _sut.Name = "Valid Name";

        var result = await _sut.SaveAsync();

        Assert.True(result is Failure);
        if (result is Failure failure)
        {
            Assert.Contains("ユーザー情報が取得できませんでした", failure.ErrorMessage);
        }
    }

    [Fact]
    public async Task SaveAsync_WhenNameEmpty_ReturnsFailure()
    {
        _sut.Initialize(null, "user-1");
        _sut.Name = "   ";

        var result = await _sut.SaveAsync();

        Assert.True(result is Failure);
        if (result is Failure failure)
        {
            Assert.Contains("グループ名は必須です", failure.ErrorMessage);
        }
    }

    [Fact]
    public async Task SaveAsync_CreateMode_CallsCreateUserGroupAsyncAndReturnsSuccess()
    {
        // Arrange
        _sut.Initialize(null, "user-1");
        _sut.Name = "  New Group  ";
        _sut.Description = "  Some description  ";

        var createdGroup = new UserGroupEntity
        {
            Id = 55,
            Name = "New Group",
            Description = "Some description",
            OwnerId = "user-1"
        };

        _userGroupDataProviderMock.Setup(p => p.CreateUserGroupAsync(
                "New Group",
                "Some description",
                "user-1",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdGroup);

        // Act
        var result = await _sut.SaveAsync();

        // Assert
        Assert.True(result is Success<UserGroupEntity>);
        if (result is Success<UserGroupEntity> success)
        {
            Assert.Equal(55, success.Value.Id);
            Assert.Equal("New Group", success.Value.Name);
        }

        Assert.False(_sut.IsSaving);
        _userGroupDataProviderMock.Verify(p => p.CreateUserGroupAsync("New Group", "Some description", "user-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveAsync_EditMode_CallsUpdateUserGroupAsyncAndReturnsSuccess()
    {
        // Arrange
        var existing = new UserGroupEntity
        {
            Id = 22,
            Name = "Old Name",
            Description = "Old Desc",
            OwnerId = "user-1"
        };
        _sut.Initialize(existing, "user-1");
        _sut.Name = "  Updated Name  ";
        _sut.Description = "  Updated Desc  ";

        _userGroupDataProviderMock.Setup(p => p.UpdateUserGroupAsync(
                22,
                "Updated Name",
                "Updated Desc",
                "user-1",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _sut.SaveAsync();

        // Assert
        Assert.True(result is Success<UserGroupEntity>);
        if (result is Success<UserGroupEntity> success)
        {
            Assert.Equal(22, success.Value.Id);
            Assert.Equal("Updated Name", success.Value.Name);
            Assert.Equal("Updated Desc", success.Value.Description);
        }

        Assert.Equal("Updated Name", existing.Name);
        Assert.Equal("Updated Desc", existing.Description);
        Assert.False(_sut.IsSaving);
    }

    [Fact]
    public async Task SaveAsync_EditMode_WhenUpdateFails_ReturnsFailure()
    {
        // Arrange
        var existing = new UserGroupEntity
        {
            Id = 22,
            Name = "Old Name",
            OwnerId = "user-1"
        };
        _sut.Initialize(existing, "user-1");
        _sut.Name = "Updated Name";

        _userGroupDataProviderMock.Setup(p => p.UpdateUserGroupAsync(
                22,
                "Updated Name",
                "",
                "user-1",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _sut.SaveAsync();

        // Assert
        Assert.True(result is Failure);
        if (result is Failure failure)
        {
            Assert.Contains("グループの更新権限がないか、見つかりませんでした", failure.ErrorMessage);
        }

        Assert.False(_sut.IsSaving);
    }

    [Fact]
    public async Task SaveAsync_WhenAlreadySaving_ReturnsFailureWithDuplicateMessage()
    {
        _sut.Initialize(null, "user-1");
        _sut.Name = "Concurrent Group";

        var tcs = new TaskCompletionSource<UserGroupEntity>();
        _userGroupDataProviderMock.Setup(p => p.CreateUserGroupAsync("Concurrent Group", "", "user-1", It.IsAny<CancellationToken>()))
            .Returns(tcs.Task);

        var firstSave = _sut.SaveAsync();
        Assert.True(_sut.IsSaving);

        // 実行中の2回目の呼び出しは重複送信として拒否される
        var secondResult = await _sut.SaveAsync();
        Assert.True(secondResult is Failure);
        if (secondResult is Failure fail)
        {
            Assert.Equal(global::SRNSMudApp.Client.Services.DoubleSubmissionGuard.DuplicateSubmissionMessage, fail.ErrorMessage);
        }

        tcs.SetResult(new UserGroupEntity { Id = 1, Name = "Concurrent Group", OwnerId = "user-1" });
        var firstResult = await firstSave;
        Assert.True(firstResult is Success<UserGroupEntity>);
        Assert.False(_sut.IsSaving);
    }
}