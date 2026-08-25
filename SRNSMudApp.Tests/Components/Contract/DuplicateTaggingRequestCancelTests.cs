using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

using AngleSharp.Dom;

using Bunit;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.Contract;
using SRNSMudApp.Components.Tag;
using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;
using SRNSMudApp.Tests.TestSupport;

using Xunit;

namespace SRNSMudApp.Tests.Components.Contract;

public sealed class DuplicateTaggingRequestCancelTests : IAsyncLifetime
{
    private const string TagOwnerId = "dup-tag-owner";
    private const string UserAId = "dup-user-a";
    private const string UserBId = "dup-user-b";

    private readonly BunitContext _ctx = new();
    private readonly Mock<IContractDataProvider> _contractDataMock = new();
    private readonly Mock<ITaggingRequestActions> _actionsMock = new();
    private readonly Mock<TaggingContractService> _contractServiceMock;

    public DuplicateTaggingRequestCancelTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices().AddMockSrnsServices();
        _ = _ctx.Services.AddScoped(_ => _contractDataMock.Object);
        _ = _ctx.Services.AddScoped(_ => _actionsMock.Object);
        _ctx.Services.AddAuthorizationCore();

        var dummyOptions = new Microsoft.EntityFrameworkCore.DbContextOptions<ApplicationDbContext>();
        _contractServiceMock = new Mock<TaggingContractService>(new ApplicationDbContext(dummyOptions));
        _ctx.Services.AddScoped(_ => _contractServiceMock.Object);

        var authState = BunitTestSetup.CreateAuthState(UserBId);
        Mock<AuthenticationStateProvider> authMock = new();
        _ = authMock.Setup(p => p.GetAuthenticationStateAsync()).ReturnsAsync(authState);
        _ctx.Services.AddScoped(_ => authMock.Object);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    [Fact]
    public void ApprovingRequestFromUserA_CallsApproveAsyncForContractA()
    {
        var contractA = CreateRequest(1, UserAId);
        var contractB = CreateRequest(2, UserBId);

        _ = _actionsMock.Setup(a => a.CanApprove(contractA, TagOwnerId)).Returns(true);
        _ = _actionsMock.Setup(a => a.CanApprove(contractB, TagOwnerId)).Returns(true);
        _ = _actionsMock.Setup(a => a.ApproveAsync(contractA.Id, TagOwnerId)).ReturnsAsync(true);

        IRenderedComponent<TaggingRequestList> cut = _ctx.Render<TaggingRequestList>(parameters => parameters
            .Add(p => p.Requests, new[] { contractA, contractB })
            .AddCascadingValue(Task.FromResult(BunitTestSetup.CreateAuthState(TagOwnerId))));

        cut.WaitForState(() => cut.Markup.Contains("data-testid=\"tagging-request-approve\""));

        IElement rowA = cut.FindAll("tr").First(tr => tr.TextContent.Contains(UserAId));
        rowA.QuerySelector("[data-testid='tagging-request-approve']")!.Click();

        _actionsMock.Verify(a => a.ApproveAsync(contractA.Id, TagOwnerId), Times.Once);
        _actionsMock.Verify(a => a.ApproveAsync(contractB.Id, TagOwnerId), Times.Never);
    }

    [Fact]
    public void OutboxWithdrawButton_CallsCancelContractAsync()
    {
        var contractB = CreateRequest(2, UserBId);

        _ = _contractDataMock.Setup(d => d.GetContractsAsync(UserBId))
            .ReturnsAsync(new ContractManagementPageData([], [contractB]));
        _ = _contractServiceMock.Setup(s => s.CancelContractAsync(contractB.Id, UserBId))
            .ReturnsAsync(new Success<string>("コントラクトを取り下げました。"));

        RenderFragment page = builder =>
        {
            builder.OpenComponent<ContractManagement>(0);
            builder.CloseComponent();
        };
        IRenderedComponent<SnackbarHost> host =
            _ctx.Render<SnackbarHost>(parameters => parameters.Add(p => p.ChildContent, page));

        host.WaitForState(() => host.Markup.Contains("送信済み"));
        host.FindAll(".mud-tab").First(t => t.TextContent.Contains("送信済み")).Click();

        host.WaitForState(() => host.Markup.Contains("提案中"));
        host.FindAll("button").First(b => b.TextContent.Contains("取り下げる")).Click();

        host.WaitForState(() => host.Markup.Contains("コントラクトを取り下げました。"),
            TimeSpan.FromSeconds(5));

        _contractServiceMock.Verify(s => s.CancelContractAsync(contractB.Id, UserBId), Times.Once);
    }

    private static TaggingRequestEntity CreateRequest(int id, string ownerId) => new()
    {
        Id = id,
        ContractType = "Gratis",
        OwnerId = ownerId,
        RequesterUserId = ownerId,
        TagOwnerUserId = TagOwnerId,
        TargetItemId = 10,
        RequestedTagId = 20,
        Status = TradeStatus.Proposed,
        RequestType = TaggingRequestType.Add,
        Owner = new ApplicationUser { Id = ownerId, UserName = ownerId },
        RequestedTag = new SRNSMudApp.Data.Tag { Id = 20, Name = "DupTag", OwnerId = TagOwnerId },
        TargetItem = new SRNSMudApp.Data.Item { Id = 10, Content = "Dup Item", OwnerId = ownerId }
    };

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }

    private sealed class SnackbarHost : ComponentBase
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