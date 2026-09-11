using Bunit;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Moq;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.Item;
using SRNSMudApp.Data;
using SRNSMudApp.Services;
using SRNSMudApp.Tests.TestSupport;

namespace SRNSMudApp.Tests.Components.Item;

public sealed class QuotedItemListDialogTests : IAsyncLifetime
{
    private readonly BunitContext _ctx = new();
    private readonly Mock<IItemQuoteService> _itemQuoteServiceMock = new();
    private readonly IRenderedComponent<MudDialogProvider> _dialogProvider;

    public QuotedItemListDialogTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices().AddMockSrnsServices();
        _ctx.Services.RemoveAll<IItemQuoteService>();
        _ = _ctx.Services.AddScoped(_ => _itemQuoteServiceMock.Object);
        _dialogProvider = _ctx.Render<MudDialogProvider>();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Render_WhenTargetItemHasSourceItem_RendersSourceItemSectionAndCard()
    {
        var targetItem = new SRNSMudApp.Data.Item
        {
            Id = 10,
            OwnerId = "user-1",
            Content = "分割された新アイテム本文",
            QuotedItemId = 5
        };

        var sourceItem = new SRNSMudApp.Data.Item
        {
            Id = 5,
            OwnerId = "user-1",
            Content = "分割元の親アイテム本文"
        };

        _ = _itemQuoteServiceMock.Setup(s => s.GetSourceItemAsync(targetItem.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceItem);
        _ = _itemQuoteServiceMock.Setup(s => s.GetQuotedByItemsAsync(targetItem.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var parameters = new DialogParameters<QuotedItemListDialog>
        {
            { x => x.TargetItem, targetItem },
            { x => x.CurrentUserId, "user-1" }
        };

        IDialogService dialogService = _ctx.Services.GetRequiredService<IDialogService>();
        _ = await dialogService.ShowAsync<QuotedItemListDialog>("引用された投稿一覧", parameters);

        _dialogProvider.WaitForState(() => _dialogProvider.FindAll("[data-testid='source-item-container']").Count > 0);

        var sourceContainer = _dialogProvider.Find("[data-testid='source-item-container']");
        Assert.NotNull(sourceContainer);
        Assert.Contains("分割元の親アイテム本文", sourceContainer.TextContent);
        Assert.Contains("分割元・引用元のアイテム", sourceContainer.TextContent);
    }

    [Fact]
    public async Task Render_WhenTargetItemHasNoSourceItem_AndNoQuotes_RendersNoQuotesMessage()
    {
        var targetItem = new SRNSMudApp.Data.Item
        {
            Id = 20,
            OwnerId = "user-1",
            Content = "単独アイテム本文"
        };

        _ = _itemQuoteServiceMock.Setup(s => s.GetSourceItemAsync(targetItem.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SRNSMudApp.Data.Item?)null);
        _ = _itemQuoteServiceMock.Setup(s => s.GetQuotedByItemsAsync(targetItem.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var parameters = new DialogParameters<QuotedItemListDialog>
        {
            { x => x.TargetItem, targetItem },
            { x => x.CurrentUserId, "user-1" }
        };

        IDialogService dialogService = _ctx.Services.GetRequiredService<IDialogService>();
        _ = await dialogService.ShowAsync<QuotedItemListDialog>("引用された投稿一覧", parameters);

        _dialogProvider.WaitForState(() => _dialogProvider.FindAll("[data-testid='no-quotes-message']").Count > 0);

        var noQuotesMessage = _dialogProvider.Find("[data-testid='no-quotes-message']");
        Assert.NotNull(noQuotesMessage);
        Assert.Contains("このアイテムを引用した投稿はまだありません", noQuotesMessage.TextContent);
    }

    [Fact]
    public async Task Render_WhenTargetItemHasQuotes_RendersQuotedItemsContainer()
    {
        var targetItem = new SRNSMudApp.Data.Item
        {
            Id = 30,
            OwnerId = "user-1",
            Content = "親アイテム本文"
        };

        var quoteItem = new SRNSMudApp.Data.Item
        {
            Id = 31,
            OwnerId = "user-2",
            Content = "引用投稿本文",
            QuotedItemId = 30
        };

        _ = _itemQuoteServiceMock.Setup(s => s.GetSourceItemAsync(targetItem.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SRNSMudApp.Data.Item?)null);
        _ = _itemQuoteServiceMock.Setup(s => s.GetQuotedByItemsAsync(targetItem.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([quoteItem]);

        var parameters = new DialogParameters<QuotedItemListDialog>
        {
            { x => x.TargetItem, targetItem },
            { x => x.CurrentUserId, "user-1" }
        };

        IDialogService dialogService = _ctx.Services.GetRequiredService<IDialogService>();
        _ = await dialogService.ShowAsync<QuotedItemListDialog>("引用された投稿一覧", parameters);

        _dialogProvider.WaitForState(() => _dialogProvider.FindAll("[data-testid='quoted-items-container']").Count > 0);

        var container = _dialogProvider.Find("[data-testid='quoted-items-container']");
        Assert.NotNull(container);
        Assert.Contains("引用投稿本文", container.TextContent);
    }

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }
}