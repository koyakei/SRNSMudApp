#region

using System;
using System.Security.Claims;
using System.Threading.Tasks;

using AngleSharp.Dom;

using Bunit;

using SRNSMudApp.Tests.TestSupport;

using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor.Services;

using SRNSMudApp.Components.User;
using SRNSMudApp.Data;
using Xunit;

#endregion

namespace SRNSMudApp.Tests.Components.User;

// BunitContextの継承をやめ、IAsyncDisposableを実装します
public class UserManagementTests : IAsyncDisposable
{
    private readonly BunitContext _ctx;

    public UserManagementTests()
    {
        _ctx = new BunitContext();

        // 認証モック・MudServices・アプリ側サービスは BunitTestSetup に集約
        _ = _ctx.Services.AddAuth("test-user-id", "Admin");
        _ = _ctx.Services.AddSrnsComponentServices();

        var dbName = Guid.NewGuid().ToString();
        _ = _ctx.Services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(dbName), ServiceLifetime.Scoped, ServiceLifetime.Singleton);
        _ = _ctx.Services.AddDbContextFactory<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(dbName));

        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
    }

    // 非同期でBunitContextを破棄し、MudBlazorの非同期サービスの例外を防ぐ
    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
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

        _ = _ctx.Services.AddScoped(sp => userManagerMock.Object);

        // Add a user to the in-memory db
        IDbContextFactory<ApplicationDbContext> dbFactory =
            _ctx.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        await using (ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync())
        {
            _ = dbContext.Users.Add(new ApplicationUser
            {
                Id = "user1", UserName = "testuser@example.com", Email = "testuser@example.com"
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

        // Act: find the switch and toggle it to true
        // MudSwitch uses an input type="checkbox"
        IElement mudSwitch = component.Find("input[type=\"checkbox\"]");
        mudSwitch.Change(true); // Toggle to true

        // Assert: AddToRoleAsync should have been called
        userManagerMock.Verify(u => u.AddToRoleAsync(It.IsAny<ApplicationUser>(), "Admin"), Times.Once);
    }
}