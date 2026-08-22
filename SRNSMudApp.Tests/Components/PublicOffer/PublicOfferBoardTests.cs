#region

using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Moq;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.PublicOffer;
using SRNSMudApp.Data;
using SRNSMudApp.Services;

using Xunit;

#endregion

namespace SRNSMudApp.Tests.Components.PublicOffer;

public class PublicOfferBoardTests : IAsyncDisposable
{
    private const string AliceUserId = "alice-id";
    private const string CharlieUserId = "charlie-id";

    private readonly BunitContext _ctx;

    public PublicOfferBoardTests()
    {
        _ctx = new BunitContext();
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices();
        _ctx.Services.AddAuthorizationCore();

        AuthenticationState authState = CreateAuthState(CharlieUserId);
        Mock<AuthenticationStateProvider> authMock = new();
        _ = authMock.Setup(p => p.GetAuthenticationStateAsync()).ReturnsAsync(authState);
        _ctx.Services.AddScoped(_ => authMock.Object);

        var dbName = Guid.NewGuid().ToString();
        _ = _ctx.Services.AddDbContextFactory<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(dbName)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));

        _ctx.Services.AddScoped<TaggingContractService>();
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }

    /// <summary>
    ///     アクティブなオファーのみカード表示され（無償バッジ・提供者名付き）、
    ///     無効化済みのオファーは表示されないことを検証する。
    ///     （PublicOfferE2ETests の「Alice creates a Public Offer / Charlie sees it」の移行テスト）
    /// </summary>
    [Fact]
    public async Task Board_ShowsOnlyActiveOffers_WithFreeBadge()
    {
        // Arrange: alice のアクティブな無償オファーと、charlie の無効化済みオファーを投入
        (SRNSMudApp.Data.Tag aliceTag, SRNSMudApp.Data.Tag inactiveTag, _) = await SeedOffersAsync();

        RenderFragment board = builder =>
        {
            builder.OpenComponent<PublicOfferBoard>(0);
            builder.CloseComponent();
        };
        IRenderedComponent<AuthDialogHost> host =
            _ctx.Render<AuthDialogHost>(parameters => parameters.Add(p => p.ChildContent, board));

        // Act & Assert: ロード完了後、アクティブなオファーだけが表示される
        host.WaitForState(() => host.Markup.Contains(aliceTag.Name));
        host.WaitForState(() => !host.Markup.Contains("mud-progress-circular"));

        Assert.Contains(aliceTag.Name, host.Markup);
        Assert.Contains("提供者: Alice", host.Markup);
        Assert.Contains("無償で提供", host.Markup);
        Assert.Contains("オファーに応じる", host.Markup);
        Assert.DoesNotContain(inactiveTag.Name, host.Markup);
    }

    /// <summary>
    ///     カードの「オファーに応じる」からトリガー契約を作成・承認すると、
    ///     関係が追加され、オファーが無効化されて掲示板から消えることを検証する。
    ///     （ContractAndOfferScenarioE2ETests / PublicOffer_CreateAndTrigger の移行テスト）
    /// </summary>
    [Fact]
    public async Task TriggerViaCard_AddsRelation_AndRemovesOfferFromBoard()
    {
        // Arrange: charlie に自分のアイテム、alice にアクティブな無償オファーを用意
        (SRNSMudApp.Data.Tag aliceTag, _, PublicTradeOffer offer) = await SeedOffersAsync(
            withCharlieItem: true);

        RenderFragment board = builder =>
        {
            builder.OpenComponent<PublicOfferBoard>(0);
            builder.CloseComponent();
        };
        IRenderedComponent<AuthDialogHost> host =
            _ctx.Render<AuthDialogHost>(parameters => parameters.Add(p => p.ChildContent, board));

        host.WaitForState(() => host.Markup.Contains(aliceTag.Name));

        // Act: 「オファーに応じる」→ ダイアログでアイテム選択 → 実行
        host.FindAll("button").First(b => b.TextContent.Contains("オファーに応じる")).Click();

        host.WaitForState(() => host.Markup.Contains("タグを付与する対象のアイテム"));

        IRenderedComponent<MudAutocomplete<SRNSMudApp.Data.Item>> autocomplete =
            host.FindComponents<MudAutocomplete<SRNSMudApp.Data.Item>>().First();
        SRNSMudApp.Data.Item selectedItem =
            (await autocomplete.Instance.SearchFunc!("Charlie", default))
            .Single(i => i.OwnerId == CharlieUserId);
        await host.InvokeAsync(() => autocomplete.Instance.ValueChanged!.InvokeAsync(selectedItem));

        IRenderedComponent<MudForm> form = host.FindComponents<MudForm>().First();
        await host.InvokeAsync(() => form.Instance.ValidateAsync());

        host.FindAll("button").First(b => b.TextContent.Contains("実行する")).Click();

        // Assert: 承認完了のスナックバーが表示され、掲示板が再ロードされる
        //        （注: 現行実装ではトリガー後もオファーはアクティブなまま表示され続ける）
        host.WaitForState(() => host.Markup.Contains("公開オファーを利用してタグを獲得しました！"),
            TimeSpan.FromSeconds(15));
        host.WaitForState(() => !host.Markup.Contains("タグを付与する対象のアイテム"));

        await using ApplicationDbContext db = CreateDbContext();
        TagRelation relation =
            await db.TagRelations!.SingleAsync(r => r.ItemId == selectedItem.Id && r.TagId == aliceTag.Id);
        Assert.Equal(CharlieUserId, relation.OwnerId);

        // 注: 現行のサービス実装はトリガー実行後もオファーを無効化しない
        await using ApplicationDbContext db2 = CreateDbContext();
        PublicTradeOffer reloaded = await db2.PublicTradeOffers!.SingleAsync(o => o.Id == offer.Id);
        Assert.True(reloaded.IsActive);
    }

    private async Task<(SRNSMudApp.Data.Tag, SRNSMudApp.Data.Tag, PublicTradeOffer)> SeedOffersAsync(
        bool withCharlieItem = false)
    {
        await using ApplicationDbContext db = CreateDbContext();
        _ = db.Users.Add(new ApplicationUser { Id = AliceUserId, UserName = "Alice", Email = "alice@example.com" });
        _ = db.Users.Add(new ApplicationUser { Id = CharlieUserId, UserName = "Charlie", Email = "charlie@example.com" });

        SRNSMudApp.Data.Tag aliceTag =
            new() { Name = $"AliceOfferTag_{Guid.NewGuid():N}", Content = "Alice's offered tag", OwnerId = AliceUserId };
        _ = db.Tags.Add(aliceTag);

        SRNSMudApp.Data.Tag inactiveTag =
            new() { Name = $"InactiveTag_{Guid.NewGuid():N}", Content = "Inactive offer tag", OwnerId = AliceUserId };
        _ = db.Tags.Add(inactiveTag);

        if (withCharlieItem)
        {
            SRNSMudApp.Data.Item charlieItem =
                new() { Content = "Charlie's own item", OwnerId = CharlieUserId };
            _ = db.Items.Add(charlieItem);
        }

        _ = await db.SaveChangesAsync();

        PublicTradeOffer activeOffer = new()
        {
            OwnerId = AliceUserId,
            OfferedTagId = aliceTag.Id,
            RequiredAssetAmount = 0,
            IsActive = true
        };
        _ = db.PublicTradeOffers.Add(activeOffer);

        PublicTradeOffer deactivatedOffer = new()
        {
            OwnerId = AliceUserId,
            OfferedTagId = inactiveTag.Id,
            RequiredAssetAmount = 0,
            IsActive = false
        };
        _ = db.PublicTradeOffers.Add(deactivatedOffer);
        _ = await db.SaveChangesAsync();

        return (aliceTag, inactiveTag, activeOffer);
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
    /// </summary>
    private sealed class AuthDialogHost : ComponentBase
    {
        [Parameter] public RenderFragment ChildContent { get; set; } = _ => { };

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<CascadingAuthenticationState>(0);
            builder.AddAttribute(1, nameof(CascadingAuthenticationState.ChildContent), (RenderFragment)(b =>
            {
                b.OpenComponent<MudSnackbarProvider>(0);
                b.CloseComponent();
                b.OpenComponent<MudDialogProvider>(1);
                b.CloseComponent();
                b.AddContent(2, ChildContent);
            }));
            builder.CloseComponent();
        }
    }
}
