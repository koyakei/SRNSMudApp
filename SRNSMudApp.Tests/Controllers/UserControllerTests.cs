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

public class UserControllerTests
{
    private readonly Mock<IUserDataProvider> _userDataProviderMock = new();

    private UserController CreateController(ClaimsPrincipal? user = null)
    {
        var controller = new UserController(_userDataProviderMock.Object);
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
    public async Task GetUserDetail_EmptyUserId_ReturnsBadRequest()
    {
        // Arrange
        var controller = CreateController();

        // Act
        ActionResult<UserDetailDto> result = await controller.GetUserDetail(string.Empty, CancellationToken.None);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetUserDetail_UserNotFound_ReturnsNotFound()
    {
        // Arrange
        var controller = CreateController();
        _userDataProviderMock
            .Setup(p => p.GetUserDetailAsync("non-existent", It.IsAny<string?>()))
            .ReturnsAsync(new UserDetailPageData(null, [], []));

        // Act
        ActionResult<UserDetailDto> result = await controller.GetUserDetail("non-existent", CancellationToken.None);

        // Assert
        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetUserDetail_UserExists_ReturnsMappedDto()
    {
        // Arrange
        var controller = CreateController();
        var appUser = new ApplicationUser { Id = "user-123", UserName = "alice", IsBanned = false };
        var item = new Item
        {
            Id = 1,
            Content = "テスト投稿",
            OwnerId = "user-123",
            ItemKindJson = "{}",
            CreatedDate = DateTime.UtcNow,
            UpdatedDate = DateTime.UtcNow
        };
        var tag = new Tag
        {
            Id = 2,
            Name = "テストタグ",
            OwnerId = "user-123"
        };
        var pageData = new UserDetailPageData(
            User: appUser,
            UserTags: [tag],
            UserItems: [item],
            IsFollowing: true,
            FollowingCount: 5,
            FollowersCount: 10
        );

        _userDataProviderMock
            .Setup(p => p.GetUserDetailAsync("user-123", It.IsAny<string?>()))
            .ReturnsAsync(pageData);

        // Act
        ActionResult<UserDetailDto> result = await controller.GetUserDetail("user-123", CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<UserDetailDto>(okResult.Value);
        Assert.Equal("user-123", dto.UserId);
        Assert.Equal("alice", dto.UserName);
        Assert.True(dto.IsFollowing);
        Assert.Equal(5, dto.FollowingCount);
        Assert.Equal(10, dto.FollowersCount);
        Assert.Single(dto.Items);
        Assert.Single(dto.Tags);
    }

    [Fact]
    public async Task ToggleFollow_Unauthorized_ReturnsUnauthorized()
    {
        // Arrange
        var controller = CreateController(); // 未認証

        // Act
        ActionResult<ToggleFollowResponse> result = await controller.ToggleFollow("target-user", CancellationToken.None);

        // Assert
        Assert.IsType<UnauthorizedObjectResult>(result.Result);
    }

    [Fact]
    public async Task ToggleFollow_SelfFollow_ReturnsBadRequest()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "my-id")], "test"));
        var controller = CreateController(user);

