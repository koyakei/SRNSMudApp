using System;
using System.Security.Claims;
using System.Threading.Tasks;

using Bunit;

using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor.Services;

using SRNSMudApp.Components.Tag;
using SRNSMudApp.Data;
using SRNSMudApp.Services;
using SRNSMudApp.Tests.TestSupport;

using Xunit;

namespace SRNSMudApp.Tests.Components.Tag;

public sealed class TaggingRequestApprovalTests : IAsyncLifetime
{
    private const string ItemOwnerId = "item-owner";
    private const string TagOwnerId = "tag-owner";

    private readonly BunitContext _ctx = new();
    private readonly Mock<ITaggingRequestActions> _actionsMock = new();
    private int _onRequestChangedCount;
    private string _currentUserId = TagOwnerId;

    public TaggingRequestApprovalTests()
    {
        _ = _ctx.Services.AddMudServices().AddMockSrnsServices();
        _ = _ctx.Services.AddScoped(_ => _actionsMock.Object);
        _ctx.Services.AddAuthorizationCore();
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    [Fact]
    public void ApprovingRequest_CallsApproveAsync_AndInvokesCallback()
    {
        var request = CreateTestRequest(1, TagOwnerId);
        _ = _actionsMock.Setup(a => a.CanApprove(request, TagOwnerId)).Returns(true);
        _ = _actionsMock.Setup(a => a.ApproveAsync(request.Id, TagOwnerId)).ReturnsAsync(true);

        IRenderedComponent<TaggingRequestList> cut = RenderList(request);

        cut.Find("[data-testid='tagging-request-approve']").Click();

        _actionsMock.Verify(a => a.ApproveAsync(request.Id, TagOwnerId), Times.Once);
        Assert.Equal(1, _onRequestChangedCount);
    }

    [Fact]
    public void ApproveButton_ShouldBeHidden_WhenCanApproveReturnsFalse()
    {
        var request = CreateTestRequest(1, TagOwnerId);
        _currentUserId = "unrelated-user";
        _ = _actionsMock.Setup(a => a.CanApprove(request, _currentUserId)).Returns(false);

        IRenderedComponent<TaggingRequestList> cut = RenderList(request);

        Assert.Empty(cut.FindAll("[data-testid='tagging-request-approve']"));
    }

    private IRenderedComponent<TaggingRequestList> RenderList(params TaggingRequestEntity[] requests)
    {
        return _ctx.Render<TaggingRequestList>(parameters => parameters
            .Add(p => p.Requests, requests)
            .Add(p => p.OnRequestChanged, () => _onRequestChangedCount++)
            .AddCascadingValue(Task.FromResult(CreateAuthState(_currentUserId))));
    }

    private static TaggingRequestEntity CreateTestRequest(int id, string tagOwnerId) => new()
    {
        Id = id,
        ContractType = "Gratis",
        OwnerId = ItemOwnerId,
        RequesterUserId = ItemOwnerId,
        TagOwnerUserId = tagOwnerId,
        TargetItemId = 10,
        RequestedTagId = 20,
        Status = TradeStatus.Proposed,
        RequestType = TaggingRequestType.Add
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
}