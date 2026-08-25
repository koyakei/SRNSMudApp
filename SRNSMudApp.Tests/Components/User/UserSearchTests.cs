using AngleSharp.Dom;

using Bunit;

using Microsoft.EntityFrameworkCore;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.User;
using SRNSMudApp.Data;
using SRNSMudApp.Tests.TestSupport;

using Xunit;

namespace SRNSMudApp.Tests.Components.User;

[Collection(MsSqlCollection.Name)]
public class UserSearchTests : IAsyncLifetime
{
    private readonly MsSqlContainerFixture _fixture;
    private MsSqlTestDatabase _testDb = null!;
    private readonly BunitContext _ctx = new();

    public UserSearchTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _testDb = await MsSqlTestDatabase.CreateAsync(_fixture.ConnectionString, nameof(UserSearchTests));
        _ = _ctx.Services.AddMudServices().AddSrnsComponentServices();
        _ctx.Services.AddMsSqlDbFactory(_testDb.ConnectionString);
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;

        await using var dbContext = new ApplicationDbContext(_testDb.Options);
        dbContext.Users.AddRange(
            new ApplicationUser { Id = "1", UserName = "TestUser1", NormalizedUserName = "TESTUSER1" },
            new ApplicationUser { Id = "2", UserName = "AdminUser", NormalizedUserName = "ADMINUSER" },
            new ApplicationUser { Id = "3", UserName = "GuestUser", NormalizedUserName = "GUESTUSER" }
        );
        await dbContext.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
        await _testDb.DisposeAsync();
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