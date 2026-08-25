using System;
using System.Security.Claims;
using System.Threading.Tasks;

using Bunit;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor.Services;

using SRNSMudApp.Components.Pages;
using SRNSMudApp.Components.Tag;
using SRNSMudApp.Data;
using SRNSMudApp.Services;
using SRNSMudApp.Tests.TestSupport;

using Xunit;

namespace SRNSMudApp.Tests.Components.Pages;

public sealed class PageRenderSmokeTests : IAsyncLifetime
{
    private const string UserId = "smoke-user-id";

    private readonly BunitContext _ctx = new();
    private readonly Mock<IHomeDataProvider> _homeDataMock = new();

    public PageRenderSmokeTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices().AddMockSrnsServices();
        _ = _ctx.Services.AddScoped(_ => _homeDataMock.Object);

        var authState = CreateAuthState(UserId);
        Mock<AuthenticationStateProvider> authMock = new();
        _ = authMock.Setup(p => p.GetAuthenticationStateAsync()).ReturnsAsync(authState);
        _ctx.Services.AddScoped(_ => authMock.Object);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    [Fact]
    public void Home_Renders_WithoutException()
    {
        _ = _homeDataMock.Setup(d => d.GetFollowedTagIdsAsync(UserId))
            .ReturnsAsync([]);
        _ = _homeDataMock.Setup(d => d.GetTagsAndRelationsAsync())
            .ReturnsAsync(([], []));
        _ = _homeDataMock.Setup(d => d.EnsureSystemTagsAsync(UserId))
            .ReturnsAsync(new SystemTagsResult(1, 2, false));
        _ = _homeDataMock.Setup(d => d.LoadTimelineAsync(It.IsAny<System.Collections.Generic.IReadOnlyList<int>>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new HomeTimelinePage([], 0));

        RenderFragment home = builder =>
        {
            builder.OpenComponent<Home>(0);
            builder.CloseComponent();
        };
        IRenderedComponent<AuthHost> host =
            _ctx.Render<AuthHost>(parameters => parameters.Add(p => p.ChildContent, home));

        host.WaitForState(() => host.Markup.Contains("タイムライン") ||
                                 host.Markup.Contains("まだタグをフォローしていません"));

        Assert.Contains("タイムライン", host.Markup);
    }

    [Fact]
    public void TagSearch_Renders_WithoutException()
    {
        IRenderedComponent<TagSearch> cut = _ctx.Render<TagSearch>();

        Assert.Contains("タグ検索", cut.Markup);
        Assert.Contains("タグを検索", cut.Markup);
    }

    private static AuthenticationState CreateAuthState(string userId)
    {
        Claim[] claims = [new(ClaimTypes.NameIdentifier, userId), new(ClaimTypes.Name, userId)];
        var identity = new ClaimsIdentity(claims, "TestAuthType");
        return new AuthenticationState(new ClaimsPrincipal(identity));
    }

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }

    private sealed class AuthHost : ComponentBase
    {
        [Parameter] public RenderFragment ChildContent { get; set; } = _ => { };

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<CascadingAuthenticationState>(0);
            builder.AddAttribute(1, nameof(CascadingAuthenticationState.ChildContent), (RenderFragment)(b =>
            {
                b.AddContent(0, ChildContent);
            }));
            builder.CloseComponent();
        }
    }
}