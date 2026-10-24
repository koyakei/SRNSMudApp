using Moq;

using SRNSMudApp.Components.Tag;
using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;

using TagEntity = SRNSMudApp.Data.Tag;
using UserGroupEntity = SRNSMudApp.Data.UserGroup;

namespace SRNSMudApp.Tests.Components.Tag;

/// <summary>
///     <see cref="TagEditViewModel"/> の単体テスト。
///     DOM 描画や bUnit を伴わずに高速かつ決定論的に初期化、バリデーション、タグ更新ロジックを検証する。
/// </summary>
public sealed class TagEditViewModelTests
{
    private readonly Mock<ITagCommandService> _tagCommandServiceMock = new();
    private readonly Mock<IUserGroupDataProvider> _userGroupDataMock = new();
    private readonly TagEditViewModel _sut;

    public TagEditViewModelTests()
    {
        _sut = new TagEditViewModel(_tagCommandServiceMock.Object, _userGroupDataMock.Object);
    }

    [Fact]
    public void InitialState_IsConfiguredProperly()
    {
        // Assert
        Assert.Null(_sut.Tag);
        Assert.Empty(_sut.EditName);
        Assert.Empty(_sut.EditContent);
        Assert.Empty(_sut.SelectedGroupIds);
        Assert.Empty(_sut.MyGroups);
        Assert.False(_sut.IsAdmin);
        Assert.False(_sut.CanSave);
    }

