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

public class TagListPageTests : IAsyncDisposable
{
    private readonly BunitContext _ctx = new();
    private readonly Mock<IItemListApiClient> _apiClientMock = new();

    public TagListPageTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddMudServices();
        _ctx.Services.AddScoped(_ => _apiClientMock.Object);
    }

    [Fact]
    public void WhenNoTags_RendersEmptyMessage()
    {
        // Arrange
        _apiClientMock
            .Setup(c => c.GetTagListAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        IRenderedComponent<TagListPage> cut = _ctx.Render<TagListPage>();

        // Assert
        Assert.Contains("該当するタグが見つかりませんでした", cut.Markup);
    }

    [Fact]
    public void WhenTagsExist_RendersTagTable()
    {
        // Arrange
        var tag1 = new TagDto(
            Id: 1,
            Name: "プログラミング",
            Content: "プログラミングに関するタグ",
            OwnerUserName: "alice",
            OwnerUserId: "alice-id",
            Score: 10,
            IsPrivate: false,
            TargetUserGroupName: null
        );

        _apiClientMock
            .Setup(c => c.GetTagListAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([tag1]);

        // Act
        IRenderedComponent<TagListPage> cut = _ctx.Render<TagListPage>();

        // Assert
        Assert.Contains("プログラミング", cut.Markup);
        Assert.Contains("プログラミングに関するタグ", cut.Markup);
        Assert.Contains("alice", cut.Markup);
        Assert.Contains("10", cut.Markup);
    }

    [Fact]
    public async Task WhenVoteTagClicked_CallsApi()
    {
        // Arrange
        var tag1 = new TagDto(
            Id: 1,
            Name: "プログラミング",
            Content: "説明",
            OwnerUserName: "alice",
            OwnerUserId: "alice-id",
            Score: 10,
            IsPrivate: false,
            TargetUserGroupName: null
        );

        _apiClientMock
            .Setup(c => c.GetTagListAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([tag1]);
        _apiClientMock
            .Setup(c => c.VoteTagAsync(1, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        IRenderedComponent<TagListPage> cut = _ctx.Render<TagListPage>();

        // Act
        var upvoteBtn = cut.Find("button[title='Upvote']");
        await cut.InvokeAsync(() => upvoteBtn.Click());

        // Assert
        _apiClientMock.Verify(c => c.VoteTagAsync(1, true, It.IsAny<CancellationToken>()), Times.Once);
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}