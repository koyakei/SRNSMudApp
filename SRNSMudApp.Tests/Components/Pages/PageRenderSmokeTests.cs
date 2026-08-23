#region

using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Bunit;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor.Services;

using SRNSMudApp.Components.Pages;
using SRNSMudApp.Components.Tag;
using SRNSMudApp.Data;
using SRNSMudApp.Services;
using SRNSMudApp.Tests.TestSupport;

using Xunit;

#endregion

namespace SRNSMudApp.Tests.Components.Pages;

/// <summary>
///     GlobalPopoverE2ETests（実ブラウザで全ルート遷移時の未処理例外UIを確認する横断スモークテスト）の
///     bUnit 側補完。各トップレベルページが単体で例外なく初期レンダリングできることを検証する。
/// </summary>
public class PageRenderSmokeTests : IAsyncDisposable
{
    private const string UserId = "smoke-user-id";

    private readonly BunitContext _ctx;

    public PageRenderSmokeTests()
    {
        _ctx = new BunitContext();
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices().AddSrnsComponentServices();

        AuthenticationState authState = CreateAuthState(UserId);
        Mock<AuthenticationStateProvider> authMock = new();
        _ = authMock.Setup(p => p.GetAuthenticationStateAsync()).ReturnsAsync(authState);
        _ctx.Services.AddScoped(_ => authMock.Object);

        var dbName = Guid.NewGuid().ToString();
        _ = _ctx.Services.AddDbContextFactory<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(dbName)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));

        // Home が TagCard を描画するため、その依存も登録する
        _ = _ctx.Services.AddSrnsComponentServices();

        Mock<ITagEmbeddingService> embeddingMock = new();
        _ = embeddingMock.Setup(s => s.GenerateEmbeddingAsync(It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("embedding unavailable in smoke test"));
        _ctx.Services.AddScoped(_ => embeddingMock.Object);
    }

    public async ValueTask DisposeAsync() => await _ctx.DisposeAsync();

    /// <summary>
    ///     ホームページ（/）がログイン済み状態で例外なくレンダリングできること。
    /// </summary>
    [Fact]
    public void Home_Renders_WithoutException()
    {
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

    /// <summary>
    ///     タグ検索ページ（/Tag/TagSearch）が例外なくレンダリングできること。
    /// </summary>
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
        ClaimsIdentity identity = new(claims, "TestAuthType");
        return new AuthenticationState(new ClaimsPrincipal(identity));
    }

    /// <summary>
    ///     認証カスケードを提供するホスト。
    /// </summary>
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