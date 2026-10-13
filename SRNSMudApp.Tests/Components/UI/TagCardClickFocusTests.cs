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
///     TagCard のカードクリック（OnCardClick）とスクロールフォーカス（OnFocus）の
///     コールバック発火仕様を検証する単体テスト。
/// </summary>
public sealed class TagCardClickFocusTests : IAsyncLifetime
{
    private const string UserId = "test-user-tag";
    private readonly BunitContext _ctx = new();

    public TagCardClickFocusTests()
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
        var tag = new SRNSMudApp.Data.Tag
        {
            Id = 88,
            Name = "TestTag88",
            OwnerId = UserId
        };

        var cardClickedId = 0;
        var focusedId = 0;

        IRenderedComponent<TagCard> comp = _ctx.Render<TagCard>(parameters => parameters
            .Add(p => p.Tag, tag)
            .Add(p => p.CurrentUserId, UserId)
            .Add(p => p.OnCardClick, (int id) => cardClickedId = id)
            .Add(p => p.OnFocus, (int id) => focusedId = id));

        var cardElement = comp.Find("#tag-card-88");
        cardElement.Click();

        Assert.Equal(88, cardClickedId);
        Assert.Equal(0, focusedId);
    }

    [Fact]
    public void WhenCardClicked_WithoutOnCardClick_FallsBackToOnFocus()
    {
        var tag = new SRNSMudApp.Data.Tag
        {
            Id = 88,
            Name = "TestTag88",
            OwnerId = UserId
        };

        var focusedId = 0;

        IRenderedComponent<TagCard> comp = _ctx.Render<TagCard>(parameters => parameters
            .Add(p => p.Tag, tag)
            .Add(p => p.CurrentUserId, UserId)
            .Add(p => p.OnFocus, (int id) => focusedId = id));

        var cardElement = comp.Find("#tag-card-88");
        cardElement.Click();

        Assert.Equal(88, focusedId);
    }

    [Fact]
    public void WhenScrollFocused_OnlyInvokesOnFocus_NeverOnCardClick()
    {
        var tag = new SRNSMudApp.Data.Tag
        {
            Id = 88,
            Name = "TestTag88",
            OwnerId = UserId
        };

        var cardClickedId = 0;
        var focusedId = 0;

        IRenderedComponent<TagCard> comp = _ctx.Render<TagCard>(parameters => parameters
            .Add(p => p.Tag, tag)
            .Add(p => p.CurrentUserId, UserId)
            .Add(p => p.OnCardClick, (int id) => cardClickedId = id)
            .Add(p => p.OnFocus, (int id) => focusedId = id));

        // スクロール検知コールバックをシミュレート
        comp.Instance.OnElementFocusedByScroll("tag-card-88");

        Assert.Equal(0, cardClickedId);
        Assert.Equal(88, focusedId);
    }

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }
}