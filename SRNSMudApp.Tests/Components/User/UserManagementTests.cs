using AngleSharp.Dom;

using Bunit;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.User;
using SRNSMudApp.Data;
using SRNSMudApp.Services;

namespace SRNSMudApp.Tests.Components.User;

public sealed class UserManagementTests : IAsyncLifetime
{
    private readonly BunitContext _ctx = new();
    private readonly Mock<IUserDataProvider> _userDataMock = new();
    private readonly Mock<UserManager<ApplicationUser>> _userManagerMock;

    public UserManagementTests()
    {
        _ctx.Services.AddMudServices().AddMockSrnsServices();
        _ctx.Services.AddAuth("test-user-id", "Admin");
        _ctx.Services.AddScoped(_ => _userDataMock.Object);

        var storeMock = new Mock<IUserStore<ApplicationUser>>();
        _userManagerMock = new Mock<UserManager<ApplicationUser>>(storeMock.Object, null!, null!, null!, null!,
            null!, null!, null!, null!);
        _ctx.Services.AddScoped(_ => _userManagerMock.Object);

        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Render<MudPopoverProvider>();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    [Fact]
    public void RendersUserManagement_And_TogglesAdminRole()
    {
        var targetUser = new ApplicationUser { Id = "user1", UserName = "testuser@example.com", Email = "testuser@example.com" };

        _ = _userDataMock.Setup(d => d.GetAllUsersAsync())
            .ReturnsAsync([targetUser]);

        _ = _userManagerMock.Setup(u => u.IsInRoleAsync(It.IsAny<ApplicationUser>(), "Admin")).ReturnsAsync(false);
        _ = _userManagerMock.Setup(u => u.FindByIdAsync("user1")).ReturnsAsync(targetUser);
        _ = _userManagerMock.Setup(u => u.AddToRoleAsync(It.IsAny<ApplicationUser>(), "Admin"))
            .ReturnsAsync(IdentityResult.Success);

        IRenderedComponent<UserManagement> component = _ctx.Render<UserManagement>();

        component.WaitForState(() => component.Markup.Contains("testuser@example.com"));
        Assert.Contains("testuser@example.com", component.Markup);

        IElement targetRow = component
            .FindAll("td")
            .First(td => td.TextContent.Contains("testuser@example.com"))
            .Closest("tr")!;
        IElement mudSwitch = targetRow.QuerySelector("input[type='checkbox']")!;
        mudSwitch.Change(true);

        component.WaitForAssertion(() => _userManagerMock.Verify(u => u.AddToRoleAsync(It.IsAny<ApplicationUser>(), "Admin"), Times.Once));
    }

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }
}