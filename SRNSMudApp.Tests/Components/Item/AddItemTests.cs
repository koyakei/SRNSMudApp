using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;

using Bunit;

using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor.Services;

using SRNSMudApp.Components.Item;
using SRNSMudApp.Data;
using SRNSMudApp.Services;
using SRNSMudApp.Tests.TestSupport;

using Xunit;

namespace SRNSMudApp.Tests.Components.Item;

public sealed class AddItemTests : IAsyncLifetime
{
    private const string ExistingUserId = "test-user-id";
    private const string TestContent = "Test Item Content";

    private readonly BunitContext _ctx = new();
    private readonly Mock<IItemCardDataProvider> _itemCardDataMock = new();

    public AddItemTests()
    {
        _ = _ctx.Services.AddMudServices().AddMockSrnsServices();
        _ = _ctx.Services.AddScoped(_ => _itemCardDataMock.Object);

        Claim[] claims = [new(ClaimTypes.NameIdentifier, ExistingUserId), new(ClaimTypes.Name, "testuser")];
        var identity = new ClaimsIdentity(claims, "TestAuthType");
        var authState = new AuthenticationState(new ClaimsPrincipal(identity));
        _ = _ctx.Services.AddCascadingValue(_ => Task.FromResult(authState));
        _ = _ctx.Services.AddAuthorizationCore();

        var storeMock = new Mock<IUserStore<ApplicationUser>>();
        var userManagerMock = new Mock<UserManager<ApplicationUser>>(storeMock.Object, null!, null!, null!, null!,
            null!, null!, null!, null!);
        _ = _ctx.Services.AddScoped(_ => userManagerMock.Object);

        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    [Fact]
    public void Save_SubmitsForm_AndCallsCreateItemAsyncWithCorrectParameters()
    {
        var onItemAddedCalled = false;
        _ = _itemCardDataMock
            .Setup(d => d.CreateItemAsync(It.IsAny<SRNSMudApp.Data.Item>(), It.IsAny<IReadOnlyCollection<int>?>()))
            .Returns(Task.CompletedTask);

        IRenderedComponent<AddItem> cut = _ctx.Render<AddItem>(parameters => parameters
            .Add(p => p.OnItemAdded, () => onItemAddedCalled = true));

        cut.WaitForState(() => cut.FindAll("form").Count > 0);
        cut.Find("textarea").Input(TestContent);
        cut.Find("form").Submit();

        _itemCardDataMock.Verify(d => d.CreateItemAsync(
            It.Is<SRNSMudApp.Data.Item>(i => i.Content == TestContent && i.OwnerId == ExistingUserId),
            It.IsAny<IReadOnlyCollection<int>?>()), Times.Once);

        Assert.True(onItemAddedCalled);
    }

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }
}