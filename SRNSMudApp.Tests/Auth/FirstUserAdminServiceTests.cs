using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using SRNSMudApp.Data;
using SRNSMudApp.Services.Auth;

namespace SRNSMudApp.Tests.Auth;

/// <summary>
///     <see cref="FirstUserAdminService" /> の単体テスト。
///     EF Core の AnyAsync を含む結合部分は <see cref="ExternalLoginCallbackIntegrationTests" /> でカバーする。
///     ここでは FindByIdAsync が null を返すケース（ユーザー不存在）と
///     既に Admin ロールを持つケースのみを検証する。
/// </summary>
public class FirstUserAdminServiceTests
{
    private static Mock<UserManager<ApplicationUser>> BuildUserManager(
        ApplicationUser? findByIdResult = null,
        bool isInAdminRole = false)
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        var um = new Mock<UserManager<ApplicationUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        um.Setup(m => m.FindByIdAsync(It.IsAny<string>())).ReturnsAsync(findByIdResult);
        um.Setup(m => m.IsInRoleAsync(It.IsAny<ApplicationUser>(), "Admin")).ReturnsAsync(isInAdminRole);
        um.Setup(m => m.AddToRoleAsync(It.IsAny<ApplicationUser>(), "Admin"))
          .ReturnsAsync(IdentityResult.Success);

        // Users プロパティは EF Core DbSet のため非同期プロバイダを模倣する
        um.Setup(m => m.Users)
          .Returns(Array.Empty<ApplicationUser>().AsAsyncQueryable());

        return um;
    }

    private static Mock<RoleManager<IdentityRole>> BuildRoleManager(bool adminExists = true)
    {
        var store = new Mock<IRoleStore<IdentityRole>>();
        var rm = new Mock<RoleManager<IdentityRole>>(
            store.Object, null!, null!, null!, null!);

        rm.Setup(m => m.RoleExistsAsync("Admin")).ReturnsAsync(adminExists);
        rm.Setup(m => m.CreateAsync(It.IsAny<IdentityRole>())).ReturnsAsync(IdentityResult.Success);

        return rm;
    }

    [Fact]
    public async Task GrantAdminIfFirstUserAsync_WhenUserNotFound_DoesNotGrantRole()
    {
        // Arrange: FindByIdAsync が null を返す（ユーザーが DB に存在しない）
        var um = BuildUserManager(findByIdResult: null);
        var rm = BuildRoleManager();
        var service = new FirstUserAdminService(um.Object, rm.Object, NullLogger<FirstUserAdminService>.Instance);

        // Act
        await service.GrantAdminIfFirstUserAsync("non-existent-id");

        // Assert: ロール付与されない
        um.Verify(m => m.AddToRoleAsync(It.IsAny<ApplicationUser>(), "Admin"), Times.Never);
    }

    [Fact]
    public async Task GrantAdminIfFirstUserAsync_WhenAlreadyAdmin_DoesNotCallAddToRoleAgain()
    {
        // Arrange: ユーザーは存在し、既に Admin ロールを持つ
        var user = new ApplicationUser { Id = "user-1", UserName = "user@example.com" };
        var um = BuildUserManager(findByIdResult: user, isInAdminRole: true);
        var rm = BuildRoleManager();
        var service = new FirstUserAdminService(um.Object, rm.Object, NullLogger<FirstUserAdminService>.Instance);

        // Act
        await service.GrantAdminIfFirstUserAsync(user.Id);

        // Assert: AddToRoleAsync は呼ばれない
        um.Verify(m => m.AddToRoleAsync(It.IsAny<ApplicationUser>(), "Admin"), Times.Never);
    }
}