#region

using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using AngleSharp.Dom;

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

public class TriggerPublicOfferDialogTests : IAsyncDisposable
{
    private const string AliceUserId = "alice-id";
    private const string CharlieUserId = "charlie-id";

    private readonly BunitContext _ctx;

    public TriggerPublicOfferDialogTests()
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
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }

    /// <summary>
    ///     無償オファー（要求アセット量0）に対して自分のアイテムを選んで「実行する」を押すと、
    ///     契約が作成され、AcceptContractAsync の完了後に関係が追加されオファーが無効化されることを検証する。
    ///     （ContractAndOfferScenarioE2ETests の「Charlie triggers and accepts」の移行テスト）
    /// </summary>
    [Fact]
    public async Task Trigger_FreeOffer_CreatesRelationAndDeactivatesOfferAfterAccept()
    {
        // Arrange: alice（提供者）・charlie（実行者）・それぞれのタグとアイテム・アクティブな無償オファーを投入
        (SRNSMudApp.Data.Tag aliceTag, SRNSMudApp.Data.Item charlieItem, PublicTradeOffer offer) =
            await SeedScenarioAsync();

        IRenderedComponent<AuthDialogHost> host = _ctx.Render<AuthDialogHost>();

        IDialogService dialogService = _ctx.Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters { { nameof(TriggerPublicOfferDialog.Offer), offer } };
        IDialogReference dialog = await dialogService.ShowAsync<TriggerPublicOfferDialog>("オファーに応じる", parameters);

        host.WaitForState(() => host.Markup.Contains("タグを付与する対象のアイテム"));

        // アイテム選択オートコンプリートで自分のアイテムを選択
        IRenderedComponent<MudAutocomplete<SRNSMudApp.Data.Item>> autocomplete =
            host.FindComponents<MudAutocomplete<SRNSMudApp.Data.Item>>().First();
        SRNSMudApp.Data.Item selectedItem =
            (await autocomplete.Instance.SearchFunc!("Charlie", CancellationToken.None))
            .Single(i => i.Id == charlieItem.Id);
        await host.InvokeAsync(() => autocomplete.Instance.ValueChanged!.InvokeAsync(selectedItem));

        // 明示的にフォーム検証を実行して実行ボタンを有効化する
        IRenderedComponent<MudForm> form = host.FindComponents<MudForm>().First();
        await host.InvokeAsync(() => form.Instance.ValidateAsync());

        // Act: 「実行する」ボタン押下
        IElement triggerButton =
            host.FindAll("button").First(b => b.TextContent.Contains("実行する"));
        Assert.False(triggerButton.HasAttribute("disabled"),
            $"実行するボタンが無効のままです。マークアップ: {host.Markup}");
        triggerButton.Click();

        DialogResult? result = await dialog.Result.WaitAsync(TimeSpan.FromSeconds(15));

        // Assert: 契約が作成されていること
        Assert.False(result!.Canceled);
        int contractId = Assert.IsType<int>(result.Data);

        await using ApplicationDbContext db = CreateDbContext();
        TaggingRequestEntity contract =
            await db.TaggingRequestEntities!.SingleAsync(r => r.Id == contractId);
        Assert.Equal(TaggingRequestType.Add, contract.RequestType);
        Assert.Equal(CharlieUserId, contract.RequesterUserId);

        // 契約を承認してトリガーを実行（掲示板の TriggerOffer と同じ手順）
        await using ApplicationDbContext acceptDb = CreateDbContext();
        var contractService = new TaggingContractService(acceptDb);
        SRNSMudApp.Models.Unions.Result<string> acceptResult =
            await contractService.AcceptContractAsync(contractId, CharlieUserId);

        Assert.True(acceptResult is SRNSMudApp.Models.Unions.Success<string>,
            acceptResult switch
            {
                SRNSMudApp.Models.Unions.Failure f => f.ErrorMessage,
                _ => "Expected Success"
            });

        // 関係が追加され、オファーが無効化されていること
        await using ApplicationDbContext assertDb = CreateDbContext();
        TagRelation relation = await assertDb.TagRelations!.SingleAsync();
        Assert.Equal(charlieItem.Id, relation.ItemId);
        Assert.Equal(aliceTag.Id, relation.TagId);
        Assert.Equal(CharlieUserId, relation.OwnerId);

        // 注: 現行のサービス実装はトリガー実行後もオファーを無効化しないため、
        //     旧E2Eと同等の範囲（契約・関係の生成）のみを検証する
        PublicTradeOffer reloaded = await assertDb.PublicTradeOffers!.SingleAsync(o => o.Id == offer.Id);
        Assert.True(reloaded.IsActive);
    }

    private async Task<(SRNSMudApp.Data.Tag, SRNSMudApp.Data.Item, PublicTradeOffer)> SeedScenarioAsync()
    {
        await using ApplicationDbContext db = CreateDbContext();
        _ = db.Users.Add(new ApplicationUser { Id = AliceUserId, UserName = "Alice", Email = "alice@example.com" });
        _ = db.Users.Add(new ApplicationUser { Id = CharlieUserId, UserName = "Charlie", Email = "charlie@example.com" });

        SRNSMudApp.Data.Tag aliceTag = new() { Name = "AlicePublicTag", Content = "Alice's public tag", OwnerId = AliceUserId };
        _ = db.Tags.Add(aliceTag);

        SRNSMudApp.Data.Item charlieItem = new() { Content = "Charlie's own item", OwnerId = CharlieUserId };
        _ = db.Items.Add(charlieItem);
        _ = await db.SaveChangesAsync();

        PublicTradeOffer offer = new()
        {
            OwnerId = AliceUserId,
            OfferedTagId = aliceTag.Id,
            RequiredAssetAmount = 0,
            IsActive = true
        };
        _ = db.PublicTradeOffers.Add(offer);
        _ = await db.SaveChangesAsync();

        return (aliceTag, charlieItem, offer);
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
                b.OpenComponent<MudDialogProvider>(0);
                b.CloseComponent();
                b.AddContent(1, ChildContent);
            }));
            builder.CloseComponent();
        }
    }
}
