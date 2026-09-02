using Bunit;

using Microsoft.Extensions.DependencyInjection;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.Diagram;

using TagEntity = SRNSMudApp.Data.Tag;

namespace SRNSMudApp.Tests.Components.Diagram;

public sealed class CreateEdgeDialogTests : IAsyncDisposable
{
    private readonly BunitContext _ctx = new();

    public CreateEdgeDialogTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices();
        _ = _ctx.Render<MudPopoverProvider>();
        _ = _ctx.Render<MudDialogProvider>();
    }

    [Fact]
    public async Task CreateEdgeDialog_InitializesWithSourceAndTargetTags_WhenProvided()
    {
        // Arrange
        var host = _ctx.Render<MudDialogProvider>();
        var dialogService = _ctx.Services.GetRequiredService<IDialogService>();

        var tag1 = new TagEntity { Id = 10, Name = "SourceAlpha", OwnerId = "user-1" };
        var tag2 = new TagEntity { Id = 20, Name = "TargetBeta", OwnerId = "user-1" };
        var availableTags = new List<TagEntity> { tag1, tag2 };
        var parameters = new DialogParameters<CreateEdgeDialog>
        {
            { x => x.AvailableTags, availableTags },
            { x => x.InitialSourceTag, tag1 },
            { x => x.InitialTargetTag, tag2 }
        };

        // Act
        _ = await dialogService.ShowAsync<CreateEdgeDialog>("Edge の作成", parameters);
        host.WaitForState(() => host.Markup.Contains("SourceAlpha") && host.Markup.Contains("TargetBeta"));

        // Assert
        Assert.Contains("SourceAlpha", host.Markup);
        Assert.Contains("TargetBeta", host.Markup);
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }
}