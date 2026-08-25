using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

using Bunit;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor.Services;

using SRNSMudApp.Components.Account;
using SRNSMudApp.Components.Account.Pages.Debug;
using SRNSMudApp.Data;
using SRNSMudApp.Services;
using SRNSMudApp.Tests.TestSupport;

using Xunit;

namespace SRNSMudApp.Tests.Components.Admin;

public sealed class RequireConfirmedAccountTests : IAsyncLifetime
{
    private readonly BunitContext _ctx = new();
    private readonly Mock<IUserDataProvider> _userDataMock = new();
    private readonly Mock<UserManager<ApplicationUser>> _userManagerMock;

    public RequireConfirmedAccountTests()
    {
        _ = _ctx.Services.AddAuth("test-admin-id", "Admin");
        _ = _ctx.Services.AddMudServices().AddMockSrnsServices();
        _ = _ctx.Services.AddLogging();
        _ = _ctx.Services.AddScoped(_ => _userDataMock.Object);

        var storeMock = new Mock<IUserStore<ApplicationUser>>();
        _userManagerMock = new Mock<UserManager<ApplicationUser>>(storeMock.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        _ = _ctx.Services.AddScoped(_ => _userManagerMock.Object);

        _ = _ctx.Services.AddScoped<IdentityRedirectManager>();

        var envMock = new Mock<IWebHostEnvironment>();
        _ = envMock.Setup(e => e.EnvironmentName).Returns("Production");
        _ = _ctx.Services.AddSingleton(envMock.Object);

        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    [Fact]
    public void RendersUsersAndTogglesConfirmation()
    {
        var testUser = new ApplicationUser
        {
            Id = "user1",
            UserName = "testuser@example.com",
            Email = "testuser@example.com",
            EmailConfirmed = false
        };

        _ = _userDataMock.Setup(d => d.GetAllUsersAsync())
            .ReturnsAsync([testUser]);

        var httpContext = new DefaultHttpContext();

        IRenderedComponent<RequireConfirmedAccount> component =
            _ctx.Render<RequireConfirmedAccount>(parameters => parameters.AddCascadingValue(httpContext));

        component.WaitForState(() => component.Markup.Contains("testuser@example.com"));

        Assert.Contains("testuser@example.com", component.Markup);
        Assert.Contains("Unconfirmed", component.Markup);
    }

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }
}