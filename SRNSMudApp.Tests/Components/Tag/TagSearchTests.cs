using AngleSharp.Dom;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.Tag;
using SRNSMudApp.Services;

namespace SRNSMudApp.Tests.Components.Tag;

public sealed class TagSearchTests : IAsyncLifetime
{
    private readonly BunitContext _ctx = new();
    private readonly Mock<ITagDialogDataProvider> _dialogDataMock = new();

    public TagSearchTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices().AddMockSrnsServices();
        _ = _ctx.Services.AddScoped(_ => _dialogDataMock.Object);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    [Fact]
    public void TypingQuery_CallsSearchTagsWithFallbackAsync_AndShowsCandidates()
    {
        var yakuzaTag = new SRNSMudApp.Data.Tag
        {
            Id = 1,
            Name = "ヤクザ",
            Content = "反社",
            OwnerId = "user-1",
            CachedWeight = 5
        };

        _ = _dialogDataMock
            .Setup(d => d.SearchTagsWithFallbackAsync("反社会的勢力", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SRNSMudApp.Data.Tag> { yakuzaTag });

        IRenderedComponent<MudPopoverProvider> provider = _ctx.Render<MudPopoverProvider>();
        IRenderedComponent<TagSearch> cut = _ctx.Render<TagSearch>();

        IElement input = cut.Find("input");
        input.Input("反社会的勢力");

        provider.WaitForState(() => provider.Markup.Contains("ヤクザ"), TimeSpan.FromSeconds(5));

        Assert.Contains("ヤクザ", provider.Markup);
        _dialogDataMock.Verify(d => d.SearchTagsWithFallbackAsync("反社会的勢力", It.IsAny<CancellationToken>()), Times.Once);
    }

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }
}