using Bunit;

using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.Tag;
using SRNSMudApp.Data;
using SRNSMudApp.Services;

using TagEntity = SRNSMudApp.Data.Tag;

namespace SRNSMudApp.Tests.Components.Tag;

public class TagAutocompleteTests : IAsyncDisposable
{
    private readonly BunitContext _ctx = new();
    private readonly Mock<ITagDialogDataProvider> _dataProviderMock = new();

    public TagAutocompleteTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices();
        _ctx.Services.AddSingleton(_dataProviderMock.Object);
        _ = _ctx.Render<MudPopoverProvider>();
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }

    [Fact]
    public void TagAutocomplete_RendersWithLabelAndPlaceholder()
    {
        var cut = _ctx.Render<TagAutocomplete>(parameters => parameters
            .Add(p => p.Label, "カスタム検索ラベル")
            .Add(p => p.Placeholder, "プレースホルダー入力..."));

        Assert.Contains("カスタム検索ラベル", cut.Markup);
        Assert.Contains("プレースホルダー入力...", cut.Markup);
    }

    [Fact]
    public void TagAutocomplete_WithValue_RendersSelectedTagName()
    {
        var tag = new TagEntity { Id = 1, Name = "React", OwnerId = "user1" };

        var cut = _ctx.Render<TagAutocomplete>(parameters => parameters
            .Add(p => p.Value, tag));

        Assert.Contains("React", cut.Markup);
    }

    [Fact]
    public async Task TagAutocomplete_UsesCustomSearchFunc_WhenProvided()
    {
        bool customSearchCalled = false;
        var customTag = new TagEntity { Id = 99, Name = "CustomTag", OwnerId = "user1" };

        Task<IEnumerable<TagEntity>> CustomSearch(string? query, CancellationToken token)
        {
            customSearchCalled = true;
            return Task.FromResult<IEnumerable<TagEntity>>([customTag]);
        }

        var cut = _ctx.Render<TagAutocomplete>(parameters => parameters
            .Add(p => p.CustomSearchFunc, CustomSearch));

        // MudAutocomplete の SearchFunc を直接呼び出して動作確認
        var autocomplete = cut.FindComponent<MudBlazor.MudAutocomplete<TagEntity>>();
        var results = await autocomplete.Instance.SearchFunc("Cust", CancellationToken.None);

        Assert.True(customSearchCalled);
        Assert.Contains(results, t => t.Name == "CustomTag");
        _dataProviderMock.Verify(d => d.SearchTagsWithFallbackAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TagAutocomplete_UsesDataProviderSearch_ByDefault()
    {
        var fallbackTag = new TagEntity { Id = 5, Name = "FallbackTag", OwnerId = "user1" };
        _dataProviderMock.Setup(d => d.SearchTagsWithFallbackAsync("Fall", It.IsAny<CancellationToken>()))
            .ReturnsAsync([fallbackTag]);

        var cut = _ctx.Render<TagAutocomplete>();

        var autocomplete = cut.FindComponent<MudBlazor.MudAutocomplete<TagEntity>>();
        var results = await autocomplete.Instance.SearchFunc("Fall", CancellationToken.None);

        Assert.Contains(results, t => t.Name == "FallbackTag");
        _dataProviderMock.Verify(d => d.SearchTagsWithFallbackAsync("Fall", It.IsAny<CancellationToken>()), Times.Once);
    }
}