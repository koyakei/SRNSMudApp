namespace SRNSMudApp.Tests.Client;

using System;
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

public class TagDetailPageTests : IAsyncDisposable
{
    private readonly BunitContext _ctx = new();
    private readonly Mock<IItemListApiClient> _apiClientMock = new();

    public TagDetailPageTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddMudServices();
        _ctx.Services.AddScoped(_ => _apiClientMock.Object);
    }

    [Fact]
    public void WhenTagNotFound_RendersNotFoundMessage()
    {
        // Arrange
        _apiClientMock
            .Setup(c => c.GetTagDetailAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TagDetailDto?)null);

        // Act
        IRenderedComponent<TagDetailPage> cut = _ctx.Render<TagDetailPage>(p => p.Add(x => x.TagId, 999));

        // Assert
        Assert.Contains("タグが見つかりませんでした", cut.Markup);
    }

    [Fact]
    public void WhenTagExists_RendersTagDetailAndRelatedData()
    {
        // Arrange
        var relatedItem = new ItemDto(
            Id: 10,
            Content: "関連アイテムのテスト本文",
            OwnerUserName: "bob",
            UpdatedDate: DateTimeOffset.UtcNow,
            IsPrivate: false,
            TagRelations: [],
            ItemKindJson: "{}",
            Score: 5
        );
        var relatedTag = new TagDto(
            Id: 2,
            Name: "子タグ1",
            Content: "子タグの説明",
            OwnerUserName: "alice",
            Score: 3,
            IsPrivate: false
        );
        var pendingRequest = new TaggingRequestDto(
            Id: 100,
            TargetItemId: 10,
            RequestedTagId: 1,
            RequestedTagName: "テスト用タグ",
            RequesterUserId: "user-2",
            RequesterUserName: "requester_bob",
            TagOwnerUserId: "alice",
            ProposedWeight: 2,
            Status: "Pending",
            RequestType: "Add"
        );
        var detail = new TagDetailDto(
            Id: 1,
            Name: "テスト用タグ",
            Content: "テストタグの詳細説明文です",
            OwnerUserId: "alice-id",
            OwnerUserName: "alice",
            Score: 15,
            IsPrivate: false,
            TargetUserGroupName: null,
            IsFollowing: true,
            AutoAcceptIncomingTaggingRequests: true,
            IsLocked: false,
            RelatedItems: [relatedItem],
            RelatedTags: [relatedTag],
            PendingRequests: [pendingRequest]
        );

        _apiClientMock
            .Setup(c => c.GetTagDetailAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        // Act
        IRenderedComponent<TagDetailPage> cut = _ctx.Render<TagDetailPage>(p => p.Add(x => x.TagId, 1));

        // Assert
        Assert.Contains("テスト用タグ", cut.Markup);
        Assert.Contains("テストタグの詳細説明文です", cut.Markup);
        Assert.Contains("15", cut.Markup);
        Assert.Contains("フォロー解除", cut.Markup);
        Assert.Contains("関連アイテム (1)", cut.Markup);
        Assert.Contains("関連タグ (1)", cut.Markup);
        Assert.Contains("リクエスト (1)", cut.Markup);
        Assert.Contains("関連アイテムのテスト本文", cut.Markup);
        Assert.Contains("子タグ1", cut.Markup);
    }

    [Fact]
    public async Task WhenToggleFollowClicked_TogglesFollowStatus()
    {
        // Arrange
        var detail = new TagDetailDto(
            Id: 1,
            Name: "テスト用タグ",
            Content: "説明",
            OwnerUserId: "alice-id",
            OwnerUserName: "alice",
            Score: 10,
            IsPrivate: false,
            TargetUserGroupName: null,
            IsFollowing: false,
            AutoAcceptIncomingTaggingRequests: false,
            IsLocked: false,
            RelatedItems: [],
            RelatedTags: [],
            PendingRequests: []
        );

        _apiClientMock
            .Setup(c => c.GetTagDetailAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);
        _apiClientMock
            .Setup(c => c.ToggleTagFollowAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        IRenderedComponent<TagDetailPage> cut = _ctx.Render<TagDetailPage>(p => p.Add(x => x.TagId, 1));
        Assert.Contains("フォロー", cut.Markup);

        // Act: フォローボタンをクリック
        var followButton = cut.Find("button:contains('フォロー')");
        await cut.InvokeAsync(() => followButton.Click());

        // Assert
        _apiClientMock.Verify(c => c.ToggleTagFollowAsync(1, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Contains("フォロー解除", cut.Markup);
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}