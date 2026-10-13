using System.Security.Claims;

using Bunit;

using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web.Virtualization;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.Item;
using SRNSMudApp.Components.Pages;
using SRNSMudApp.Models;
using SRNSMudApp.Services;
using SRNSMudApp.Services.Dialogs;

namespace SRNSMudApp.Tests.Components.Pages;

/// <summary>
///     <see cref="Home" /> コンポーネントのPC向け機能（AddItem 表示・アイテム追加時コールバック）のテスト。
/// </summary>
public sealed class HomeDesktopTests : IAsyncLifetime
{
    private const string CurrentUserId = "test-user-id";
    private readonly BunitContext _ctx = new();
    private readonly Mock<IHomeDataProvider> _homeDataMock = new();
    private readonly Mock<ISystemTagEnsurer> _systemTagEnsurerMock = new();
    private readonly Mock<IDialogLauncher> _dialogLauncherMock = new();

    public HomeDesktopTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices().AddMockSrnsServices();

        _homeDataMock.Setup(d => d.GetFollowedTagIdsAsync(CurrentUserId))
            .ReturnsAsync([1]);
        _homeDataMock.Setup(d => d.GetTagsAndRelationsAsync())
            .ReturnsAsync((new List<global::SRNSMudApp.Data.Tag>(), new List<global::SRNSMudApp.Data.TagRelationToTag>()));
        _homeDataMock.Setup(d => d.LoadTimelineAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync(new HomeTimelinePage([], 0));

        var homeViewModel = new HomeViewModel(_homeDataMock.Object, _systemTagEnsurerMock.Object);
        _ = _ctx.Services.AddScoped(_ => homeViewModel);
        _ = _ctx.Services.AddScoped(_ => _dialogLauncherMock.Object);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _ctx.DisposeAsync().AsTask();

    [Fact]
    public void Home_WhenLoggedIn_RendersDesktopAddItem()
    {
        // Arrange
        Claim[] claims = [new(ClaimTypes.NameIdentifier, CurrentUserId), new(ClaimTypes.Name, "testuser")];
        var identity = new ClaimsIdentity(claims, "TestAuthType");
        var authState = new AuthenticationState(new ClaimsPrincipal(identity));
        _ = _ctx.Services.AddCascadingValue(_ => Task.FromResult(authState));
        _ = _ctx.Services.AddCascadingValue(_ => Breakpoint.Lg);
        _ = _ctx.Services.AddAuthorizationCore();

        // Act
        var cut = _ctx.Render<Home>();

        // Assert: AddItem コンポーネントが描画され、TextareaId="add-item-home" が設定されていること
        var addItem = cut.FindComponent<AddItem>();
        Assert.NotNull(addItem);
        Assert.Equal("add-item-home", addItem.Instance.TextareaId);
    }

    [Fact]
    public void Home_WhenNotLoggedIn_DoesNotRenderDesktopAddItem()
    {
        // Arrange
        var identity = new ClaimsIdentity(); // 未認証
        var authState = new AuthenticationState(new ClaimsPrincipal(identity));
        _ = _ctx.Services.AddCascadingValue(_ => Task.FromResult(authState));
        _ = _ctx.Services.AddCascadingValue(_ => Breakpoint.Lg);
        _ = _ctx.Services.AddAuthorizationCore();

        // Act
        var cut = _ctx.Render<Home>();

        // Assert: 未ログイン時は AddItem が描画されないこと
        Assert.Empty(cut.FindComponents<AddItem>());
    }

    [Fact]
    public async Task Home_WhenItemAdded_InvokesHandleItemAddedAsync()
    {
        // Arrange
        Claim[] claims = [new(ClaimTypes.NameIdentifier, CurrentUserId), new(ClaimTypes.Name, "testuser")];
        var identity = new ClaimsIdentity(claims, "TestAuthType");
        var authState = new AuthenticationState(new ClaimsPrincipal(identity));
        _ = _ctx.Services.AddCascadingValue(_ => Task.FromResult(authState));
        _ = _ctx.Services.AddCascadingValue(_ => Breakpoint.Lg);
        _ = _ctx.Services.AddAuthorizationCore();

        var cut = _ctx.Render<Home>();

        // Act
        await cut.InvokeAsync(() => cut.Instance.HandleItemAddedAsync());

        // Assert: FetchTagsAsync が呼ばれること
        _homeDataMock.Verify(d => d.GetTagsAndRelationsAsync(), Times.AtLeast(2));
    }

    [Fact]
    public void Home_WhenLoggedIn_TimelineDoesNotHaveFixedScrollContainer()
    {
        // Arrange
        Claim[] claims = [new(ClaimTypes.NameIdentifier, CurrentUserId), new(ClaimTypes.Name, "testuser")];
        var identity = new ClaimsIdentity(claims, "TestAuthType");
        var authState = new AuthenticationState(new ClaimsPrincipal(identity));
        _ = _ctx.Services.AddCascadingValue(_ => Task.FromResult(authState));
        _ = _ctx.Services.AddCascadingValue(_ => Breakpoint.Lg);
        _ = _ctx.Services.AddAuthorizationCore();

        // Act
        var cut = _ctx.Render<Home>();

        // Assert: タイムラインの Virtualize が独立したスクロール領域（overflow-y: auto や固定高さ 80vh）で囲まれておらず、
        // 画面全体でスクロールダウン（Twitterライク）できるようになっていることを確認
        Assert.DoesNotContain("overflow-y: auto", cut.Markup);
        Assert.DoesNotContain("80vh", cut.Markup);
        Assert.NotNull(cut.FindComponent<Virtualize<TimelineFeedGroup>>());
    }
}