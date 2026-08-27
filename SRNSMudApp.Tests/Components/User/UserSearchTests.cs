using AngleSharp.Dom;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.User;
using SRNSMudApp.Data;
using SRNSMudApp.Services;

namespace SRNSMudApp.Tests.Components.User;

public sealed class UserSearchTests : IAsyncLifetime
{
    private readonly BunitContext _ctx = new();
    private readonly Mock<IUserDataProvider> _userDataMock = new();

    public UserSearchTests()
    {
        _ = _ctx.Services.AddMudServices().AddMockSrnsServices();
        _ = _ctx.Services.AddScoped(_ => _userDataMock.Object);
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    [Fact]
    public void UserSearch_Renders_Initially()
    {
        IRenderedComponent<UserSearch> component = _ctx.Render<UserSearch>();
        Assert.NotNull(component);
        Assert.Contains("ユーザーを検索", component.Markup);
    }

    [Fact]
    public void UserSearch_CanSearchUsers_CaseInsensitive()
    {
        var user = new ApplicationUser { Id = "1", UserName = "TestUser1", NormalizedUserName = "TESTUSER1" };
        _ = _userDataMock.Setup(d => d.SearchUsersByNormalizedNameAsync("test", It.IsAny<CancellationToken>()))
            .ReturnsAsync([user]);

        IRenderedComponent<MudPopoverProvider> provider = _ctx.Render<MudPopoverProvider>();
        IRenderedComponent<UserSearch> searchComponent = _ctx.Render<UserSearch>();

        IElement input = searchComponent.Find("input");
        input.Input("test");

        provider.WaitForState(
            () => provider.Markup.Contains("TestUser1") || provider.Markup.Contains("一致するユーザーが見つかりません"),
            TimeSpan.FromSeconds(3));

        Assert.Contains("TestUser1", provider.Markup);
    }

    [Fact]
    public void UserSearch_TypingFullName_ShowsCandidateInPopover()
    {
        var user = new ApplicationUser { Id = "1", UserName = "TestUser1", NormalizedUserName = "TESTUSER1" };
        _ = _userDataMock.Setup(d => d.SearchUsersByNormalizedNameAsync("testuser", It.IsAny<CancellationToken>()))
            .ReturnsAsync([user]);

        IRenderedComponent<MudPopoverProvider> provider = _ctx.Render<MudPopoverProvider>();
        IRenderedComponent<UserSearch> searchComponent = _ctx.Render<UserSearch>();

        IElement input = searchComponent.Find("input");
        input.Input("testuser");

        provider.WaitForState(() => provider.Markup.Contains("TestUser1"), TimeSpan.FromSeconds(3));

        Assert.Contains("TestUser1", provider.Markup);
    }

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }
}