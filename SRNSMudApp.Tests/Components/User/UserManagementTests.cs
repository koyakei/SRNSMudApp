using AngleSharp.Dom;

using Bunit;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using SRNSMudApp.Components.User;
using SRNSMudApp.Data;
using SRNSMudApp.Tests.TestSupport;

using Xunit;

namespace SRNSMudApp.Tests.Components.User;

[Collection(MsSqlCollection.Name)]
public class UserManagementTests(MsSqlContainerFixture fixture) : IAsyncLifetime
{
    private MsSqlTestDatabase _testDb = null!;
    private readonly BunitContext _ctx = new();

    public async Task InitializeAsync()
    {
        _testDb = await MsSqlTestDatabase.CreateAsync(fixture.ConnectionString, nameof(UserManagementTests));

        _ctx.Services.AddAuth("test-user-id", "Admin");
        _ctx.Services.AddSrnsComponentServices();
        _ctx.Services.AddMsSqlDbFactory(_testDb.ConnectionString);

        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
    }

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
        await _testDb.DisposeAsync();
    }

    [Fact]
    public async Task RendersUserManagement_And_TogglesAdminRole()
    {
        // Arrange
        // Setup UserManager mock and register before resolving any service
        var storeMock = new Mock<IUserStore<ApplicationUser>>();
        var userManagerMock = new Mock<UserManager<ApplicationUser>>(storeMock.Object, null!, null!, null!, null!,
            null!, null!, null!, null!);

        // Mock IsInRoleAsync to initially return false for all users
        _ = userManagerMock.Setup(u => u.IsInRoleAsync(It.IsAny<ApplicationUser>(), "Admin")).ReturnsAsync(false);
        // Mock FindByIdAsync for the target user
        var targetUser = new ApplicationUser { Id = "user1", UserName = "testuser@example.com" };
        _ = userManagerMock.Setup(u => u.FindByIdAsync("user1")).ReturnsAsync(targetUser);
        // Mock FindByIdAsync for system_root (seeded by MsSqlTestDatabase) to avoid null early-return
        _ = userManagerMock.Setup(u => u.FindByIdAsync("system_root"))
            .ReturnsAsync(new ApplicationUser { Id = "system_root", UserName = "system_root" });
        // Mock AddToRoleAsync
        _ = userManagerMock.Setup(u => u.AddToRoleAsync(It.IsAny<ApplicationUser>(), "Admin"))
            .ReturnsAsync(IdentityResult.Success);

        _ = _ctx.Services.AddScoped(sp => userManagerMock.Object);

        // Add a user to the in-memory db
        IDbContextFactory<ApplicationDbContext> dbFactory =
            _ctx.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        await using (ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync())
        {
            _ = dbContext.Users.Add(new ApplicationUser
            {
                Id = "user1",
                UserName = "testuser@example.com",
                Email = "testuser@example.com"
            });
            _ = await dbContext.SaveChangesAsync();
        }

        // Act
        // Render ではなく、_ctx.Render<T>() を使用します
        IRenderedComponent<UserManagement> component = _ctx.Render<UserManagement>();

        // Wait for users to load
        component.WaitForState(() => component.Markup.Contains("testuser@example.com"));

        // Assert: user is in the table
        Assert.Contains("testuser@example.com", component.Markup);

        // Act: testuser@example.com の行を特定し、その行の MudSwitch (checkbox) を操作する。
        // system_root もテーブルに表示されるため Find("input[type=checkbox]") で先頭を取ると
        // system_root の行を操作してしまう可能性がある。Closest で行を絞り込む。
        IElement targetRow = component
            .FindAll("td")
            .First(td => td.TextContent.Contains("testuser@example.com"))
            .Closest("tr")!;
        IElement mudSwitch = targetRow.QuerySelector("input[type='checkbox']")!;
        mudSwitch.Change(true);

        // Assert: AddToRoleAsync should have been called exactly once for the target user
        component.WaitForAssertion(() => userManagerMock.Verify(u => u.AddToRoleAsync(It.IsAny<ApplicationUser>(), "Admin"), Times.Once));
    }
}