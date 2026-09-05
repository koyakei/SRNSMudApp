using System.Security.Claims;

using Bunit;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.PublicOffer;
using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;
using SRNSMudApp.Services.Dialogs;

namespace SRNSMudApp.Tests.Components.PublicOffer;

public sealed class PublicOfferBoardTests : IAsyncLifetime
{
    private const string AliceUserId = "alice-id";
    private const string CharlieUserId = "charlie-id";

    private readonly BunitContext _ctx = new();
    private readonly Mock<IContractDataProvider> _contractDataMock = new();
    private readonly Mock<ITaggingContractService> _contractServiceMock = new();
    private readonly Mock<IDialogLauncher> _dialogLauncherMock = new();

    public PublicOfferBoardTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices().AddMockSrnsServices();
        _ = _ctx.Services.AddScoped(_ => _contractDataMock.Object);
        _ = _ctx.Services.AddScoped(_ => _dialogLauncherMock.Object);
        _ctx.Services.AddAuthorizationCore();

        var authState = CreateAuthState(CharlieUserId);
        Mock<AuthenticationStateProvider> authMock = new();
        _ = authMock.Setup(p => p.GetAuthenticationStateAsync()).ReturnsAsync(authState);
        _ctx.Services.AddScoped(_ => authMock.Object);
        _ctx.Services.AddScoped(_ => _contractServiceMock.Object);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    [Fact]
    public void Board_ShowsOnlyActiveOffers_WithFreeBadge()
    {
        var alice = new ApplicationUser { Id = AliceUserId, UserName = "Alice" };
        var aliceTag = new SRNSMudApp.Data.Tag { Id = 1, Name = "AliceOfferTag", OwnerId = AliceUserId, Owner = alice };
        var activeOffer = new PublicTradeOffer
        {
            Id = 10,
            OwnerId = AliceUserId,
            Owner = alice,
            OfferedTagId = 1,
            OfferedTag = aliceTag,
            RequiredAssetAmount = 0,
            IsActive = true
        };

        _ = _contractDataMock.Setup(d => d.GetActivePublicOffersAsync()).ReturnsAsync([activeOffer]);

        RenderFragment board = builder =>
        {
            builder.OpenComponent<PublicOfferBoard>(0);
            builder.CloseComponent();
        };
        IRenderedComponent<AuthDialogHost> host =
            _ctx.Render<AuthDialogHost>(parameters => parameters.Add(p => p.ChildContent, board));

        host.WaitForState(() => host.Markup.Contains(aliceTag.Name));

        Assert.Contains(aliceTag.Name, host.Markup);
        Assert.Contains("提供者: Alice", host.Markup);
        Assert.Contains("無償で提供", host.Markup);
        Assert.Contains("オファーに応じる", host.Markup);
    }

    [Fact]
    public void TriggerViaCard_CallsContractServiceAccept()
    {
        var alice = new ApplicationUser { Id = AliceUserId, UserName = "Alice" };
        var aliceTag = new SRNSMudApp.Data.Tag { Id = 1, Name = "AliceOfferTag", OwnerId = AliceUserId, Owner = alice };
        var activeOffer = new PublicTradeOffer
        {
            Id = 10,
            OwnerId = AliceUserId,
            Owner = alice,
            OfferedTagId = 1,
            OfferedTag = aliceTag,
            RequiredAssetAmount = 0,
            IsActive = true
        };

        _ = _contractDataMock.Setup(d => d.GetActivePublicOffersAsync()).ReturnsAsync([activeOffer]);

        var dialogReferenceMock = new Mock<IDialogReference>();
        _ = dialogReferenceMock.Setup(d => d.Result).ReturnsAsync(DialogResult.Ok(999));

        _ = _dialogLauncherMock.Setup(l => l.ShowAsync(
            typeof(TriggerPublicOfferDialog),
            "オファーに応じる",
            It.IsAny<DialogParameters?>(),
            It.IsAny<DialogOptions?>()))
            .ReturnsAsync(dialogReferenceMock.Object);

        _ = _contractServiceMock.Setup(s => s.AcceptContractAsync(999, CharlieUserId))
            .ReturnsAsync(new Success<string>("Success"));

        RenderFragment board = builder =>
        {
            builder.OpenComponent<PublicOfferBoard>(0);
            builder.CloseComponent();
        };
        IRenderedComponent<AuthDialogHost> host =
            _ctx.Render<AuthDialogHost>(parameters => parameters.Add(p => p.ChildContent, board));

        host.WaitForState(() => host.Markup.Contains(aliceTag.Name));

        host.FindAll("button").First(b => b.TextContent.Contains("オファーに応じる")).Click();

        host.WaitForState(() => host.Markup.Contains("公開オファーを利用してタグを獲得しました！"),
            TimeSpan.FromSeconds(5));

        _contractServiceMock.Verify(s => s.AcceptContractAsync(999, CharlieUserId), Times.Once);
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