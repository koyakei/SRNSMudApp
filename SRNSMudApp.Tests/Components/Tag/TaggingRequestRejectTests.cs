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

using SRNSMudApp.Components.Tag;
using SRNSMudApp.Components.UI;
using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;
using SRNSMudApp.Tests.TestSupport;

using Xunit;

#endregion

namespace SRNSMudApp.Tests.Components.Tag;

[Collection(MsSqlCollection.Name)]
public class TaggingRequestRejectTests : IAsyncLifetime
{
    private const string ItemOwnerId = "item-owner";
    private const string TagOwnerId = "tag-owner";

    private readonly MsSqlContainerFixture _fixture;
    private MsSqlTestDatabase _testDb = null!;
    private BunitContext _ctx = null!;
    private int _onRequestChangedCount;

    public TaggingRequestRejectTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _testDb = await MsSqlTestDatabase.CreateAsync(_fixture.ConnectionString, nameof(TaggingRequestRejectTests));

        _ctx = new BunitContext();
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices().AddSrnsComponentServices();
        _ctx.Services.AddAuthorizationCore();

        AuthenticationState authState = CreateAuthState(TagOwnerId);
        Mock<AuthenticationStateProvider> authMock = new();
        _ = authMock.Setup(p => p.GetAuthenticationStateAsync()).ReturnsAsync(authState);
        _ctx.Services.AddScoped(_ => authMock.Object);

        _ = _ctx.Services.AddMsSqlDbFactory(_testDb.ConnectionString);

