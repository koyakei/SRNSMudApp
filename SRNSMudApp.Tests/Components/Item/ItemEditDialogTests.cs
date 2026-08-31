using Bunit;

using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.Item;
using SRNSMudApp.Services;

namespace SRNSMudApp.Tests.Components.Item;

public sealed class ItemEditDialogTests : IAsyncLifetime
{
    private readonly BunitContext _ctx = new();
    private readonly Mock<IItemCardDataProvider> _itemCardDataMock = new();
    private readonly IRenderedComponent<MudDialogProvider> _dialogProvider;

    public ItemEditDialogTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices().AddMockSrnsServices();
        _ = _ctx.Services.AddScoped(_ => _itemCardDataMock.Object);
        _dialogProvider = _ctx.Render<MudDialogProvider>();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Render_WithUrlInContent_DisplaysUrlPreviewCardWithLoadPreview()
    {
        var item = new SRNSMudApp.Data.Item
        {
            Id = 1,
            OwnerId = "test-user-id",
            Content = "Editing item with link https://example.com"
        };
        var parameters = new DialogParameters<ItemEditDialog>
        {
            { x => x.Item, item }
        };

        IDialogService dialogService = _ctx.Services.GetRequiredService<IDialogService>();
        _ = await dialogService.ShowAsync<ItemEditDialog>("アイテムの編集", parameters);

        _dialogProvider.WaitForState(() => _dialogProvider.FindAll("textarea").Count > 0);

        var previewCard = _dialogProvider.FindComponent<SRNSMudApp.Components.UI.UrlPreviewCard>();
        Assert.NotNull(previewCard);
        Assert.Equal("https://example.com", previewCard.Instance.Url);
        Assert.NotNull(previewCard.Instance.LoadPreview);
    }

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }
}