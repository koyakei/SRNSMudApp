#region

using System.Reflection;
using System.Security.Claims;

using Bunit;
using Bunit.TestDoubles;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor.Services;

using SRNSMudApp.Components.Tag;
using SRNSMudApp.Data;

#endregion

namespace SRNSMudApp.Tests.Components.Tag;

public class TagListConcurrencyTests : TestContext
{
    public TagListConcurrencyTests()
    {
        // Add MudBlazor services
        _ = Services.AddMudServices();

        // Setup mock authentication
        var authContext = this.AddTestAuthorization();
        authContext.SetAuthorized("test-user-id");
        authContext.SetClaims(new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "test-user-id"));

        // Setup In-Memory database
        var dbName = Guid.NewGuid().ToString();
        _ = Services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(dbName), ServiceLifetime.Scoped, ServiceLifetime.Singleton);

        _ = Services.AddDbContextFactory<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(dbName));

        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void RenderingTagList_ShouldNotThrowConcurrencyException()
    {
        // Act & Assert
        // If the components share the same Scoped DbContext and perform async DB operations simultaneously,
        // this will throw an InvalidOperationException from Entity Framework Core.
        // By using IDbContextFactory internally, this error is avoided.
        Exception? exception = Record.Exception(() =>
        {
            _ = RenderComponent<TagList>();
        });

        // Verify that no exception was thrown
        Assert.Null(exception);
    }

    [Theory]
    [InlineData(typeof(TagList))]
    [InlineData(typeof(TagTable))]
    public void Component_ShouldNotInjectDbContextDirectly_ToPreventConcurrencyIssues(Type componentType)
    {
        // Arrange
        PropertyInfo[] properties =
            componentType.GetProperties(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        // Act
        var hasDirectDbContextInjection = properties.Any(p =>
            p.PropertyType == typeof(ApplicationDbContext) &&
            p.GetCustomAttributes(typeof(InjectAttribute), true).Length != 0);

        // Assert
        Assert.False(hasDirectDbContextInjection,
            $"{componentType.Name} should inject IDbContextFactory<ApplicationDbContext> instead of ApplicationDbContext directly to prevent InvalidOperationException during concurrent rendering.");
    }
}