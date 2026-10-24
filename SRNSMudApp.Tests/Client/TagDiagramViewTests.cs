namespace SRNSMudApp.Tests.Client;

using System;
using System.Threading;
using System.Threading.Tasks;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Client.Components;
using SRNSMudApp.Client.Models.Api;
using SRNSMudApp.Client.Services;

using Xunit;

public class TagDiagramViewTests : IAsyncDisposable
{
    private readonly BunitContext _ctx = new();
    private readonly Mock<IItemListApiClient> _apiClientMock = new();
    private readonly Mock<ISnackbar> _snackbarMock = new();

    public TagDiagramViewTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddMudServices();

        _ctx.Services.AddScoped(_ => _apiClientMock.Object);
        _ctx.Services.AddScoped(_ => _snackbarMock.Object);
    }

    [Fact]
    public void WhenNoTags_RendersEmptyMessage()
    {
        // Arrange
        _apiClientMock
            .Setup(c => c.GetTagDiagramDataAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TagDiagramResponse([], [], []));

        // Act
        IRenderedComponent<TagDiagramView> cut = _ctx.Render<TagDiagramView>();

        // Assert
        Assert.Contains("タグデータがありません", cut.Markup);
    }

    [Fact]
    public void WhenTagsExist_RendersTagCardsAndEdgeCount()
    {
        // Arrange
        var response = new TagDiagramResponse(
            Nodes:
            [
                new TagDto(1, "Architecture", "System architecture tag", "user-1", Score: 5),
                new TagDto(2, "Frontend", "UI tag", "user-2", Score: 3)
            ],
            Edges:
            [
                new TagDiagramEdgeDto(100, 1, 2, "user-1")
            ],
            ContextItems: []
        );

        _apiClientMock
            .Setup(c => c.GetTagDiagramDataAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        IRenderedComponent<TagDiagramView> cut = _ctx.Render<TagDiagramView>();

        // Assert
        Assert.Contains("Architecture", cut.Markup);
        Assert.Contains("Frontend", cut.Markup);
        Assert.Contains("Edge: 1", cut.Markup);
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}