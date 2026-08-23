#region

using System;
using System.Threading;
using System.Threading.Tasks;

using AngleSharp.Dom;

using Bunit;

using SRNSMudApp.Tests.TestSupport;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.User;
using SRNSMudApp.Data;
using Xunit;

#endregion

namespace SRNSMudApp.Tests.Components.User;

// BunitContextの継承をやめ、IAsyncDisposableを実装します
public class UserSearchTests : IAsyncDisposable
{
    private readonly BunitContext _ctx;

    public UserSearchTests()
    {
        _ctx = new BunitContext();

        // 継承元のプロパティではなく、_ctx のプロパティを使用するように変更
        _ = _ctx.Services.AddMudServices().AddSrnsComponentServices();
        // Since MudBlazor Popover requires JS, we need to mock it in bUnit or use Bunit.Web.JSInterop
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;

        DbContextOptions<ApplicationDbContext> options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("UserSearchTestDb")
            .Options;

        var dbContext = new ApplicationDbContext(options);
        _ = dbContext.Database.EnsureDeleted();
        _ = dbContext.Database.EnsureCreated();

        dbContext.Users.AddRange(
            new ApplicationUser { Id = "1", UserName = "TestUser1", NormalizedUserName = "TESTUSER1" },
            new ApplicationUser { Id = "2", UserName = "AdminUser", NormalizedUserName = "ADMINUSER" },
            new ApplicationUser { Id = "3", UserName = "GuestUser", NormalizedUserName = "GUESTUSER" }
        );
        _ = dbContext.SaveChanges();

        var mockDbFactory = new Mock<IDbContextFactory<ApplicationDbContext>>();
        _ = mockDbFactory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new ApplicationDbContext(options));

        _ = _ctx.Services.AddSingleton(mockDbFactory.Object);
    }

    // 非同期でBunitContextを破棄し、MudBlazorの非同期サービスの例外を防ぐ
    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }

    [Fact]
    public void UserSearch_Renders_Initially()
    {
        // Render ではなく、_ctx.Render<T>() を使用します
        IRenderedComponent<UserSearch> component = _ctx.Render<UserSearch>();
        Assert.NotNull(component);
        Assert.Contains("ユーザーを検索", component.Markup);
    }

    [Fact]
    public void UserSearch_CanSearchUsers_CaseInsensitive()
    {
        // Render ではなく、_ctx.Render<T>() を使用します
        IRenderedComponent<MudPopoverProvider> provider = _ctx.Render<MudPopoverProvider>();
        IRenderedComponent<UserSearch> searchComponent = _ctx.Render<UserSearch>();

        IElement input = searchComponent.Find("input");

        // Simulate typing lowercase but searching for uppercase
        input.Input("test");

        // The autocomplete dropdown is rendered inside MudPopoverProvider
        provider.WaitForState(
            () => provider.Markup.Contains("TestUser1") || provider.Markup.Contains("一致するユーザーが見つかりません"),
            TimeSpan.FromSeconds(3));

        Assert.Contains("TestUser1", provider.Markup);
    }

    /// <summary>
    ///     完全一致するユーザー名を入力すると、候補一覧にそのユーザーが表示されること。
    ///     （MudPopoverE2ETests.UserSearch_PopoverShouldAppear_WhenTyping の移行テスト。
    ///     ポップオーバーのCSS位置合わせではなく「入力→候補データのレンダリング」を検証する）
    /// </summary>
    [Fact]
    public void UserSearch_TypingFullName_ShowsCandidateInPopover()
    {
        IRenderedComponent<MudPopoverProvider> provider = _ctx.Render<MudPopoverProvider>();
        IRenderedComponent<UserSearch> searchComponent = _ctx.Render<UserSearch>();

        IElement input = searchComponent.Find("input");
        input.Input("testuser");

        provider.WaitForState(() => provider.Markup.Contains("TestUser1"), TimeSpan.FromSeconds(3));

        Assert.Contains("TestUser1", provider.Markup);
    }
}