    [Fact]
    public async Task InitializeAsync_SetsPropertiesAndLoadsGroups()
    {
        // Arrange
        var tag = new TagEntity
        {
            Id = 10,
            Name = "OriginalName",
            Content = "OriginalContent",
            OwnerId = "owner-1",
            AutoApproveUserGroups = [new TagAutoApproveUserGroup { TagId = 10, UserGroupId = 3, OwnerId = "owner-1" }]
        };

        var groups = new List<UserGroupEntity>
        {
            new() { Id = 3, Name = "GroupA", OwnerId = "user-1" },
            new() { Id = 4, Name = "GroupB", OwnerId = "user-1" }
        };

        _userGroupDataMock.Setup(d => d.GetManagedGroupsAsync("user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(groups);

        // Act
        await _sut.InitializeAsync(tag, "user-1", true);

        // Assert
        Assert.Same(tag, _sut.Tag);
        Assert.Equal("OriginalName", _sut.EditName);
        Assert.Equal("OriginalContent", _sut.EditContent);
        Assert.True(_sut.IsAdmin);
        Assert.Contains(3, _sut.SelectedGroupIds);
        Assert.Equal(2, _sut.MyGroups.Count);
        Assert.True(_sut.CanSave);
    }

    [Fact]
    public async Task InitializeAsync_WhenCurrentUserIdNull_UsesTagOwnerId()
    {
        // Arrange
        var tag = new TagEntity
        {
            Id = 10,
            Name = "Tag",
            OwnerId = "tag-owner",
            AutoApproveUserGroups = []
        };

        var groups = new List<UserGroupEntity>
        {
            new() { Id = 1, Name = "Group1", OwnerId = "tag-owner" }
        };

        _userGroupDataMock.Setup(d => d.GetManagedGroupsAsync("tag-owner", It.IsAny<CancellationToken>()))
            .ReturnsAsync(groups);

        // Act
        await _sut.InitializeAsync(tag, null, false);

        // Assert
        Assert.Single(_sut.MyGroups);
        _userGroupDataMock.Verify(d => d.GetManagedGroupsAsync("tag-owner", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InitializeAsync_WhenGroupFetchThrows_HandlesExceptionGracefully()
    {
        // Arrange
        var tag = new TagEntity
        {
            Id = 10,
            Name = "Tag",
            OwnerId = "tag-owner",
            AutoApproveUserGroups = []
        };

        _userGroupDataMock.Setup(d => d.GetManagedGroupsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DB error"));

        // Act
        await _sut.InitializeAsync(tag, "user-1", false);

        // Assert
        Assert.Empty(_sut.MyGroups);
        Assert.Equal("Tag", _sut.EditName);
    }

    [Fact]
    public async Task SaveAsync_WhenTagNotInitialized_ReturnsFailure()
    {
        // Act
        var result = await _sut.SaveAsync();

        // Assert
        Assert.True(result is Failure);
        if (result is Failure failure)
        {
            Assert.Contains("指定されていません", failure.ErrorMessage);
        }
    }

    [Fact]
    public async Task SaveAsync_WhenEditNameEmpty_ReturnsFailure()
    {
        // Arrange
        var tag = new TagEntity { Id = 1, Name = "Name", OwnerId = "u1", AutoApproveUserGroups = [] };
        await _sut.InitializeAsync(tag, "u1", false);
        _sut.EditName = "   ";

        // Act
        var result = await _sut.SaveAsync();

        // Assert
        Assert.True(result is Failure);
        if (result is Failure failure)
        {
            Assert.Contains("タグ名は空にできません", failure.ErrorMessage);
        }

        _tagCommandServiceMock.Verify(
            s => s.UpdateTagAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<IReadOnlyCollection<int>?>(), It.IsAny<bool>()),
            Times.Never);
    }

    [Fact]
    public async Task SaveAsync_WhenValid_CallsCommandServiceSuccessfully()
    {
        // Arrange
        var tag = new TagEntity
        {
            Id = 42,
            Name = "OldName",
            Content = "OldContent",
            OwnerId = "u1",
            AutoAcceptIncomingTaggingRequests = true,
            AutoApproveUserGroups = []
        };
        await _sut.InitializeAsync(tag, "u1", true);

        _sut.EditName = "NewName";
        _sut.EditContent = "NewContent";
        _sut.SelectedGroupIds = [10, 20];

        _tagCommandServiceMock.Setup(s => s.UpdateTagAsync(
                42, "NewName", "NewContent", true, It.Is<IReadOnlyCollection<int>>(g => g.Count == 2), true, It.IsAny<bool?>(), It.IsAny<int?>()))
            .ReturnsAsync(true);

        // Act
        var result = await _sut.SaveAsync();

        // Assert
        Assert.True(result is Success<bool>);
        _tagCommandServiceMock.Verify(s => s.UpdateTagAsync(
            42, "NewName", "NewContent", true, It.IsAny<IReadOnlyCollection<int>>(), true, It.IsAny<bool?>(), It.IsAny<int?>()), Times.Once);
    }

    [Fact]
    public async Task SaveAsync_WhenServiceReturnsFalse_ReturnsLockFailure()
    {
        // Arrange
        var tag = new TagEntity { Id = 42, Name = "LockedTag", OwnerId = "u1", AutoApproveUserGroups = [] };
        await _sut.InitializeAsync(tag, "u1", false);

        _tagCommandServiceMock.Setup(s => s.UpdateTagAsync(
                42, "LockedTag", It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<IReadOnlyCollection<int>?>(), false, It.IsAny<bool?>(), It.IsAny<int?>()))
            .ReturnsAsync(false);

        // Act
        var result = await _sut.SaveAsync();

        // Assert
        Assert.True(result is Failure);
        if (result is Failure failure)
        {
            Assert.Contains("ロック", failure.ErrorMessage);
        }
    }

    [Fact]
    public async Task SaveAsync_WhenServiceThrows_ReturnsFailure()
    {
        // Arrange
        var tag = new TagEntity { Id = 42, Name = "Tag", OwnerId = "u1", AutoApproveUserGroups = [] };
        await _sut.InitializeAsync(tag, "u1", false);

        _tagCommandServiceMock.Setup(s => s.UpdateTagAsync(
                42, "Tag", It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<IReadOnlyCollection<int>?>(), false, It.IsAny<bool?>(), It.IsAny<int?>()))
            .ThrowsAsync(new InvalidOperationException("Database deadlock"));

        // Act
        var result = await _sut.SaveAsync();

        // Assert
        Assert.True(result is Failure);
        if (result is Failure failure)
        {
            Assert.Contains("Database deadlock", failure.ErrorMessage);
        }
    }

    [Fact]
    public async Task SaveAsync_WithPrivacySettings_PassesPrivacyParametersToCommandService()
    {
        // Arrange
        var tag = new TagEntity { Id = 50, Name = "PrivateTag", OwnerId = "u1", AutoApproveUserGroups = [] };
        await _sut.InitializeAsync(tag, "u1", false);
        _sut.IsPrivate = true;
        _sut.SelectedTargetGroupId = 77;

        _tagCommandServiceMock.Setup(s => s.UpdateTagAsync(
                50, "PrivateTag", It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<IReadOnlyCollection<int>?>(), false, true, 77))
            .ReturnsAsync(true);

        // Act
        var result = await _sut.SaveAsync();

        // Assert
        Assert.True(result is Success<bool>);
        _tagCommandServiceMock.Verify(s => s.UpdateTagAsync(
            50, "PrivateTag", It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<IReadOnlyCollection<int>?>(), false, true, 77), Times.Once);
    }

    [Fact]
    public async Task SaveAsync_WhenDuplicateSubmission_ReturnsFailureWithDuplicateMessage()
    {
        var tag = new TagEntity { Id = 60, Name = "TagName", Content = "Desc", OwnerId = "u1", AutoApproveUserGroups = [] };
        await _sut.InitializeAsync(tag, "u1", false);

        _tagCommandServiceMock.Setup(s => s.UpdateTagAsync(
                60, "TagName", "Desc", It.IsAny<bool>(), It.IsAny<IReadOnlyCollection<int>?>(), false, false, null))
            .ReturnsAsync(true);

        var first = await _sut.SaveAsync();
        Assert.True(first is Success<bool>);

        // 2回目の保存（直近の同一操作）は重複として抑止される
        var second = await _sut.SaveAsync();
        Assert.True(second is Failure);
        if (second is Failure fail)
        {
            Assert.Equal(global::SRNSMudApp.Client.Services.DoubleSubmissionGuard.DuplicateSubmissionMessage, fail.ErrorMessage);
        }
    }
}