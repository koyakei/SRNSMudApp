namespace SRNSMudApp.Tests.Client;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Client.Models.Api;
using SRNSMudApp.Client.Pages;
using SRNSMudApp.Client.Services;

using Xunit;

public class ItemDetailPageTests : IAsyncDisposable
{
    private readonly BunitContext _ctx = new();
    private readonly Mock<IItemListApiClient> _apiClientMock = new();

    public ItemDetailPageTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddMudServices();

        _ctx.Services.AddScoped(_ => _apiClientMock.Object);
    }

    [Fact]
    public void WhenItemNotFound_RendersNotFoundMessage()
    {
        // Arrange
        _apiClientMock
            .Setup(c => c.GetItemByIdAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ItemDto?)null);
        _apiClientMock
            .Setup(c => c.GetRepliesAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _apiClientMock
            .Setup(c => c.GetQuotedByItemsAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        IRenderedComponent<ItemDetailPage> cut = _ctx.Render<ItemDetailPage>(p => p.Add(x => x.ItemId, 999));

        // Assert
        Assert.Contains("アイテムが見つかりませんでした", cut.Markup);
    }

    [Fact]
    public void WhenItemExists_RendersContentTagsAndReplies()
    {
        // Arrange
        var item = new ItemDto(
            Id: 42,
            Content: "詳細テスト用のコンテンツです",
            OwnerUserName: "alice",
            UpdatedDate: DateTimeOffset.UtcNow,
            IsPrivate: false,
            TagRelations: [new TagRelationDto(1, "テストタグ", 3, "alice")],
            ItemKindJson: "{}",
            Score: 10,
            ShinjiScore: 1,
            ZenScore: 2,
            BiScore: 3,
            ReplyCount: 1
        );
        var reply = new ItemDto(
            Id: 43,
            Content: "返信メッセージです",
            OwnerUserName: "bob",
            UpdatedDate: DateTimeOffset.UtcNow,
            IsPrivate: false,
            TagRelations: [],
            ItemKindJson: "{}",
            ParentItemId: 42
        );
        var quote = new ItemDto(
            Id: 44,
            Content: "引用投稿メッセージです",
            OwnerUserName: "carol",
            UpdatedDate: DateTimeOffset.UtcNow,
            IsPrivate: false,
            TagRelations: [],
            ItemKindJson: "{}",
            QuotedItemId: 42
        );

        _apiClientMock
            .Setup(c => c.GetItemByIdAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);
        _apiClientMock
            .Setup(c => c.GetRepliesAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync([reply]);
        _apiClientMock
            .Setup(c => c.GetQuotedByItemsAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync([quote]);
        _apiClientMock
            .Setup(c => c.GetTaggingRequestsAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        IRenderedComponent<ItemDetailPage> cut = _ctx.Render<ItemDetailPage>(p => p.Add(x => x.ItemId, 42));

        // Assert
        Assert.Contains("詳細テスト用のコンテンツです", cut.Markup);
        Assert.Contains("テストタグ", cut.Markup);
        Assert.Contains("alice", cut.Markup);
        Assert.Contains("返信メッセージです", cut.Markup);
        Assert.Contains("bob", cut.Markup);
        Assert.Contains("引用投稿メッセージです", cut.Markup);
        Assert.Contains("carol", cut.Markup);
    }

    [Fact]
    public void WhenTaggingRequestsExist_RendersRequestCards()
    {
        // Arrange
        var item = new ItemDto(
            Id: 42,
            Content: "タグ付けリクエストテスト",
            OwnerUserName: "alice",
            UpdatedDate: DateTimeOffset.UtcNow,
            IsPrivate: false,
            TagRelations: [],
            ItemKindJson: "{}",
            Score: 10
        );
        var taggingRequest = new TaggingRequestDto(
            Id: 99,
            TargetItemId: 42,
            RequestedTagId: 10,
            RequestedTagName: "提案されたタグ",
            RequesterUserId: "req-user",
            RequesterUserName: "requester_bob",
            TagOwnerUserId: "alice",
            ProposedWeight: 1,
            Status: "Pending",
            RequestType: "AddTag"
        );

        _apiClientMock
            .Setup(c => c.GetItemByIdAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);
        _apiClientMock
            .Setup(c => c.GetRepliesAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _apiClientMock
            .Setup(c => c.GetQuotedByItemsAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _apiClientMock
            .Setup(c => c.GetTaggingRequestsAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync([taggingRequest]);

        // Act
        IRenderedComponent<ItemDetailPage> cut = _ctx.Render<ItemDetailPage>(p => p.Add(x => x.ItemId, 42));

        // Assert
        Assert.Contains("タグ付けリクエスト (1)", cut.Markup);
        Assert.Contains("提案されたタグ", cut.Markup);
        Assert.Contains("requester_bob", cut.Markup);
        Assert.Contains("承認", cut.Markup);
        Assert.Contains("却下", cut.Markup);
    }

    [Fact]
    public void WhenPendingSplitRequestsExist_RendersSplitRequestCards()
    {
        // Arrange
        var item = new ItemDto(
            Id: 42,
            Content: "分割リクエストテスト",
            OwnerUserName: "alice",
            UpdatedDate: DateTimeOffset.UtcNow,
            IsPrivate: false,
            TagRelations: [],
            ItemKindJson: "{}",
            Score: 10
        );
        var splitRequest = new ItemSplitRequestDto(
            Id: 88,
            OriginalItemId: 42,
            SelectedText: "分割したい抜粋文",
            RequesterUserId: "req-user",
            RequesterUserName: "requester_bob",
            OwnerUserId: "alice",
            Status: "Proposed",
            RejectReason: null,
            CreatedDate: DateTime.UtcNow
        );

        _apiClientMock
            .Setup(c => c.GetItemByIdAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);
        _apiClientMock
            .Setup(c => c.GetRepliesAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _apiClientMock
            .Setup(c => c.GetQuotedByItemsAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _apiClientMock
            .Setup(c => c.GetTaggingRequestsAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _apiClientMock
            .Setup(c => c.GetPendingSplitRequestsAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync([splitRequest]);

        // Act
        IRenderedComponent<ItemDetailPage> cut = _ctx.Render<ItemDetailPage>(p => p.Add(x => x.ItemId, 42));

        // Assert
        Assert.Contains("保留中の分割リクエスト (1)", cut.Markup);
        Assert.Contains("分割したい抜粋文", cut.Markup);
        Assert.Contains("requester_bob", cut.Markup);
        Assert.Contains("テキストを新規アイテムに分割", cut.Markup);
        Assert.Contains("直接分割（所有者）", cut.Markup);
        Assert.Contains("分割リクエスト", cut.Markup);
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}