#region

using System.Security.Claims;

using AngleSharp.Dom;

using Bunit;
using Bunit.TestDoubles;

using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor.Services;

using SRNSMudApp.Components.User;
using SRNSMudApp.Data;

#endregion

namespace SRNSMudApp.Tests.Components.User;

public class UserManagementTests : TestContext
{
    public UserManagementTests()
    {
        _ = Services.AddMudServices();

        var authStateProviderMock = new Mock<AuthenticationStateProvider>();
        Claim[] claims =
        [
            new(ClaimTypes.NameIdentifier, "test-user-id"), new(ClaimTypes.Role, "Admin")
        ];
        var identity = new ClaimsIdentity(claims, "TestAuthType");
        var user = new ClaimsPrincipal(identity);
        _ = authStateProviderMock.Setup(x => x.GetAuthenticationStateAsync())
            .ReturnsAsync(new AuthenticationState(user));

        _ = Services.AddScoped(sp => authStateProviderMock.Object);
        // AuthorizeView needs AuthorizationService
        _ = Services.AddAuthorizationCore();

        var dbName = Guid.NewGuid().ToString();
        _ = Services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(dbName), ServiceLifetime.Scoped, ServiceLifetime.Singleton);
        _ = Services.AddDbContextFactory<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(dbName));

        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public async Task RendersUserManagement_And_TogglesAdminRole()
    {
        // Arrange
        // Setup UserManager mock and register before resolving any service
        var storeMock = new Mock<IUserStore<ApplicationUser>>();
        var userManagerMock = new Mock<UserManager<ApplicationUser>>(storeMock.Object, null!, null!, null!, null!,
            null!, null!, null!, null!);

        // Mock IsInRoleAsync to initially return false
        _ = userManagerMock.Setup(u => u.IsInRoleAsync(It.IsAny<ApplicationUser>(), "Admin")).ReturnsAsync(false);
        // Mock FindByIdAsync
        _ = userManagerMock.Setup(u => u.FindByIdAsync("user1"))
            .ReturnsAsync(new ApplicationUser { Id = "user1", UserName = "testuser@example.com" });
        // Mock AddToRoleAsync
        _ = userManagerMock.Setup(u => u.AddToRoleAsync(It.IsAny<ApplicationUser>(), "Admin"))
            .ReturnsAsync(IdentityResult.Success);

        _ = Services.AddScoped(sp => userManagerMock.Object);

        // Add a user to the in-memory db
        IDbContextFactory<ApplicationDbContext> dbFactory =
            Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        await using (ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync())
        {
            _ = dbContext.Users.Add(new ApplicationUser
            {
                Id = "user1", UserName = "testuser@example.com", Email = "testuser@example.com"
            });
            _ = await dbContext.SaveChangesAsync();
        }

        // Act
        IRenderedComponent<UserManagement> component = RenderComponent<UserManagement>();

        // Wait for users to load
        component.WaitForState(() => component.Markup.Contains("testuser@example.com"));

        // Assert: user is in the table
        Assert.Contains("testuser@example.com", component.Markup);

        // Act: find the switch and toggle it to true
        // MudSwitch uses an input type="checkbox"
        IElement mudSwitch = component.Find("input[type=\"checkbox\"]");
        mudSwitch.Change(true); // Toggle to true

        // Assert: AddToRoleAsync should have been called
        userManagerMock.Verify(u => u.AddToRoleAsync(It.IsAny<ApplicationUser>(), "Admin"), Times.Once);
    }
}