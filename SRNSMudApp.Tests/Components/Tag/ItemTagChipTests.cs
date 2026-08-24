using System.Threading.Tasks;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor.Services;

using SRNSMudApp.Components.Tag;
using SRNSMudApp.Data;
using SRNSMudApp.Services;
using SRNSMudApp.Tests.TestSupport;

using Xunit;

namespace SRNSMudApp.Tests.Components.Tag;

[Collection(MsSqlCollection.Name)]
public class ItemTagChipTests : IAsyncLifetime
{
    private readonly MsSqlContainerFixture _fixture;
    private MsSqlTestDatabase _testDb = null!;
    private readonly BunitContext _ctx = new();

    public ItemTagChipTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _testDb = await MsSqlTestDatabase.CreateAsync(_fixture.ConnectionString, nameof(ItemTagChipTests));

        _ = _ctx.Services.AddAuth("test-user-id");
        _ = _ctx.Services.AddSrnsComponentServices();

        _ctx.Services.AddMsSqlDbFactory(_testDb.ConnectionString);

        var itemTagServiceMock = new Mock<IItemTagService>();
        _ = _ctx.Services.AddScoped(sp => itemTagServiceMock.Object);

        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
    }

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
        await _testDb.DisposeAsync();
    }

    [Fact]
    public void ItemTagChip_ShouldRenderTagNameAndWeight_ForTagRelation()
    {
        // Arrange
        var tag = new SRNSMudApp.Data.Tag { Id = 1, Name = "TestTag", OwnerId = "test-user-id" };
        var tagRelation = new TagRelation
        {
            Id = 1,
            TagId = 1,
            Tag = tag,
            ItemId = 1,
            Weight = 10,
            OwnerId = "test-user-id"
        };
        var item = new SRNSMudApp.Data.Item { Id = 1, Content = "Test Item", OwnerId = "test-user-id" };

        // Act
        // Render ではなく、最新の _ctx.Render<T>() を使用します
        IRenderedComponent<ItemTagChip> component = _ctx.Render<ItemTagChip>(parameters => parameters
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