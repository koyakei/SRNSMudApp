#region

using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

using AngleSharp.Dom;

using Bunit;
using Moq;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.PublicOffer;
using SRNSMudApp.Data;

using Xunit;

#endregion

namespace SRNSMudApp.Tests.Components.PublicOffer;

public class CreatePublicOfferDialogTests : IAsyncDisposable
{
    private const string AliceUserId = "alice-id";

    private readonly BunitContext _ctx;

    public CreatePublicOfferDialogTests()
    {
        _ctx = new BunitContext();
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices();
        _ctx.Services.AddAuthorizationCore();

        AuthenticationState authState = CreateAuthState(AliceUserId);
        Mock<AuthenticationStateProvider> authMock = new();
        _ = authMock.Setup(p => p.GetAuthenticationStateAsync()).ReturnsAsync(authState);
        _ctx.Services.AddScoped(_ => authMock.Object);

        var dbName = Guid.NewGuid().ToString();
        _ = _ctx.Services.AddDbContextFactory<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(dbName)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }

    /// <summary>
    ///     自分が所有するタグを選んで「公開する」を押すと、Gratisタイプ（要求アセット量0・アクティブ）の
    ///     PublicTradeOffer がDBに作成され、ダイアログが正常終了することを検証する。
    ///     （ContractAndOfferScenarioE2ETests の「Alice creates a Public Offer」および
    ///     PublicOfferE2ETests/PublicOffer_CreateAndTrigger 前半の移行テスト）
    /// </summary>
    [Fact]
    public async Task Publish_WithOwnedTag_CreatesActiveFreeOffer()
    {
        // Arrange: alice とその所有タグを投入
        int tagId = await SeedUserWithTagAsync("AliceTag");

        IRenderedComponent<AuthDialogHost> host = _ctx.Render<AuthDialogHost>();

        IDialogService dialogService = _ctx.Services.GetRequiredService<IDialogService>();
        IDialogReference dialog = await dialogService.ShowAsync<CreatePublicOfferDialog>("公開オファーの作成");

        host.WaitForState(() => host.Markup.Contains("提供するタグ"));

        // タグ選択オートコンプリートで自分のタグを選択（候補選択時に ValueChanged へ候補が流れる）
        IRenderedComponent<MudAutocomplete<SRNSMudApp.Data.Tag>> autocomplete =
            host.FindComponents<MudAutocomplete<SRNSMudApp.Data.Tag>>().First();
        SRNSMudApp.Data.Tag selectedTag =
            (await autocomplete.Instance.SearchFunc!("AliceTag", System.Threading.CancellationToken.None))
            .Single(t => t.Id == tagId);
        await host.InvokeAsync(() => autocomplete.Instance.ValueChanged!.InvokeAsync(selectedTag));

        // ValueChanged の直接呼び出しではフォーム検証が走らないため、明示的に検証して _isValid を更新する
        IRenderedComponent<MudForm> form = host.FindComponents<MudForm>().First();
        await host.InvokeAsync(() => form.Instance.ValidateAsync());

        // Act: 「公開する」ボタン押下
        IElement publishButton =
            host.FindAll("button").First(b => b.TextContent.Contains("公開する"));
        Assert.False(publishButton.HasAttribute("disabled"),
            $"公開するボタンが無効のままです。マークアップ: {host.Markup}");
        publishButton.Click();

        // タイムアウト付きで待機し、ダイアログが閉じない場合にテスト全体が停止しないようにする
        DialogResult? result = await dialog.Result.WaitAsync(TimeSpan.FromSeconds(15));

        // Assert: ダイアログが正常終了し、オファーがDBに作成されている
        Assert.False(result.Canceled);
        Assert.Equal(true, result.Data);

        await using ApplicationDbContext db = CreateDbContext();
        PublicTradeOffer offer = await db.PublicTradeOffers!.SingleAsync();
        Assert.Equal(AliceUserId, offer.OwnerId);
        Assert.Equal(tagId, offer.OfferedTagId);
        Assert.Equal(0, offer.RequiredAssetAmount);
        Assert.True(offer.IsActive);
    }

    private async Task<int> SeedUserWithTagAsync(string tagName)
    {
        await using ApplicationDbContext db = CreateDbContext();
        _ = db.Users.Add(new ApplicationUser { Id = AliceUserId, UserName = "alice", Email = "alice@example.com" });
        SRNSMudApp.Data.Tag tag = new() { Name = tagName, Content = "Alice's Tag", OwnerId = AliceUserId };
        _ = db.Tags.Add(tag);
        _ = await db.SaveChangesAsync();
        return tag.Id;
    }

    private ApplicationDbContext CreateDbContext()
    {
        return _ctx.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext();
    }

    private static AuthenticationState CreateAuthState(string userId)
    {
        Claim[] claims = [new(ClaimTypes.NameIdentifier, userId), new(ClaimTypes.Name, userId)];
        ClaimsIdentity identity = new(claims, "TestAuthType");
        return new AuthenticationState(new ClaimsPrincipal(identity));
    }

    /// <summary>
    ///     認証カスケードと MudDialogProvider を提供するホスト。
    ///     アプリの MudProviders.razor 相当の構成を再現する。
    /// </summary>
    private sealed class AuthDialogHost : ComponentBase
    {
        [Parameter] public RenderFragment ChildContent { get; set; } = _ => { };

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<CascadingAuthenticationState>(0);
            builder.AddAttribute(1, nameof(CascadingAuthenticationState.ChildContent), (RenderFragment)(b =>
            {
                b.OpenComponent<MudDialogProvider>(0);
                b.CloseComponent();
                b.AddContent(1, ChildContent);
            }));
            builder.CloseComponent();
        }
    }
}
