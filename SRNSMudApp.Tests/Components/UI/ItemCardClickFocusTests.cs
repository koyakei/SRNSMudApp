using System.Security.Claims;

using Bunit;

using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor.Services;

using SRNSMudApp.Components.UI;
using SRNSMudApp.Data;
using SRNSMudApp.Tests.TestSupport;

namespace SRNSMudApp.Tests.Components.UI;

/// <summary>
///     ItemCard のカードクリック（OnCardClick）とスクロールフォーカス（OnFocus）の
///     コールバック発火仕様を検証する単体テスト。
/// </summary>
public sealed class ItemCardClickFocusTests : IAsyncLifetime
{
    private const string UserId = "test-user-1";
    private readonly BunitContext _ctx = new();

    public ItemCardClickFocusTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices().AddMockSrnsServices();
        _ctx.Services.AddAuthorizationCore();

        AuthenticationState authState = BunitTestSetup.CreateAuthState(UserId);
        Mock<AuthenticationStateProvider> authMock = new();
        _ = authMock.Setup(p => p.GetAuthenticationStateAsync()).ReturnsAsync(authState);
        _ctx.Services.AddScoped(_ => authMock.Object);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    [Fact]
    public void WhenCardClicked_WithOnCardClick_InvokesOnCardClick_AndNotOnFocus()
    {
        var item = new SRNSMudApp.Data.Item
        {
            Id = 99,
            Content = "Test Card Content",
            OwnerId = UserId
        };

        var cardClickedId = 0;
        var focusedId = 0;

        IRenderedComponent<ItemCard> comp = _ctx.Render<ItemCard>(parameters => parameters
            .Add(p => p.Item, item)
            .Add(p => p.CurrentUserId, UserId)
            .Add(p => p.OnCardClick, (int id) => cardClickedId = id)
            .Add(p => p.OnFocus, (int id) => focusedId = id));

        var cardElement = comp.Find("#item-card-99");
        cardElement.Click();

        Assert.Equal(99, cardClickedId);
        Assert.Equal(0, focusedId);
    }

    [Fact]
    public void WhenCardClicked_WithoutOnCardClick_FallsBackToOnFocus()
    {
        var item = new SRNSMudApp.Data.Item
        {
            Id = 99,
            Content = "Test Card Content",
            OwnerId = UserId
        };

        var focusedId = 0;

        IRenderedComponent<ItemCard> comp = _ctx.Render<ItemCard>(parameters => parameters
            .Add(p => p.Item, item)
            .Add(p => p.CurrentUserId, UserId)
            .Add(p => p.OnFocus, (int id) => focusedId = id));

        var cardElement = comp.Find("#item-card-99");
        cardElement.Click();

        Assert.Equal(99, focusedId);
    }

    [Fact]
    public void WhenScrollFocused_OnlyInvokesOnFocus_NeverOnCardClick()
    {
        var item = new SRNSMudApp.Data.Item
        {
            Id = 99,
            Content = "Test Card Content",
            OwnerId = UserId
        };

        var cardClickedId = 0;
        var focusedId = 0;

        IRenderedComponent<ItemCard> comp = _ctx.Render<ItemCard>(parameters => parameters
            .Add(p => p.Item, item)
            .Add(p => p.CurrentUserId, UserId)
            .Add(p => p.OnCardClick, (int id) => cardClickedId = id)
            .Add(p => p.OnFocus, (int id) => focusedId = id));

        // スクロール検知コールバックをシミュレート
        comp.Instance.OnElementFocusedByScroll("item-card-99");

        Assert.Equal(0, cardClickedId);
        Assert.Equal(99, focusedId);
    }

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }
}