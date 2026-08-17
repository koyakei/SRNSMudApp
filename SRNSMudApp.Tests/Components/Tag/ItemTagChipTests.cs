#region

using System.Security.Claims;

using Bunit;
using Bunit.TestDoubles;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.Tag;
using SRNSMudApp.Data;
using SRNSMudApp.Services;

#endregion

namespace SRNSMudApp.Tests.Components.Tag;

public class ItemTagChipTests : TestContext
{
    public ItemTagChipTests()
    {
        _ = Services.AddMudServices();
        TestAuthorizationContext authContext = this.AddTestAuthorization();
        authContext.SetAuthorized("test-user-id");
        authContext.SetClaims(new Claim(ClaimTypes.NameIdentifier, "test-user-id"));

        var dbName = Guid.NewGuid().ToString();
        _ = Services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(dbName), ServiceLifetime.Scoped, ServiceLifetime.Singleton);
        _ = Services.AddDbContextFactory<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(dbName));

        var itemTagServiceMock = new Mock<IItemTagService>();
        _ = Services.AddScoped(sp => itemTagServiceMock.Object);

        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void ItemTagChip_ShouldRenderTagNameAndWeight_ForTagRelation()
    {
        // Arrange
        var tag = new SRNSMudApp.Data.Tag { Id = 1, Name = "TestTag", OwnerId = "test-user-id" };
        var tagRelation = new TagRelation { Id = 1, TagId = 1, Tag = tag, ItemId = 1, Weight = 10, OwnerId = "test-user-id" };
        var item = new SRNSMudApp.Data.Item { Id = 1, Content = "Test Item", OwnerId = "test-user-id" };

        // Act
        IRenderedComponent<ItemTagChip> component = RenderComponent<ItemTagChip>(parameters => parameters
            .Add(p => p.TagRelation, tagRelation)
            .Add(p => p.Item, item)
            .Add(p => p.CurrentUserId, "test-user-id")
        );

        // Assert
        Assert.Contains("TestTag", component.Markup);
        Assert.Contains("10", component.Markup);
        Assert.Contains("mud-icon", component.Markup); // Check for the presence of an icon
    }
}
