using System.Security.Claims;

using Bunit;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Moq;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.Item;
using SRNSMudApp.Components.UI;
using SRNSMudApp.Data;
using SRNSMudApp.Services;
using SRNSMudApp.Services.Dialogs;
using SRNSMudApp.Tests.TestSupport;

namespace SRNSMudApp.Tests.Components.UI;

/// <summary>
///     ItemCard の引用投稿完了後に、新しく作成されたアイテムへフォーカスを当てて
///     ItemList ページへ遷移することを検証する単体テスト。
/// </summary>
public sealed class ItemCardQuoteFocusTests : IAsyncLifetime
{
    private const string UserId = "quote-user-1";
    private readonly BunitContext _ctx = new();
    private readonly Mock<IDialogLauncher> _dialogLauncherMock = new();
    private readonly Mock<IDialogReference> _dialogReferenceMock = new();
    private readonly Mock<IItemQuoteService> _itemQuoteServiceMock = new();

    public ItemCardQuoteFocusTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices().AddMockSrnsServices();
        _ctx.Services.AddAuthorizationCore();

        AuthenticationState authState = BunitTestSetup.CreateAuthState(UserId);
        Mock<AuthenticationStateProvider> authMock = new();
        _ = authMock.Setup(p => p.GetAuthenticationStateAsync()).ReturnsAsync(authState);
        _ctx.Services.AddScoped(_ => authMock.Object);

        _ctx.Services.RemoveAll<IDialogLauncher>();
        _ctx.Services.AddScoped(_ => _dialogLauncherMock.Object);

        _ctx.Services.RemoveAll<IItemQuoteService>();
        _ctx.Services.AddScoped(_ => _itemQuoteServiceMock.Object);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    [Fact]
    public async Task WhenQuoteCreated_NavigatesToItemList_WithFocusParameter()
    {
        var originalItem = new SRNSMudApp.Data.Item
        {
            Id = 10,
            Content = "Original Item",
            OwnerId = "other-user"
        };
        var createdQuoteItem = new SRNSMudApp.Data.Item
        {
            Id = 99,
            Content = "Quote Item",
            OwnerId = UserId,
            QuotedItemId = 10
        };

        var dialogResult = DialogResult.Ok(createdQuoteItem);
        _ = _dialogReferenceMock.Setup(r => r.Result).ReturnsAsync(dialogResult);
        _ = _dialogLauncherMock
            .Setup(l => l.ShowAsync(typeof(QuoteItemDialog), "引用して投稿", It.IsAny<DialogParameters>(), It.IsAny<DialogOptions>()))
            .ReturnsAsync(_dialogReferenceMock.Object);

        NavigationManager navManager = _ctx.Services.GetRequiredService<NavigationManager>();
        navManager.NavigateTo("http://localhost/Item/ItemDetail/10");

        IRenderedComponent<ItemCard> cut = _ctx.Render<ItemCard>(parameters => parameters
            .Add(p => p.Item, originalItem)
            .Add(p => p.CurrentUserId, UserId));

        // 引用ボタンをクリック
        var quoteButton = cut.Find($"[data-testid='quote-button-{originalItem.Id}']");
        await cut.InvokeAsync(() => quoteButton.Click());

        // /Item/ItemList?focus=99 へ遷移したことを検証
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("/Item/ItemList", navManager.Uri);
            Assert.Contains("focus=99", navManager.Uri);
        });
    }

    [Fact]
    public async Task WhenQuoteCreated_FromItemList_NavigatesToItemList_WithFocusParameter()
    {
        var originalItem = new SRNSMudApp.Data.Item
        {
            Id = 20,
            Content = "Original Item in List",
            OwnerId = "other-user"
        };
        var createdQuoteItem = new SRNSMudApp.Data.Item
        {
            Id = 100,
            Content = "Quote Item in List",
            OwnerId = UserId,
            QuotedItemId = 20
        };

        var dialogResult = DialogResult.Ok(createdQuoteItem);
        _ = _dialogReferenceMock.Setup(r => r.Result).ReturnsAsync(dialogResult);
        _ = _dialogLauncherMock
            .Setup(l => l.ShowAsync(typeof(QuoteItemDialog), "引用して投稿", It.IsAny<DialogParameters>(), It.IsAny<DialogOptions>()))
            .ReturnsAsync(_dialogReferenceMock.Object);

        NavigationManager navManager = _ctx.Services.GetRequiredService<NavigationManager>();
        navManager.NavigateTo("http://localhost/Item/ItemList?f=5");

        IRenderedComponent<ItemCard> cut = _ctx.Render<ItemCard>(parameters => parameters
            .Add(p => p.Item, originalItem)
            .Add(p => p.CurrentUserId, UserId));

        // 引用ボタンをクリック
        var quoteButton = cut.Find($"[data-testid='quote-button-{originalItem.Id}']");
        await cut.InvokeAsync(() => quoteButton.Click());

        // /Item/ItemList?focus=100 へ遷移したことを検証
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("/Item/ItemList", navManager.Uri);
            Assert.Contains("focus=100", navManager.Uri);
        });
    }

    [Fact]
    public async Task WhenQuoteCanceled_DoesNotNavigate()
    {
        var originalItem = new SRNSMudApp.Data.Item
        {
            Id = 30,
            Content = "Original Item",
            OwnerId = "other-user"
        };

        var dialogResult = DialogResult.Cancel();
        _ = _dialogReferenceMock.Setup(r => r.Result).ReturnsAsync(dialogResult);
        _ = _dialogLauncherMock
            .Setup(l => l.ShowAsync(typeof(QuoteItemDialog), "引用して投稿", It.IsAny<DialogParameters>(), It.IsAny<DialogOptions>()))
            .ReturnsAsync(_dialogReferenceMock.Object);

        NavigationManager navManager = _ctx.Services.GetRequiredService<NavigationManager>();
        var initialUri = "http://localhost/Item/ItemDetail/30";
        navManager.NavigateTo(initialUri);

        IRenderedComponent<ItemCard> cut = _ctx.Render<ItemCard>(parameters => parameters
            .Add(p => p.Item, originalItem)
            .Add(p => p.CurrentUserId, UserId));

        // 引用ボタンをクリック
        var quoteButton = cut.Find($"[data-testid='quote-button-{originalItem.Id}']");
        await cut.InvokeAsync(() => quoteButton.Click());

        Assert.Equal(initialUri, navManager.Uri);
    }

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }
}