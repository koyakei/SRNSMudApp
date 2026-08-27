using System.Security.Claims;

using Bunit;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.Tag;
using SRNSMudApp.Components.UI;
using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;

namespace SRNSMudApp.Tests.Components.Tag;

public sealed class TaggingRequestRejectTests : IAsyncLifetime
{
    private const string ItemOwnerId = "item-owner";
    private const string TagOwnerId = "tag-owner";

    private readonly BunitContext _ctx = new();
    private readonly Mock<ITaggingRequestActions> _actionsMock = new();
    private int _onRequestChangedCount;

    public TaggingRequestRejectTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices().AddMockSrnsServices();
        _ = _actionsMock.Setup(a => a.CanApprove(It.IsAny<TaggingRequestEntity>(), TagOwnerId)).Returns(true);
        _ctx.Services.AddScoped(_ => _actionsMock.Object);
        _ctx.Services.AddAuthorizationCore();

        var authState = CreateAuthState(TagOwnerId);
        Mock<AuthenticationStateProvider> authMock = new();
        _ = authMock.Setup(p => p.GetAuthenticationStateAsync()).ReturnsAsync(authState);
        _ctx.Services.AddScoped(_ => authMock.Object);
    }

    public Task InitializeAsync() => Task.CompletedTask;

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
    public void RejectFlow_FromList_ShouldCallRejectViaDialogAsyncAndInvokeCallback()
    {
        var contract = CreateAddRequest();
        _ = _actionsMock.Setup(a => a.RejectViaDialogAsync(contract.Id, TagOwnerId)).ReturnsAsync(true);

        IRenderedComponent<AuthDialogHost> host = RenderHostWithList([contract]);

        // リクエスト一覧が描画され、却下ボタンが表示される（タグオーナーに見える）
        host.WaitForState(() => host.Markup.Contains("data-testid=\"tagging-request-reject\""));
        host.Find("[data-testid='tagging-request-reject']").Click();

        _actionsMock.Verify(a => a.RejectViaDialogAsync(contract.Id, TagOwnerId), Times.Once);
        Assert.Equal(1, _onRequestChangedCount);
    }

    [Fact]
    public void RejectedRequest_ShouldNotBeShownInList()
    {
        var rejected = new TaggingRequestEntity
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
        var proposed = new TaggingRequestEntity
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
        var requestInfo = new SRNSMudApp.Components.UI.RequestInfo
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

    private static TaggingRequestEntity CreateAddRequest() => new()
    {
        Id = 100,
        ContractType = "Gratis",
        OwnerId = ItemOwnerId,
        RequesterUserId = ItemOwnerId,
        TagOwnerUserId = TagOwnerId,
        TargetItemId = 1,
        RequestedTagId = 2,
        Status = TradeStatus.Proposed,
        Payload = new GratisPayload("Please add this tag"),
        RequestType = TaggingRequestType.Add,
        Owner = new ApplicationUser { Id = ItemOwnerId, UserName = ItemOwnerId },
        TargetItem = new SRNSMudApp.Data.Item { Id = 1, Content = "AddableTargetItem", OwnerId = ItemOwnerId },
        RequestedTag = new SRNSMudApp.Data.Tag { Id = 2, Name = "AddableTag", OwnerId = TagOwnerId }
    };

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