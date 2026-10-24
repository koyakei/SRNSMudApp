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

public class UserDetailPageTests : IAsyncDisposable
{
    private readonly BunitContext _ctx = new();
    private readonly Mock<IItemListApiClient> _apiClientMock = new();

    public UserDetailPageTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddMudServices();
        _ctx.Services.AddScoped(_ => _apiClientMock.Object);
    }

    [Fact]
    public void WhenUserNotFound_RendersNotFoundMessage()
    {
        // Arrange
        _apiClientMock
            .Setup(c => c.GetUserDetailAsync("non-existent", It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserDetailDto?)null);

        // Act
        IRenderedComponent<UserDetailPage> cut = _ctx.Render<UserDetailPage>(p => p.Add(x => x.UserId, "non-existent"));

        // Assert
        Assert.Contains("ユーザーが見つかりませんでした", cut.Markup);
    }

    [Fact]
    public void WhenUserExists_RendersUserDetailsItemsAndTags()
    {
        // Arrange
        var item = new ItemDto(
            Id: 10,
            Content: "ユーザーの投稿本文",
            OwnerUserName: "alice",
            UpdatedDate: DateTimeOffset.UtcNow,
            IsPrivate: false,
            TagRelations: [],
            ItemKindJson: "{}",
            Score: 7
        );
        var tag = new TagDto(
            Id: 5,
            Name: "aliceのタグ",
            Content: "タグの説明文",
            OwnerUserName: "alice",
            Score: 4,
            IsPrivate: false
        );
        var userDetail = new UserDetailDto(
            UserId: "user-123",
            UserName: "alice",
            IsBanned: false,
            IsFollowing: true,
            FollowingCount: 12,
            FollowersCount: 34,
            Items: [item],
            Tags: [tag]
        );

        _apiClientMock
            .Setup(c => c.GetUserDetailAsync("user-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(userDetail);

        // Act
        IRenderedComponent<UserDetailPage> cut = _ctx.Render<UserDetailPage>(p => p.Add(x => x.UserId, "user-123"));

        // Assert
        Assert.Contains("alice", cut.Markup);
        Assert.Contains("12", cut.Markup);
        Assert.Contains("34", cut.Markup);
        Assert.Contains("フォロー解除", cut.Markup);
        Assert.Contains("投稿アイテム (1)", cut.Markup);
        Assert.Contains("作成タグ (1)", cut.Markup);
        Assert.Contains("ユーザーの投稿本文", cut.Markup);
        Assert.Contains("aliceのタグ", cut.Markup);
    }

    [Fact]
    public async Task WhenToggleFollowClicked_CallsApi()
    {
        // Arrange
        var userDetail = new UserDetailDto(
            UserId: "user-123",
            UserName: "alice",
            IsBanned: false,
            IsFollowing: false,
            FollowingCount: 0,
            FollowersCount: 5,
            Items: [],
            Tags: []
        );

        _apiClientMock
            .Setup(c => c.GetUserDetailAsync("user-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(userDetail);
        _apiClientMock
            .Setup(c => c.ToggleUserFollowAsync("user-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        IRenderedComponent<UserDetailPage> cut = _ctx.Render<UserDetailPage>(p => p.Add(x => x.UserId, "user-123"));
        Assert.Contains("フォロー", cut.Markup);

        // Act
        var followBtn = cut.Find("button:contains('フォロー')");
        await cut.InvokeAsync(() => followBtn.Click());

        // Assert
        _apiClientMock.Verify(c => c.ToggleUserFollowAsync("user-123", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Contains("フォロー解除", cut.Markup);
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}