#region

using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

using Bunit;

using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor.Services;

using SRNSMudApp.Components.UI;
using SRNSMudApp.Data;
using SRNSMudApp.Services;
using SRNSMudApp.Tests.TestSupport;

using Xunit;

#endregion

namespace SRNSMudApp.Tests.Components.Tag;

[Collection(MsSqlCollection.Name)]
public class TaggingRequestCancelTests : IAsyncLifetime
{
    private const string UserId = "user-1";

    private readonly MsSqlContainerFixture _fixture;
    private MsSqlTestDatabase _testDb = null!;
    private BunitContext _ctx = null!;
    private int _onDataChangedCount;

    public TaggingRequestCancelTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _testDb = await MsSqlTestDatabase.CreateAsync(_fixture.ConnectionString, nameof(TaggingRequestCancelTests));

        _ctx = new BunitContext();
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices().AddSrnsComponentServices();
        _ctx.Services.AddAuthorizationCore();

        AuthenticationState authState = CreateAuthState(UserId);
        Mock<AuthenticationStateProvider> authMock = new();
        _ = authMock.Setup(p => p.GetAuthenticationStateAsync()).ReturnsAsync(authState);
        _ctx.Services.AddScoped(_ => authMock.Object);

        _ = _ctx.Services.AddMsSqlDbFactory(_testDb.ConnectionString);

        _ctx.Services.AddScoped<IItemTagService, ItemTagService>();
        _ctx.Services.AddScoped<TaggingContractService>();
        _ctx.Services.AddScoped<ITaggingService, TaggingService>();
    }

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
        await _testDb.DisposeAsync();
    }

    [Fact]
    public async Task CancelButton_ShouldCancelRequestAndShowCanceledIconInteractively()
    {
        (SRNSMudApp.Data.Item requestItem, TaggingRequestEntity contract) = await SeedSelfRequestAsync();

        IRenderedComponent<ItemCard> cut = RenderCard(requestItem);

        // 取り下げボタンが表示され、アラートには「タグ追加リクエスト」が出ている
        Assert.Contains("タグ追加リクエスト", cut.Markup);
        cut.Find("button[title='リクエストを取り下げる']").Click();

        // DB上でステータスが Canceled に変わる
        cut.WaitForAssertion(() =>
        {
            using ApplicationDbContext db = CreateDbContext();
            Assert.Equal(TradeStatus.Canceled, db.TaggingRequestEntities.Find(contract.Id)!.Status);
        });

        // アイコンが取り下げ済みに変わり、キャンセルボタンが消える
        await cut.WaitForStateAsync(() => cut.Markup.Contains("canceled-icon"));
        Assert.Empty(cut.FindAll("button[title='リクエストを取り下げる']"));
        Assert.Equal(1, _onDataChangedCount);
    }

    private IRenderedComponent<ItemCard> RenderCard(SRNSMudApp.Data.Item item)
    {
        return _ctx.Render<ItemCard>(parameters => parameters
            .Add(p => p.Item, item)
            .Add(p => p.CurrentUserId, UserId)
            .Add(p => p.OnDataChanged, () => _onDataChangedCount++));
    }

    private async Task<(SRNSMudApp.Data.Item RequestItem, TaggingRequestEntity Contract)> SeedSelfRequestAsync()
    {
        await using ApplicationDbContext db = await CreateDbContextAsync();
        db.Users.Add(new ApplicationUser { Id = UserId, UserName = "user1" });
        SRNSMudApp.Data.Tag tag = new() { Name = "CancelTestTag", OwnerId = UserId };
        SRNSMudApp.Data.Item targetItem = new() { Content = "CancelTestTargetItem", OwnerId = UserId };
        db.Tags.Add(tag);
        db.Items.Add(targetItem);
        _ = await db.SaveChangesAsync();

        TaggingRequestEntity contract = new()
        {
            ContractType = "Gratis",
            TargetItemId = targetItem.Id,
            RequestedTagId = tag.Id,
            RequesterUserId = UserId,
            OwnerId = UserId,
            TagOwnerUserId = UserId,
            RequestType = TaggingRequestType.Add,
            Status = TradeStatus.Proposed
        };
        db.TaggingRequestEntities.Add(contract);

        SRNSMudApp.Data.Item requestItem = new()
        {
            Content = "This is a request to cancel",
            OwnerId = UserId,
            AsRequestOf = contract
        };
        db.Items.Add(requestItem);
        _ = await db.SaveChangesAsync();

        // ナビゲーションプロパティを再読込してレンダリング用の実体を取得する
        await using ApplicationDbContext fresh = await CreateDbContextAsync();
        SRNSMudApp.Data.Item loaded = await fresh.Items.Include(i => i.AsRequestOf).ThenInclude(r => r.RequestedTag)
            .FirstAsync(i => i.Id == requestItem.Id);
        return (loaded, contract);
    }

    private ApplicationDbContext CreateDbContext() => _ctx.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext();

    private Task<ApplicationDbContext> CreateDbContextAsync() => _ctx.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContextAsync();

    private static AuthenticationState CreateAuthState(string userId)
    {
        Claim[] claims = [new(ClaimTypes.NameIdentifier, userId), new(ClaimTypes.Name, userId)];
        ClaimsIdentity identity = new(claims, "TestAuthType");
        return new AuthenticationState(new ClaimsPrincipal(identity));
    }
}