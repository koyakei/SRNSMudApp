// Components/User/UserDetailFollowTests.cs
#region

using System.Security.Claims;

using AngleSharp.Dom;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor.Services;

using SRNSMudApp.Components.User;
using SRNSMudApp.Data;
using SRNSMudApp.Services;

#endregion

namespace SRNSMudApp.Tests.Components.User;

/// <summary>
///     <see cref="UserDetail" /> コンポーネントにおけるユーザーフォロー機能の UI 単体テスト。
/// </summary>
public sealed class UserDetailFollowTests : IAsyncLifetime
{
    private const string ViewerUserId = "viewer-user-id";
    private const string TargetUserId = "target-user-id";

    private readonly BunitContext _ctx = new();
    private readonly Mock<IUserDataProvider> _userDataMock = new();

    public UserDetailFollowTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices().AddMockSrnsServices();
        _ = _ctx.Services.AddScoped(_ => _userDataMock.Object);

        Bunit.TestDoubles.BunitAuthorizationContext authorization = _ctx.AddAuthorization();
        authorization.SetAuthorized("testviewer");
        authorization.SetClaims(new Claim(ClaimTypes.NameIdentifier, ViewerUserId));
    }

    public Task InitializeAsync() => Task.CompletedTask;

    [Fact]
    public void FollowButton_WhenViewingAnotherUserProfile_AndNotFollowing_ShouldRenderFollowButton()
    {
        var targetUser = new ApplicationUser { Id = TargetUserId, UserName = "TargetUser", Email = "target@example.com" };
        var pageData = new UserDetailPageData(targetUser, [], [], IsFollowing: false, FollowingCount: 3, FollowersCount: 7);

        _ = _userDataMock.Setup(d => d.GetUserDetailAsync(TargetUserId, ViewerUserId))
            .ReturnsAsync(pageData);

        IRenderedComponent<UserDetail> component =
            _ctx.Render<UserDetail>(parameters => parameters.Add(p => p.UserId, TargetUserId));

        component.WaitForState(() => !component.Markup.Contains("mud-progress-circular"));

        IElement? button = component.FindAll("button")
            .FirstOrDefault(b => b.TextContent.Contains("フォロー") && !b.TextContent.Contains("フォロー解除"));

        Assert.NotNull(button);
        Assert.Contains("3", component.Markup);
        Assert.Contains("フォロー中", component.Markup);
        Assert.Contains("7", component.Markup);
        Assert.Contains("フォロワー", component.Markup);
    }

    [Fact]
    public void FollowButton_WhenAlreadyFollowing_ShouldRenderUnfollowButton()
    {
        var targetUser = new ApplicationUser { Id = TargetUserId, UserName = "TargetUser", Email = "target@example.com" };
        var pageData = new UserDetailPageData(targetUser, [], [], IsFollowing: true, FollowingCount: 1, FollowersCount: 2);

        _ = _userDataMock.Setup(d => d.GetUserDetailAsync(TargetUserId, ViewerUserId))
            .ReturnsAsync(pageData);

        IRenderedComponent<UserDetail> component =
            _ctx.Render<UserDetail>(parameters => parameters.Add(p => p.UserId, TargetUserId));

        component.WaitForState(() => !component.Markup.Contains("mud-progress-circular"));

        IElement? button = component.FindAll("button")
            .FirstOrDefault(b => b.TextContent.Contains("フォロー解除"));

        Assert.NotNull(button);
    }

    [Fact]
    public void FollowButton_WhenViewingOwnProfile_ShouldNotRenderFollowButton()
    {
        var myUser = new ApplicationUser { Id = ViewerUserId, UserName = "MyUser", Email = "my@example.com" };
        var pageData = new UserDetailPageData(myUser, [], [], IsFollowing: false, FollowingCount: 5, FollowersCount: 10);

        _ = _userDataMock.Setup(d => d.GetUserDetailAsync(ViewerUserId, ViewerUserId))
            .ReturnsAsync(pageData);

        IRenderedComponent<UserDetail> component =
            _ctx.Render<UserDetail>(parameters => parameters.Add(p => p.UserId, ViewerUserId));

        component.WaitForState(() => !component.Markup.Contains("mud-progress-circular"));

        IElement? followButton = component.FindAll("button")
            .FirstOrDefault(b => b.TextContent.Trim() == "フォロー" || b.TextContent.Trim() == "フォロー解除");

        Assert.Null(followButton);
    }

    [Fact]
    public async Task FollowButton_WhenClicked_ShouldCallToggleFollowUserAsync()
    {
        var targetUser = new ApplicationUser { Id = TargetUserId, UserName = "TargetUser", Email = "target@example.com" };
        var initialData = new UserDetailPageData(targetUser, [], [], IsFollowing: false, FollowingCount: 0, FollowersCount: 0);
        var updatedData = new UserDetailPageData(targetUser, [], [], IsFollowing: true, FollowingCount: 0, FollowersCount: 1);

        _ = _userDataMock.Setup(d => d.GetUserDetailAsync(TargetUserId, ViewerUserId))
            .ReturnsAsync(initialData);
        _ = _userDataMock.Setup(d => d.ToggleFollowUserAsync(ViewerUserId, TargetUserId))
            .ReturnsAsync(true);

        IRenderedComponent<UserDetail> component =
            _ctx.Render<UserDetail>(parameters => parameters.Add(p => p.UserId, TargetUserId));

        component.WaitForState(() => !component.Markup.Contains("mud-progress-circular"));

        IElement followButton = component.FindAll("button")
            .First(b => b.TextContent.Contains("フォロー") && !b.TextContent.Contains("フォロー解除"));

        _ = _userDataMock.Setup(d => d.GetUserDetailAsync(TargetUserId, ViewerUserId))
            .ReturnsAsync(updatedData);

        await component.InvokeAsync(() => followButton.Click());

        _userDataMock.Verify(d => d.ToggleFollowUserAsync(ViewerUserId, TargetUserId), Times.Once);
    }

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }
}

