using Bunit;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.Tag;
using SRNSMudApp.Data;

namespace SRNSMudApp.Tests.Components.Tag;

public sealed class ItemTagChipTests : IAsyncLifetime
{
    private readonly BunitContext _ctx = new();

    public ItemTagChipTests()
    {
        _ = _ctx.Services.AddMudServices().AddMockSrnsServices();
        _ = _ctx.Services.AddAuth("test-user-id");
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Render<MudPopoverProvider>();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    [Fact]
    public void ItemTagChip_ShouldRenderTagNameAndWeight_ForTagRelation()
    {
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

        IRenderedComponent<ItemTagChip> component = _ctx.Render<ItemTagChip>(parameters => parameters
            .Add(p => p.TagRelation, tagRelation)
            .Add(p => p.Item, item)
            .Add(p => p.CurrentUserId, "test-user-id")
        );

        Assert.Contains("TestTag", component.Markup);
        Assert.Contains("10", component.Markup);
        Assert.Contains("mud-icon", component.Markup);
    }

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }
}