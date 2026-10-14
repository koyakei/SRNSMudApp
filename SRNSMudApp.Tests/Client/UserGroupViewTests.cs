namespace SRNSMudApp.Tests.Client;

using System;
using System.Threading;
using System.Threading.Tasks;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Client.Components;
using SRNSMudApp.Client.Models.Api;
using SRNSMudApp.Client.Services;

using Xunit;

public class UserGroupViewTests : IAsyncDisposable
{
    private readonly BunitContext _ctx = new();
    private readonly Mock<IItemListApiClient> _apiClientMock = new();
    private readonly Mock<ISnackbar> _snackbarMock = new();

    public UserGroupViewTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddMudServices();

        _ctx.Services.AddScoped(_ => _apiClientMock.Object);
        _ctx.Services.AddScoped(_ => _snackbarMock.Object);
    }

    [Fact]
    public void WhenNoGroups_RendersEmptyMessage()
    {
        // Arrange
        _apiClientMock
            .Setup(c => c.GetUserGroupsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        IRenderedComponent<UserGroupView> cut = _ctx.Render<UserGroupView>();

        // Assert
        Assert.Contains("所属しているグループはありません", cut.Markup);
    }

    [Fact]
    public void WhenGroupsExist_RendersGroupCards()
    {
        // Arrange
        var groups = new UserGroupDto[]
        {
            new(1, "Alpha Team", "Description of Alpha"),
            new(2, "Beta Team", "Description of Beta")
        };

        _apiClientMock
            .Setup(c => c.GetUserGroupsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(groups);

        // Act
        IRenderedComponent<UserGroupView> cut = _ctx.Render<UserGroupView>();

        // Assert
        Assert.Contains("Alpha Team", cut.Markup);
        Assert.Contains("Beta Team", cut.Markup);
        Assert.Contains("Description of Alpha", cut.Markup);
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}