        _ctx.Services.AddScoped<TaggingContractService>();
        _ctx.Services.AddScoped<ITaggingService, TaggingService>();
        _ctx.Services.AddScoped<IItemTagService, ItemTagService>();
    }

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
        await _testDb.DisposeAsync();
    }

    [Fact]
    public async Task Submit_ShouldCloseDialogWithEnteredReason()
    {
        IRenderedComponent<AuthDialogHost> host = RenderEmptyHost();

        IDialogService dialogService = _ctx.Services.GetRequiredService<IDialogService>();
        IDialogReference dialog = await dialogService.ShowAsync<RejectRequestDialog>("リクエストを却下");

        host.WaitForState(() => host.Markup.Contains("却下理由"));
        host.Find("textarea").Input("不適切なタグのため");
        host.FindAll("button").First(b => b.TextContent.Contains("却下する")).Click();

        DialogResult? result = await dialog.Result;
        Assert.False(result!.Canceled);
        Assert.Equal("不適切なタグのため", result.Data as string);
    }

    [Fact]
    public async Task Cancel_ShouldReturnCanceledResult()
    {
        IRenderedComponent<AuthDialogHost> host = RenderEmptyHost();

        IDialogService dialogService = _ctx.Services.GetRequiredService<IDialogService>();
        IDialogReference dialog = await dialogService.ShowAsync<RejectRequestDialog>("リクエストを却下");

        host.WaitForState(() => host.Markup.Contains("却下理由"));
        host.FindAll("button").First(b => b.TextContent.Contains("キャンセル")).Click();

        DialogResult? result = await dialog.Result;
        Assert.True(result!.Canceled);
    }

    [Fact]
    public async Task RejectFlow_FromList_ShouldSetStatusRejectedAndSaveReason()
    {
        TaggingRequestEntity contract = await SeedAddRequestAsync();

        IRenderedComponent<AuthDialogHost> host = RenderHostWithList([contract]);

        // リクエスト一覧が描画され、却下ボタンが表示される（タグオーナーに見える）
        host.WaitForState(() => host.Markup.Contains("data-testid=\"tagging-request-reject\""));
        host.Find("[data-testid='tagging-request-reject']").Click();

        // 却下ダイアログが開く
        host.WaitForState(() => host.Markup.Contains("却下理由"));
        host.Find("textarea").Input("不適切なリクエストのため");

        // 送信するとサービスが呼ばれ、DBのステータスが Rejected になる
        host.FindAll("button").First(b => b.TextContent.Contains("却下する")).Click();
        host.WaitForAssertion(() =>
        {
            using ApplicationDbContext db = CreateDbContext();
            TaggingRequestEntity updated = db.TaggingRequestEntities.Find(contract.Id)!;
            Assert.Equal(TradeStatus.Rejected, updated.Status);
            var expectedRejectionJson = System.Text.Json.JsonSerializer.Serialize(
                new RejectionReason("不適切なリクエストのため"));
            Assert.Equal(expectedRejectionJson, updated.RejectionInfoJson);
        });
        Assert.Equal(1, _onRequestChangedCount);
    }

    [Fact]
    public void RejectedRequest_ShouldNotBeShownInList()
    {
        TaggingRequestEntity rejected = new()
        {
            ContractType = "Gratis",
            RequesterUserId = ItemOwnerId,
            TagOwnerUserId = TagOwnerId,
            TargetItemId = 1,
            RequestedTagId = 2,
            OwnerId = ItemOwnerId,
            Status = TradeStatus.Rejected,
            RequestType = TaggingRequestType.Add,
            Owner = new ApplicationUser { Id = ItemOwnerId, UserName = ItemOwnerId },
            TargetItem = new SRNSMudApp.Data.Item { Id = 1, Content = "RejectedTargetItem", OwnerId = ItemOwnerId }
        };

        IRenderedComponent<TaggingRequestList> cut = RenderList(rejected);

        Assert.DoesNotContain("RejectedTargetItem", cut.Markup);
    }

    [Fact]
    public void ProposedRequest_ShouldBeShownInList()
    {
        TaggingRequestEntity proposed = new()
        {
            ContractType = "Gratis",
            RequesterUserId = ItemOwnerId,
            TagOwnerUserId = TagOwnerId,
            TargetItemId = 1,
            RequestedTagId = 2,
            OwnerId = ItemOwnerId,
            Status = TradeStatus.Proposed,
            RequestType = TaggingRequestType.Add,
            Owner = new ApplicationUser { Id = ItemOwnerId, UserName = ItemOwnerId },
            TargetItem = new SRNSMudApp.Data.Item { Id = 1, Content = "ProposedTargetItem", OwnerId = ItemOwnerId }
        };

        IRenderedComponent<TaggingRequestList> cut = RenderList(proposed);

        Assert.Contains("ProposedTargetItem", cut.Markup);
    }

    [Fact]
    public void RequestInfoAlert_ShouldDisplayRejectedStatusText()
    {
        SRNSMudApp.Components.UI.RequestInfo requestInfo = new()
        {
            IsTaggingRequest = true,
            RequestType = TaggingRequestType.Add,
            TargetItemId = 1,
            TargetTagId = 2,
            TargetTagName = "SomeTag",
            Status = TradeStatus.Rejected,
            ProposedWeight = 1
        };

        IRenderedComponent<RequestInfoAlert> cut = _ctx.Render<RequestInfoAlert>(parameters => parameters
            .Add(p => p.RequestInfo, requestInfo));

        Assert.Contains("却下済み", cut.Markup);
    }

    private IRenderedComponent<AuthDialogHost> RenderEmptyHost() => _ctx.Render<AuthDialogHost>();

    private IRenderedComponent<AuthDialogHost> RenderHostWithList(TaggingRequestEntity[] requests)
    {
        return _ctx.Render<AuthDialogHost>(parameters => parameters
            .Add(p => p.ChildContent, builder =>
            {
                builder.OpenComponent<TaggingRequestList>(0);
                builder.AddAttribute(1, nameof(TaggingRequestList.Requests), requests);
                builder.AddAttribute(2, nameof(TaggingRequestList.OnRequestChanged),
                    EventCallback.Factory.Create(this, () => _onRequestChangedCount++));
                builder.CloseComponent();
            }));
    }

    private IRenderedComponent<TaggingRequestList> RenderList(params TaggingRequestEntity[] requests)
    {
        return _ctx.Render<TaggingRequestList>(parameters => parameters
            .Add(p => p.Requests, requests.ToList())
            .AddCascadingValue(Task.FromResult(CreateAuthState(TagOwnerId))));
    }

    private async Task<TaggingRequestEntity> SeedAddRequestAsync()
    {
        await using ApplicationDbContext db = CreateDbContext();
        db.Users.AddRange(
            new ApplicationUser { Id = ItemOwnerId, UserName = ItemOwnerId },
            new ApplicationUser { Id = TagOwnerId, UserName = TagOwnerId });
        SRNSMudApp.Data.Item item = new() { Content = "AddableTargetItem", OwnerId = ItemOwnerId };
        SRNSMudApp.Data.Tag tag = new() { Name = "AddableTag", OwnerId = TagOwnerId };
        db.Items.Add(item);
        db.Tags.Add(tag);
        _ = await db.SaveChangesAsync();

        TaggingRequestEntity contract = new()
        {
            ContractType = "Gratis",
            OwnerId = ItemOwnerId,
            RequesterUserId = ItemOwnerId,
            TagOwnerUserId = TagOwnerId,
            TargetItemId = item.Id,
            RequestedTagId = tag.Id,
            Status = TradeStatus.Proposed,
            Payload = new GratisPayload("Please add this tag"),
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