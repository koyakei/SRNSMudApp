#region

using Bunit;

using MudBlazor.Services;

using SRNSMudApp.Components.Tag;
using SRNSMudApp.Models;

#endregion

namespace SRNSMudApp.Tests.Components.Tag;

/// <summary>
///     TagBar コンポーネントの単体テスト (bUnit)。
/// </summary>
public class TagBarTests : IAsyncDisposable
{
    private readonly BunitContext _ctx;

    public TagBarTests()
    {
        _ctx = new BunitContext();
        _ctx.Services.AddMudServices().AddSrnsComponentServices();
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void TagBar_WithTags_RendersChipsWithTagNamesAndOwners()
    {
        // Arrange
        IReadOnlyList<TagPreviewItem> tags =
        [
            new(1, "Blazor", "alice", 10),
            new(2, "CSharp", null, 0)
        ];

        // Act
        IRenderedComponent<TagBar> component = _ctx.Render<TagBar>(parameters => parameters
            .Add(p => p.Tags, tags)
        );

        // Assert
        var markup = component.Markup;
        Assert.Contains("Blazor", markup);
        Assert.Contains("(alice)", markup);
        Assert.Contains("10", markup);
        Assert.Contains("CSharp", markup);
    }

    [Fact]
    public void TagBar_WithEmptyTags_RendersEmptyContainer()
    {
        // Act
        IRenderedComponent<TagBar> component = _ctx.Render<TagBar>(parameters => parameters
            .Add(p => p.Tags, [])
        );

        // Assert
        var element = component.Find("[data-testid='tag-bar']");
        Assert.NotNull(element);
        Assert.Empty(element.Children);
    }
}