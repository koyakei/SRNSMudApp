using System;
using System.Threading.Tasks;

using Bunit;

using MudBlazor.Services;

using SRNSMudApp.Components.Pages;
using SRNSMudApp.Components.Tag;
using SRNSMudApp.Tests.TestSupport;

using Xunit;

namespace SRNSMudApp.Tests.Components.Tag;

public sealed class TagListConcurrencyTests : IAsyncLifetime
{
    private readonly BunitContext _ctx = new();

    public TagListConcurrencyTests()
    {
        _ = _ctx.Services.AddMudServices().AddMockSrnsServices();
        _ = _ctx.Services.AddAuth("testuser");
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
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