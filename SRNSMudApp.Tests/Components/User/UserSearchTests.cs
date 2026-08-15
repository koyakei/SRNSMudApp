#region

using AngleSharp.Dom;

using Bunit;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.User;
using SRNSMudApp.Data;

#endregion

namespace SRNSMudApp.Tests.Components.User;

public class UserSearchTests : TestContext
{
    public UserSearchTests()
    {
        _ = Services.AddMudServices();
        // Since MudBlazor Popover requires JS, we need to mock it in bUnit or use Bunit.Web.JSInterop
        JSInterop.Mode = JSRuntimeMode.Loose;

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

        _ = Services.AddSingleton(mockDbFactory.Object);
    }

    [Fact]
    public void UserSearch_Renders_Initially()
    {
        IRenderedComponent<UserSearch> component = RenderComponent<UserSearch>();
        Assert.NotNull(component);
        Assert.Contains("ユーザーを検索", component.Markup);
    }

    [Fact]
    public void UserSearch_CanSearchUsers_CaseInsensitive()
    {
        IRenderedComponent<MudPopoverProvider> provider = RenderComponent<MudPopoverProvider>();
        IRenderedComponent<UserSearch> searchComponent = RenderComponent<UserSearch>();

        IElement input = searchComponent.Find("input");

        // Simulate typing lowercase but searching for uppercase
        input.Input("test");

        // The autocomplete dropdown is rendered inside MudPopoverProvider
        provider.WaitForState(
            () => provider.Markup.Contains("TestUser1") || provider.Markup.Contains("一致するユーザーが見つかりません"),
            TimeSpan.FromSeconds(3));

        Assert.Contains("TestUser1", provider.Markup);
    }
}