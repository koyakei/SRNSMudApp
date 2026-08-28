using System.Security.Claims;

using Bunit;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor.Services;

using SRNSMudApp.Components.UI;
using SRNSMudApp.Data;
using SRNSMudApp.Services;

namespace SRNSMudApp.Tests.Components.UI;

using Item = SRNSMudApp.Data.Item;
using Tag = SRNSMudApp.Data.Tag;

public sealed class ResourceListRenderingTests : IAsyncLifetime
{
    private const string UserId = "test-user-id";

    private readonly BunitContext _ctx = new();
    private readonly Mock<IHomeDataProvider> _homeDataMock = new();

    public ResourceListRenderingTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices().AddMockSrnsServices();
        _ = _ctx.Services.AddScoped(_ => _homeDataMock.Object);

        Bunit.TestDoubles.BunitAuthorizationContext authorization = _ctx.AddAuthorization();
        authorization.SetAuthorized("test_user");
        authorization.SetClaims(new Claim(ClaimTypes.NameIdentifier, UserId));

        var storeMock = new Mock<IUserStore<ApplicationUser>>();
        var userManagerMock = new Mock<UserManager<ApplicationUser>>(storeMock.Object, null!, null!, null!, null!,
            null!, null!, null!, null!);
        _ = _ctx.Services.AddScoped(_ => userManagerMock.Object);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    [Fact]
    public void ResourceList_RendersItemsBeforeTags()
    {
        // Arrange
        var item = new Item
        {
            Id = 101,
            Content = "Test Item Content",
            OwnerId = UserId,
            Owner = new ApplicationUser { Id = UserId, UserName = "test_user" }
        };
        var tag = new Tag
        {
            Id = 202,
            Name = "Test Tag Name",
            OwnerId = UserId
        };
        List<Item> items = [item];
        List<Tag> tags = [tag];

        _ = _homeDataMock.Setup(d => d.GetTagsAndRelationsAsync())
            .ReturnsAsync(([], []));

        // Act
        IRenderedComponent<ResourceList> cut = _ctx.Render<ResourceList>(parameters =>
            parameters
                .Add(p => p.Items, items)
                .Add(p => p.Tags, tags));

        cut.WaitForState(() => cut.Markup.Contains("item-card-101") && cut.Markup.Contains("tag-card-202"));

        // Assert: Item が Tag よりも前に出現することを確認
        var itemIndex = cut.Markup.IndexOf("item-card-101", StringComparison.Ordinal);
        var tagIndex = cut.Markup.IndexOf("tag-card-202", StringComparison.Ordinal);

        Assert.True(itemIndex >= 0, "Item card should be rendered");
        Assert.True(tagIndex >= 0, "Tag card should be rendered");
        Assert.True(itemIndex < tagIndex, "Item card should be rendered before Tag card");
    }

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }
}