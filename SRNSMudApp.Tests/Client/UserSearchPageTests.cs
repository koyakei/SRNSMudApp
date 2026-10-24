namespace SRNSMudApp.Tests.Client;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor.Services;

using SRNSMudApp.Client.Models.Api;
using SRNSMudApp.Client.Pages;
using SRNSMudApp.Client.Services;

using Xunit;

public class UserSearchPageTests : IAsyncDisposable
{
    private readonly BunitContext _ctx = new();
    private readonly Mock<IItemListApiClient> _apiClientMock = new();

    public UserSearchPageTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddMudServices();
        _ctx.Services.AddScoped(_ => _apiClientMock.Object);
    }

    [Fact]
    public void WhenInitialized_LoadsAndRendersUsers()
    {
        // Arrange
        var users = new List<UserDto>
        {
            new("user-1", "alice"),
            new("user-2", "bob")
        };

        _apiClientMock
            .Setup(c => c.SearchUsersAsync(string.Empty, It.IsAny<CancellationToken>()))
            .ReturnsAsync(users);

        // Act
        IRenderedComponent<UserSearchPage> cut = _ctx.Render<UserSearchPage>();

        // Assert
        Assert.Contains("alice", cut.Markup);
        Assert.Contains("bob", cut.Markup);
        Assert.Contains("/User/WasmDetail/user-1", cut.Markup);
        Assert.Contains("/User/WasmDetail/user-2", cut.Markup);
    }

    [Fact]
    public void WhenSearchReturnsNoResults_RendersNoResultsAlert()
    {
        // Arrange
        _apiClientMock
            .Setup(c => c.SearchUsersAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        IRenderedComponent<UserSearchPage> cut = _ctx.Render<UserSearchPage>();

        // Assert
        Assert.Contains("一致するユーザーが見つかりませんでした", cut.Markup);
    }

    [Fact]
    public async Task WhenSearchButtonClicked_InvokesApiClientWithQuery()
    {
        // Arrange
        _apiClientMock
            .Setup(c => c.SearchUsersAsync(string.Empty, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new UserDto("user-1", "alice")]);

        _apiClientMock
            .Setup(c => c.SearchUsersAsync("charlie", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new UserDto("user-3", "charlie")]);

        IRenderedComponent<UserSearchPage> cut = _ctx.Render<UserSearchPage>();
        Assert.Contains("alice", cut.Markup);

        // Act: 入力して検索ボタン押下
        var input = cut.Find("input[data-testid='user-search-input']");
        input.Input("charlie");

        var searchBtn = cut.Find("button[data-testid='user-search-button']");
        await cut.InvokeAsync(() => searchBtn.Click());

        // Assert
        Assert.Contains("charlie", cut.Markup);
        _apiClientMock.Verify(c => c.SearchUsersAsync("charlie", It.IsAny<CancellationToken>()), Times.Once);
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}