        // Act
        ActionResult<ToggleFollowResponse> result = await controller.ToggleFollow("my-id", CancellationToken.None);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task ToggleFollow_Authorized_CallsService_ReturnsNewStatus()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "my-id")], "test"));
        var controller = CreateController(user);

        _userDataProviderMock
            .Setup(p => p.ToggleFollowUserAsync("my-id", "target-id"))
            .ReturnsAsync(true);

        // Act
        ActionResult<ToggleFollowResponse> result = await controller.ToggleFollow("target-id", CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var res = Assert.IsType<ToggleFollowResponse>(okResult.Value);
        Assert.True(res.IsFollowing);
    }

    [Fact]
    public async Task SearchUsers_ReturnsMappedUsers()
    {
        // Arrange
        var controller = CreateController();
        var users = new List<ApplicationUser>
        {
            new() { Id = "u1", UserName = "alice" },
            new() { Id = "u2", UserName = "bob" }
        };

        _userDataProviderMock
            .Setup(p => p.SearchUsersAsync("al", It.IsAny<CancellationToken>()))
            .ReturnsAsync(users);

        // Act
        ActionResult<IReadOnlyList<UserDto>> result = await controller.SearchUsers("al", CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dtos = Assert.IsAssignableFrom<IReadOnlyList<UserDto>>(okResult.Value);
        Assert.Equal(2, dtos.Count);
        Assert.Equal("alice", dtos[0].UserName);
    }

    [Fact]
    public async Task GetManagementUsers_ReturnsUsersWithRoleAndProtection()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "admin-1")], "test"));
        var controller = CreateController(user);

        var u1 = new ApplicationUser { Id = "admin-1", UserName = "admin1", IsBanned = false };
        var u2 = new ApplicationUser { Id = "user-2", UserName = "regular2", IsBanned = true };
        _userDataProviderMock
            .Setup(p => p.GetAllUsersAsync())
            .ReturnsAsync([u1, u2]);

        _userDataProviderMock
            .Setup(p => p.IsUserInRoleAsync(u1, "Admin"))
            .ReturnsAsync(true);
        _userDataProviderMock
            .Setup(p => p.IsUserInRoleAsync(u2, "Admin"))
            .ReturnsAsync(false);

        // Act
        ActionResult<IReadOnlyList<ManagedUserDto>> result =
            await controller.GetManagementUsers(CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dtos = Assert.IsAssignableFrom<IReadOnlyList<ManagedUserDto>>(okResult.Value);
        Assert.Equal(2, dtos.Count);
        Assert.True(dtos[0].IsAdmin);
        Assert.True(dtos[0].IsProtected); // ログイン中の管理者自身
        Assert.False(dtos[1].IsAdmin);
        Assert.True(dtos[1].IsBanned);
        Assert.False(dtos[1].IsProtected);
    }

    [Fact]
    public async Task SetUserAdmin_WhenProtected_ReturnsBadRequest()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "admin-1")], "test"));
        var controller = CreateController(user);

        var targetUser = new ApplicationUser { Id = "admin-1", UserName = "admin1" };
        _userDataProviderMock
            .Setup(p => p.FindUserByIdAsync("admin-1"))
            .ReturnsAsync(targetUser);

        // Act - 自身のAdmin剥奪を試行
        ActionResult<bool> result = await controller.SetUserAdmin("admin-1", new SetUserAdminRequest(false), CancellationToken.None);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task SetUserAdmin_WhenNormalUser_UpdatesRole()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "admin-1")], "test"));
        var controller = CreateController(user);

        var targetUser = new ApplicationUser { Id = "user-2", UserName = "user2" };
        _userDataProviderMock
            .Setup(p => p.FindUserByIdAsync("user-2"))
            .ReturnsAsync(targetUser);

        _userDataProviderMock
            .Setup(p => p.UpdateUserAdminRoleAsync("user-2", true))
            .ReturnsAsync(Microsoft.AspNetCore.Identity.IdentityResult.Success);

        // Act
        ActionResult<bool> result = await controller.SetUserAdmin("user-2", new SetUserAdminRequest(true), CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.True((bool)okResult.Value!);
    }

    [Fact]
    public async Task SetUserBan_WhenProtected_ReturnsBadRequest()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "admin-1")], "test"));
        var controller = CreateController(user);

        var targetUser = new ApplicationUser { Id = "admin-1", UserName = "admin1" };
        _userDataProviderMock
            .Setup(p => p.FindUserByIdAsync("admin-1"))
            .ReturnsAsync(targetUser);

        // Act - 自身へのBAN試行
        ActionResult<bool> result = await controller.SetUserBan("admin-1", new SetUserBanRequest(true), CancellationToken.None);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task SetUserBan_WhenNormalUser_UpdatesBan()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "admin-1")], "test"));
        var controller = CreateController(user);

        var targetUser = new ApplicationUser { Id = "user-2", UserName = "user2" };
        _userDataProviderMock
            .Setup(p => p.FindUserByIdAsync("user-2"))
            .ReturnsAsync(targetUser);

        _userDataProviderMock
            .Setup(p => p.SetUserBanStatusAsync("user-2", true))
            .ReturnsAsync(Microsoft.AspNetCore.Identity.IdentityResult.Success);

        // Act
        ActionResult<bool> result = await controller.SetUserBan("user-2", new SetUserBanRequest(true), CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.True((bool)okResult.Value!);
    }

    [Fact]
    public async Task GetConfirmedAccounts_ReturnsListOfUsersWithStatus()
    {
        // Arrange
        var controller = CreateController();
        var users = new List<ApplicationUser>
        {
            new() { Id = "u1", UserName = "alice", Email = "alice@example.com", EmailConfirmed = true },
            new() { Id = "u2", UserName = "bob", Email = "bob@example.com", EmailConfirmed = false }
        };

        _userDataProviderMock
            .Setup(p => p.GetAllUsersAsync())
            .ReturnsAsync(users);

        // Act
        ActionResult<IReadOnlyList<UserConfirmationDto>> result =
            await controller.GetConfirmedAccounts(CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dtos = Assert.IsAssignableFrom<IReadOnlyList<UserConfirmationDto>>(okResult.Value);
        Assert.Equal(2, dtos.Count);
        Assert.True(dtos[0].EmailConfirmed);
        Assert.False(dtos[1].EmailConfirmed);
    }

    [Fact]
    public async Task ToggleUserConfirmation_WhenUserNotFound_ReturnsNotFound()
    {
        // Arrange
        var controller = CreateController();
        _userDataProviderMock
            .Setup(p => p.FindUserByIdAsync("unknown"))
            .ReturnsAsync((ApplicationUser?)null);

        // Act
        ActionResult<bool> result = await controller.ToggleUserConfirmation("unknown", CancellationToken.None);

        // Assert
        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task ToggleUserConfirmation_WhenSuccessful_TogglesAndReturnsNewStatus()
    {
        // Arrange
        var controller = CreateController();
        var user = new ApplicationUser { Id = "u1", UserName = "alice", Email = "alice@example.com", EmailConfirmed = false };
        var updatedUser = new ApplicationUser { Id = "u1", UserName = "alice", Email = "alice@example.com", EmailConfirmed = true };

        _userDataProviderMock
            .SetupSequence(p => p.FindUserByIdAsync("u1"))
            .ReturnsAsync(user)
            .ReturnsAsync(updatedUser);

        _userDataProviderMock
            .Setup(p => p.ToggleUserEmailConfirmationAsync("u1"))
            .ReturnsAsync(Microsoft.AspNetCore.Identity.IdentityResult.Success);

        // Act
        ActionResult<bool> result = await controller.ToggleUserConfirmation("u1", CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.True((bool)okResult.Value!);
    }
}