#region

using System;
using System.Collections.Generic;
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

using SRNSMudApp.Components.Tag;
using SRNSMudApp.Components.UI;
using SRNSMudApp.Data;
using SRNSMudApp.Services;
using SRNSMudApp.Tests.TestSupport;

using Xunit;

#endregion

namespace SRNSMudApp.Tests.Components.Tag;

[Collection(MsSqlCollection.Name)]
public class TaggingRequestReplyTests : IAsyncLifetime
{
    private const string ReplierId = "replier-1";
    private const string ItemOwnerId = "item-owner";
    private const string TagOwnerId = "tag-owner";

    private readonly MsSqlContainerFixture _fixture;
    private MsSqlTestDatabase _testDb = null!;
    private BunitContext _ctx = null!;

    public TaggingRequestReplyTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _testDb = await MsSqlTestDatabase.CreateAsync(_fixture.ConnectionString, nameof(TaggingRequestReplyTests));

        _ctx = new BunitContext();
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices().AddSrnsComponentServices();
        _ctx.Services.AddAuthorizationCore();

        AuthenticationState authState = CreateAuthState(ReplierId);
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
    public async Task ThreadDialog_ShouldShowRequestMessage_WhenRequestHasNoRequestItem()
    {
        TaggingRequestEntity contract = await SeedRequestAsync();

        IRenderedComponent<AuthDialogHost> host = RenderHost();
        IDialogReference dialog = await ShowThreadDialogAsync(contract);

        host.WaitForState(() => host.Markup.Contains("を追加するリクエストをしました。"));
        Assert.Contains("ReplyTestTag", host.Markup);
        Assert.Contains(ItemOwnerId, host.Markup);
    }

    [Fact]
    public async Task ThreadDialog_PostingReply_ShouldSaveReplyAsItemLinkedToRequest()
    {
        TaggingRequestEntity contract = await SeedRequestAsync();

        IRenderedComponent<AuthDialogHost> host = RenderHost();
        IDialogReference dialog = await ShowThreadDialogAsync(contract);

        host.WaitForState(() => host.Markup.Contains("返信を投稿..."));

        const string replyText = "スレッドへの返信コメントです";
        host.Find("input[placeholder='返信を投稿...']").Input(replyText);
        // MudBlazor 9 の MudIconButton は AriaLabel をそのまま属性名として出力する
        host.Find("button[AriaLabel='Send']").Click();

        // DBにリクエストに紐づくリプライItemが作成される
        host.WaitForAssertion(() =>
        {
            using ApplicationDbContext db = CreateDbContext();
            SRNSMudApp.Data.Item? reply = db.Items.FirstOrDefault(i => i.TaggingRequestEntityId == contract.Id);
            Assert.NotNull(reply);
            Assert.Equal(replyText, reply.Content);
            Assert.Equal(ReplierId, reply.OwnerId);
        });

        // 投稿したリプライがスレッド上のアイテムカードとして表示される
        await host.WaitForStateAsync(() => host.Markup.Contains(replyText));

    }

    [Fact]
    public void ItemTagRequestChip_ShouldDisplayReplyCountBadge()
    {
        TaggingRequestEntity request = new()
        {
            ContractType = "Gratis",
            OwnerId = ItemOwnerId,
            RequesterUserId = ItemOwnerId,
            TagOwnerUserId = TagOwnerId,
            TargetItemId = 10,
            RequestedTagId = 20,
            Status = TradeStatus.Proposed,
            RequestType = TaggingRequestType.Add,
            RequestedTag = new SRNSMudApp.Data.Tag { Name = "ReplyTestTag", OwnerId = TagOwnerId },
            Replies = [new() { Content = "reply-1", OwnerId = TagOwnerId }]
        };
        SRNSMudApp.Data.Item item = new() { Content = "chip-item", OwnerId = ItemOwnerId };

        IRenderedComponent<ItemTagRequestChip> cut = _ctx.Render<ItemTagRequestChip>(parameters => parameters
            .Add(p => p.Request, request)
            .Add(p => p.Item, item));

        Assert.Contains("ReplyTestTag", cut.Markup);
        Assert.Equal("1", cut.Find(".mud-badge").TextContent.Trim());
    }

    [Fact]
    public void ItemTagRequestChip_ShouldBeHidden_WhenStatusIsNotProposed()
    {
        TaggingRequestEntity request = new()
        {
            ContractType = "Gratis",
            OwnerId = ItemOwnerId,
            RequesterUserId = ItemOwnerId,
            TagOwnerUserId = TagOwnerId,
            TargetItemId = 10,
            RequestedTagId = 20,
            Status = TradeStatus.Canceled,
            RequestType = TaggingRequestType.Add,
            RequestedTag = new SRNSMudApp.Data.Tag { Name = "ReplyTestTag", OwnerId = TagOwnerId }
        };
        SRNSMudApp.Data.Item item = new() { Content = "chip-item", OwnerId = ItemOwnerId };

        IRenderedComponent<ItemTagRequestChip> cut = _ctx.Render<ItemTagRequestChip>(parameters => parameters
            .Add(p => p.Request, request)
            .Add(p => p.Item, item));

        Assert.Empty(cut.Markup.Trim());
    }

    private async Task<IDialogReference> ShowThreadDialogAsync(TaggingRequestEntity contract)
    {
        IDialogService dialogService = _ctx.Services.GetRequiredService<IDialogService>();
        DialogParameters<TaggingRequestThreadDialog> dialogParameters = new()
        {
            { x => x.TaggingRequest, contract }
        };
        return await dialogService.ShowAsync<TaggingRequestThreadDialog>("リクエストスレッド", dialogParameters);
    }

    private IRenderedComponent<AuthDialogHost> RenderHost() => _ctx.Render<AuthDialogHost>();

    private async Task<TaggingRequestEntity> SeedRequestAsync()
    {
        await using ApplicationDbContext db = CreateDbContext();
        db.Users.AddRange(
            new ApplicationUser { Id = ReplierId, UserName = "replier1" },
            new ApplicationUser { Id = ItemOwnerId, UserName = ItemOwnerId },
            new ApplicationUser { Id = TagOwnerId, UserName = TagOwnerId });
        SRNSMudApp.Data.Item targetItem = new() { Content = "ReplyTargetItem", OwnerId = ItemOwnerId };
        SRNSMudApp.Data.Tag tag = new() { Name = "ReplyTestTag", OwnerId = TagOwnerId };
        db.Items.Add(targetItem);
        db.Tags.Add(tag);
        _ = await db.SaveChangesAsync();

        TaggingRequestEntity contract = new()
        {
            ContractType = "Gratis",
            OwnerId = ItemOwnerId,
            RequesterUserId = ItemOwnerId,
            TagOwnerUserId = TagOwnerId,
            TargetItemId = targetItem.Id,
            RequestedTagId = tag.Id,
            Status = TradeStatus.Proposed,
            RequestType = TaggingRequestType.Add
        };
        db.TaggingRequestEntities.Add(contract);
        _ = await db.SaveChangesAsync();
        return contract;
    }

    private ApplicationDbContext CreateDbContext() => _ctx.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext();

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