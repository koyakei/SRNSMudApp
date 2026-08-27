using System.Security.Claims;

using Bunit;

using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor.Services;

using SRNSMudApp.Components.UI;
using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;

namespace SRNSMudApp.Tests.Components.Tag;

public sealed class TaggingRequestCancelTests : IAsyncLifetime
{
    private const string UserId = "user-1";

    private readonly BunitContext _ctx = new();
    private readonly Mock<TaggingContractService> _contractServiceMock;
    private int _onDataChangedCount;

    public TaggingRequestCancelTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices().AddMockSrnsServices();
        _ctx.Services.AddAuthorizationCore();

        AuthenticationState authState = CreateAuthState(UserId);
        Mock<AuthenticationStateProvider> authMock = new();
        _ = authMock.Setup(p => p.GetAuthenticationStateAsync()).ReturnsAsync(authState);
        _ctx.Services.AddScoped(_ => authMock.Object);

        var dummyOptions = new DbContextOptions<ApplicationDbContext>();
        _contractServiceMock = new Mock<TaggingContractService>(new ApplicationDbContext(dummyOptions));
        _ctx.Services.AddScoped(_ => _contractServiceMock.Object);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    [Fact]
    public void CancelButton_ShouldCancelRequestAndShowCanceledIconInteractively()
    {
        var contract = new TaggingRequestEntity
        {
            Id = 100,
            ContractType = "Gratis",
            TargetItemId = 10,
            RequestedTagId = 20,
            RequesterUserId = UserId,
            OwnerId = UserId,
            TagOwnerUserId = UserId,
            RequestType = TaggingRequestType.Add,
            Status = TradeStatus.Proposed,
            RequestedTag = new SRNSMudApp.Data.Tag { Id = 20, Name = "CancelTestTag", OwnerId = UserId }
        };

        var requestItem = new SRNSMudApp.Data.Item
        {
            Id = 50,
            Content = "This is a request to cancel",
            OwnerId = UserId,
            AsRequestOf = contract
        };

        _ = _contractServiceMock
            .Setup(s => s.CancelContractAsync(contract.Id, UserId))
            .ReturnsAsync(new Success<string>("契約をキャンセルしました。"));

        IRenderedComponent<ItemCard> cut = RenderCard(requestItem);

        // 取り下げボタンが表示され、アラートには「タグ追加リクエスト」が出ている
        Assert.Contains("タグ追加リクエスト", cut.Markup);
        cut.Find("button[title='リクエストを取り下げる']").Click();

        // 契約キャンセルサービスが呼ばれたこと
        _contractServiceMock.Verify(s => s.CancelContractAsync(contract.Id, UserId), Times.Once);

        // アイコンが取り下げ済みに変わり、キャンセルボタンが消える
        cut.WaitForState(() => cut.Markup.Contains("canceled-icon"));
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
}