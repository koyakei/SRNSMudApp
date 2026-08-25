using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;

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
using SRNSMudApp.Services;
using SRNSMudApp.Tests.TestSupport;

using Xunit;

namespace SRNSMudApp.Tests.Components.Tag;

public sealed class TaggingRequestReplyTests : IAsyncLifetime
{
    private const string ReplierId = "replier-1";
    private const string ItemOwnerId = "item-owner";
    private const string TagOwnerId = "tag-owner";

    private readonly BunitContext _ctx = new();
    private readonly Mock<IItemTagService> _itemTagServiceMock = new();
    private readonly Mock<IHomeDataProvider> _homeDataMock = new();

    public TaggingRequestReplyTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices().AddMockSrnsServices();
        _ = _ctx.Services.AddScoped(_ => _itemTagServiceMock.Object);
        _ = _ctx.Services.AddScoped(_ => _homeDataMock.Object);
        _ctx.Services.AddAuthorizationCore();

        _ = _homeDataMock.Setup(h => h.GetTagsAndRelationsAsync())
            .ReturnsAsync(([], []));

        var authState = CreateAuthState(ReplierId);
        Mock<AuthenticationStateProvider> authMock = new();
        _ = authMock.Setup(p => p.GetAuthenticationStateAsync()).ReturnsAsync(authState);
        _ctx.Services.AddScoped(_ => authMock.Object);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ThreadDialog_ShouldShowRequestMessage_WhenRequestHasNoRequestItem()
    {
        var contract = CreateTestRequest();

        IRenderedComponent<AuthDialogHost> host = RenderHost();
        _ = await ShowThreadDialogAsync(contract);

        host.WaitForState(() => host.Markup.Contains("を追加するリクエストをしました。"));
        Assert.Contains("ReplyTestTag", host.Markup);
        Assert.Contains(ItemOwnerId, host.Markup);
    }

    [Fact]
    public async Task ThreadDialog_PostingReply_ShouldCallAddReplyToRequestAsync_AndDisplayReply()
    {
        var contract = CreateTestRequest();
        const string replyText = "スレッドへの返信コメントです";

        var createdReply = new SRNSMudApp.Data.Item
        {
            Id = 999,
            Content = replyText,
            OwnerId = ReplierId,
            Owner = new ApplicationUser { Id = ReplierId, UserName = "Replier" },
            TaggingRequestEntityId = contract.Id
        };

        _ = _itemTagServiceMock
            .Setup(s => s.AddReplyToRequestAsync(contract.Id, ReplierId, replyText))
            .ReturnsAsync(createdReply);

        IRenderedComponent<AuthDialogHost> host = RenderHost();
        _ = await ShowThreadDialogAsync(contract);

        host.WaitForState(() => host.Markup.Contains("返信を投稿..."));

        IRenderedComponent<MudTextField<string>> textField = host.FindComponent<MudTextField<string>>();
        await host.InvokeAsync(() => textField.Instance.ValueChanged.InvokeAsync(replyText));

        IRenderedComponent<MudIconButton> sendButton = host.FindComponent<MudIconButton>();
        await host.InvokeAsync(() => sendButton.Instance.OnClick.InvokeAsync());

        _itemTagServiceMock.Verify(s => s.AddReplyToRequestAsync(contract.Id, ReplierId, replyText), Times.Once);
        host.WaitForState(() => host.Markup.Contains(replyText));
    }

    [Fact]
    public void ItemTagRequestChip_ShouldDisplayReplyCountBadge()
    {
        var request = new TaggingRequestEntity
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
        var item = new SRNSMudApp.Data.Item { Content = "chip-item", OwnerId = ItemOwnerId };

        IRenderedComponent<ItemTagRequestChip> cut = _ctx.Render<ItemTagRequestChip>(parameters => parameters
            .Add(p => p.Request, request)
            .Add(p => p.Item, item));

        Assert.Contains("ReplyTestTag", cut.Markup);
        Assert.Equal("1", cut.Find(".mud-badge").TextContent.Trim());
    }

    [Fact]
    public void ItemTagRequestChip_ShouldBeHidden_WhenStatusIsNotProposed()
    {
        var request = new TaggingRequestEntity
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
        var item = new SRNSMudApp.Data.Item { Content = "chip-item", OwnerId = ItemOwnerId };

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

    private static TaggingRequestEntity CreateTestRequest() => new()
    {
        Id = 100,
        ContractType = "Gratis",
        OwnerId = ItemOwnerId,
        RequesterUserId = ItemOwnerId,
        TagOwnerUserId = TagOwnerId,
        TargetItemId = 10,
        RequestedTagId = 20,
        Status = TradeStatus.Proposed,
        RequestType = TaggingRequestType.Add,
        Owner = new ApplicationUser { Id = ItemOwnerId, UserName = ItemOwnerId },
        RequestedTag = new SRNSMudApp.Data.Tag { Id = 20, Name = "ReplyTestTag", OwnerId = TagOwnerId }
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