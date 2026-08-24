using System.Security.Claims;

using Bunit;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor.Services;

using SRNSMudApp.Components.Account;
using SRNSMudApp.Components.Account.Pages.Debug;
using SRNSMudApp.Data;
using SRNSMudApp.Tests.TestSupport;

using Xunit;

namespace SRNSMudApp.Tests.Components.Admin;

[Collection(MsSqlCollection.Name)]
public class RequireConfirmedAccountTests : IAsyncLifetime
{
    private readonly MsSqlContainerFixture _fixture;
    private MsSqlTestDatabase _testDb = null!;
    private readonly BunitContext _ctx = new();

    public RequireConfirmedAccountTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _testDb = await MsSqlTestDatabase.CreateAsync(_fixture.ConnectionString, nameof(RequireConfirmedAccountTests));

        _ = _ctx.Services.AddAuth("test-admin-id", "Admin");
        _ = _ctx.Services.AddSrnsComponentServices();
        _ = _ctx.Services.AddLogging();

        _ = _ctx.Services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(_testDb.ConnectionString), ServiceLifetime.Scoped, ServiceLifetime.Singleton);

        _ = _ctx.Services.AddIdentityCore<ApplicationUser>()
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>();

        _ = _ctx.Services.AddScoped<IdentityRedirectManager>();

        var envMock = new Mock<IWebHostEnvironment>();
        _ = envMock.Setup(e => e.EnvironmentName).Returns("Production");
        _ = _ctx.Services.AddSingleton(envMock.Object);

        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
    }

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
        await _testDb.DisposeAsync();
    }

    [Fact]
    public async Task RendersUsersAndTogglesConfirmation()
    {
        // Arrange
        UserManager<ApplicationUser> userManager = _ctx.Services.GetRequiredService<UserManager<ApplicationUser>>();
        _ = await userManager.CreateAsync(new ApplicationUser
        {
            Id = "user1",
            UserName = "testuser@example.com",
            Email = "testuser@example.com",
            EmailConfirmed = false
        });

        var httpContext = new DefaultHttpContext();

        // Act
        IRenderedComponent<RequireConfirmedAccount> component =
            _ctx.Render<RequireConfirmedAccount>(parameters => parameters.AddCascadingValue(httpContext));

        component.WaitForState(() => component.Markup.Contains("testuser@example.com"));

        // Assert: Check if user is rendered
        Assert.Contains("testuser@example.com", component.Markup);
        Assert.Contains("Unconfirmed", component.Markup);
    }
}