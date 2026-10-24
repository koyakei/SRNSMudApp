namespace SRNSMudApp.Tests.Controllers;

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using Moq;

using SRNSMudApp.Client.Models.Api;
using SRNSMudApp.Controllers;
using SRNSMudApp.Data;
using SRNSMudApp.Services;

using Xunit;

public class UserGroupControllerTests
{
    private readonly Mock<IUserGroupDataProvider> _userGroupDataMock = new();

    private UserGroupController CreateController(ClaimsPrincipal? user = null)
    {
        var controller = new UserGroupController(_userGroupDataMock.Object);

        if (user != null)
        {
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user }
            };
        }

        return controller;
    }

    [Fact]
    public async Task GetUserGroups_ReturnsGroups()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-abc")], "test"));
        var controller = CreateController(user);
        var group = new UserGroup { Id = 1, Name = "Team A", Description = "Team A desc", OwnerId = "user-abc" };

        _userGroupDataMock
            .Setup(g => g.GetUserGroupsForUserAsync("user-abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync([group]);

        // Act
        ActionResult<IReadOnlyList<UserGroupDto>> result = await controller.GetUserGroups(CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var groups = Assert.IsAssignableFrom<IReadOnlyList<UserGroupDto>>(okResult.Value);
        Assert.Single(groups);
        Assert.Equal("Team A", groups[0].Name);
    }

    [Fact]
    public async Task GetManagedGroups_ReturnsManagedGroups()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-abc")], "test"));
        var controller = CreateController(user);
        var group = new UserGroup { Id = 1, Name = "Managed Team", Description = "Desc", OwnerId = "user-abc" };

        _userGroupDataMock
            .Setup(g => g.GetManagedGroupsAsync("user-abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync([group]);

        // Act
        ActionResult<IReadOnlyList<UserGroupDto>> result = await controller.GetManagedGroups(CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var groups = Assert.IsAssignableFrom<IReadOnlyList<UserGroupDto>>(okResult.Value);
        Assert.Single(groups);
        Assert.Equal("Managed Team", groups[0].Name);
    }

    [Fact]
    public async Task CreateUserGroup_Authorized_CreatesGroup()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-abc")], "test"));
        var controller = CreateController(user);
        var created = new UserGroup { Id = 10, Name = "New Team", Description = "Desc", OwnerId = "user-abc" };

        _userGroupDataMock
            .Setup(g => g.CreateUserGroupAsync("New Team", "Desc", "user-abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(created);

        // Act
        ActionResult<UserGroupDto> result = await controller.CreateUserGroup(new CreateUserGroupRequest("New Team", "Desc"), CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var groupDto = Assert.IsType<UserGroupDto>(okResult.Value);
        Assert.Equal(10, groupDto.Id);
        Assert.Equal("New Team", groupDto.Name);
    }

    [Fact]
    public async Task UpdateUserGroup_Owner_UpdatesGroup()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-abc")], "test"));
        var controller = CreateController(user);

        _userGroupDataMock
            .Setup(g => g.UpdateUserGroupAsync(10, "Updated", null, "user-abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        ActionResult result = await controller.UpdateUserGroup(10, new UpdateUserGroupRequest("Updated"), CancellationToken.None);

        // Assert
        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task DeleteUserGroup_Owner_DeletesGroup()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-abc")], "test"));
        var controller = CreateController(user);

        _userGroupDataMock
            .Setup(g => g.DeleteUserGroupAsync(10, "user-abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        ActionResult result = await controller.DeleteUserGroup(10, CancellationToken.None);

        // Assert
        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task AddMember_Owner_AddsMember()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-abc")], "test"));
        var controller = CreateController(user);

        _userGroupDataMock
            .Setup(g => g.AddMemberAsync(10, "user-xyz", "user-abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        ActionResult result = await controller.AddMember(10, new AddGroupMemberRequest("user-xyz"), CancellationToken.None);

        // Assert
        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task RemoveMember_Authorized_RemovesMember()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-abc")], "test"));
        var controller = CreateController(user);

        _userGroupDataMock
            .Setup(g => g.RemoveMemberAsync(10, "user-xyz", "user-abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        ActionResult result = await controller.RemoveMember(10, "user-xyz", CancellationToken.None);

        // Assert
        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task SearchUsers_ReturnsMatchingUsers()
    {
        // Arrange
        var userDataMock = new Mock<IUserDataProvider>();
        var user = new ApplicationUser { Id = "u-1", UserName = "Alice" };
        userDataMock
            .Setup(d => d.SearchUsersAsync("ali", It.IsAny<CancellationToken>()))
            .ReturnsAsync([user]);

        var controller = new UserGroupController(_userGroupDataMock.Object, userDataMock.Object);

        // Act
        ActionResult<IReadOnlyList<UserDto>> result = await controller.SearchUsers("ali", CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var users = Assert.IsAssignableFrom<IReadOnlyList<UserDto>>(okResult.Value);
        Assert.Single(users);
        Assert.Equal("Alice", users[0].UserName);
    }
}