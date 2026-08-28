using Bunit;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.Tag;

namespace SRNSMudApp.Tests.Components.Tag;

public sealed class TagListConcurrencyTests : IAsyncLifetime
{
    private readonly BunitContext _ctx = new();

    public TagListConcurrencyTests()
    {
        _ = _ctx.Services.AddMudServices().AddMockSrnsServices();
        _ = _ctx.Services.AddAuth("testuser");
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Render<MudPopoverProvider>();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    [Fact]
    public void RenderingTagList_ShouldNotThrowException()
    {
        var exception = Record.Exception(() =>
        {
            _ = _ctx.Render<TagList>();
        });

        Assert.Null(exception);
    }

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }
}