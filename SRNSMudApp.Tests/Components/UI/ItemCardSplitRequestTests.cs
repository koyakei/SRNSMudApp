using Bunit;

using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.JSInterop;

using Moq;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.Item;
using SRNSMudApp.Components.UI;
using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;
using SRNSMudApp.Services.Dialogs;
using SRNSMudApp.Tests.TestSupport;

namespace SRNSMudApp.Tests.Components.UI;

using DataItem = SRNSMudApp.Data.Item;
using DataTag = SRNSMudApp.Data.Tag;

/// <summary>
///     ItemCard における他ユーザーItemに対するテキスト分割リクエストボタンの表示、
///     保留中リクエストの承認・却下・取り下げ表示、テキスト未選択時の警告挙動を検証する bUnit テスト。
/// </summary>
public sealed class ItemCardSplitRequestTests : IAsyncLifetime
{
    private const string CurrentUserId = "current-user-id";
    private const string OtherUserId = "other-user-id";
    private readonly BunitContext _ctx = new();
    private readonly Mock<IDialogLauncher> _dialogLauncherMock = new();
    private readonly Mock<IItemCardDataProvider> _itemCardDataMock = new();
    private readonly Mock<IItemTagService> _itemTagServiceMock = new();
    private readonly Mock<IItemSplitService> _itemSplitServiceMock = new();
    private readonly Mock<IItemQuoteService> _itemQuoteServiceMock = new();
    private readonly Mock<IItemCardVoteCoordinator> _voteCoordinatorMock = new();

    public ItemCardSplitRequestTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices().AddMockSrnsServices();
        _ctx.Services.AddAuthorizationCore();

        AuthenticationState authState = BunitTestSetup.CreateAuthState(CurrentUserId);
        Mock<AuthenticationStateProvider> authMock = new();
        _ = authMock.Setup(p => p.GetAuthenticationStateAsync()).ReturnsAsync(authState);
        _ctx.Services.AddScoped(_ => authMock.Object);

        _ctx.Services.RemoveAll<IDialogLauncher>();
        _ctx.Services.AddScoped(_ => _dialogLauncherMock.Object);

        _ctx.Services.RemoveAll<IItemCardDataProvider>();
        _ctx.Services.AddScoped(_ => _itemCardDataMock.Object);

        _ctx.Services.RemoveAll<IItemTagService>();
        _ctx.Services.AddScoped(_ => _itemTagServiceMock.Object);

        _ctx.Services.RemoveAll<IItemSplitService>();
        _ctx.Services.AddScoped(_ => _itemSplitServiceMock.Object);

        _ctx.Services.RemoveAll<IItemQuoteService>();
        _ctx.Services.AddScoped(_ => _itemQuoteServiceMock.Object);

        _ctx.Services.RemoveAll<IItemCardVoteCoordinator>();
        _ctx.Services.AddScoped(_ => _voteCoordinatorMock.Object);

        _ctx.Render<MudPopoverProvider>();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    [Fact]
    public void ItemCard_WhenViewerIsNotOwner_RendersRequestSplitButton()
    {
        // Arrange
        var item = new DataItem
        {
            Id = 10,
            Content = "他ユーザーのアイテム本文",
            OwnerId = OtherUserId,
            TagRelations = []
        };

        _itemSplitServiceMock.Setup(s => s.GetPendingSplitRequestsForOriginalItemAsync(10, default))
            .ReturnsAsync([]);

        // Act
        IRenderedComponent<ItemCard> cut = _ctx.Render<ItemCard>(parameters => parameters
            .Add(p => p.Item, item)
            .Add(p => p.CurrentUserId, CurrentUserId));

        // Assert: 非所有者のため「分割リクエスト」ボタンが表示される
        Assert.Contains("選択テキストを別アイテムに分割するリクエストを送信", cut.Markup);
        // 所有者用の直接「選択テキストを別アイテムに分割」は表示されない
        Assert.DoesNotContain("title=\"選択テキストを別アイテムに分割\"", cut.Markup);
    }

    [Fact]
    public void ItemCard_WhenViewerIsOwner_RendersDirectSplitButton()
    {
        // Arrange
        var item = new DataItem
        {
            Id = 11,
            Content = "自分のアイテム本文",
            OwnerId = CurrentUserId,
            TagRelations = []
        };

        _itemSplitServiceMock.Setup(s => s.GetPendingSplitRequestsForOriginalItemAsync(11, default))
            .ReturnsAsync([]);

        // Act
        IRenderedComponent<ItemCard> cut = _ctx.Render<ItemCard>(parameters => parameters
            .Add(p => p.Item, item)
            .Add(p => p.CurrentUserId, CurrentUserId));

        // Assert: 所有者のため直接分割ボタンが表示される
        Assert.Contains("title=\"選択テキストを別アイテムに分割\"", cut.Markup);
        Assert.DoesNotContain("選択テキストを別アイテムに分割するリクエストを送信", cut.Markup);
    }

    [Fact]
    public void ItemCard_WhenPendingRequestsExist_AndUserIsOwner_RendersApproveAndRejectButtons()
    {
        // Arrange
        var item = new DataItem
        {
            Id = 12,
            Content = "元アイテムの本文テスト",
            OwnerId = CurrentUserId,
            TagRelations = []
        };

        var requester = new ApplicationUser { Id = OtherUserId, UserName = "Alice" };
        var pendingRequest = new ItemSplitRequest
        {
            Id = 101,
            OriginalItemId = 12,
            RequesterUserId = OtherUserId,
            RequesterUser = requester,
            OwnerUserId = CurrentUserId,
            OwnerId = OtherUserId,
            SelectedText = "本文テスト",
            Status = TradeStatus.Proposed
        };

        _itemSplitServiceMock.Setup(s => s.GetPendingSplitRequestsForOriginalItemAsync(12, default))
            .ReturnsAsync([pendingRequest]);

        // Act
        IRenderedComponent<ItemCard> cut = _ctx.Render<ItemCard>(parameters => parameters
            .Add(p => p.Item, item)
            .Add(p => p.CurrentUserId, CurrentUserId));

        // Assert
        Assert.Contains("Alice", cut.Markup);
        Assert.Contains("本文テスト", cut.Markup);
        Assert.Contains("承認", cut.Markup);
        Assert.Contains("却下", cut.Markup);
    }

    [Fact]
    public void ItemCard_WhenPendingRequestsExist_AndUserIsRequester_RendersCancelButton()
    {
        // Arrange
        var item = new DataItem
        {
            Id = 13,
            Content = "元アイテムの本文テスト",
            OwnerId = OtherUserId,
            TagRelations = []
        };

        var requester = new ApplicationUser { Id = CurrentUserId, UserName = "Bob" };
        var pendingRequest = new ItemSplitRequest
        {
            Id = 102,
            OriginalItemId = 13,
            RequesterUserId = CurrentUserId,
            RequesterUser = requester,
            OwnerUserId = OtherUserId,
            OwnerId = CurrentUserId,
            SelectedText = "本文テスト",
            Status = TradeStatus.Proposed
        };

        _itemSplitServiceMock.Setup(s => s.GetPendingSplitRequestsForOriginalItemAsync(13, default))
            .ReturnsAsync([pendingRequest]);

        // Act
        IRenderedComponent<ItemCard> cut = _ctx.Render<ItemCard>(parameters => parameters
            .Add(p => p.Item, item)
            .Add(p => p.CurrentUserId, CurrentUserId));

        // Assert
        Assert.Contains("Bob", cut.Markup);
        Assert.Contains("本文テスト", cut.Markup);
        Assert.Contains("取り下げ", cut.Markup);
        // 他人のアイテムなので承認・却下ボタンは表示されない
        Assert.DoesNotContain("承認", cut.Markup);
    }

    [Fact]
    public async Task ItemCard_WhenRequestSplitClicked_AndNoTextSelected_ShowsWarningSnackbar()
    {
        // Arrange
        var item = new DataItem
        {
            Id = 14,
            Content = "他ユーザーのアイテム本文",
            OwnerId = OtherUserId,
            TagRelations = []
        };

        _itemSplitServiceMock.Setup(s => s.GetPendingSplitRequestsForOriginalItemAsync(14, default))
            .ReturnsAsync([]);

        // selectionHelper.getSelectedText が空文字列を返すように設定
        _ctx.JSInterop.Setup<string>("selectionHelper.getSelectedText").SetResult("");

        IRenderedComponent<ItemCard> cut = _ctx.Render<ItemCard>(parameters => parameters
            .Add(p => p.Item, item)
            .Add(p => p.CurrentUserId, CurrentUserId));

        // Act: 分割リクエストボタンをクリック
        var splitBtn = cut.Find("button[title='選択テキストを別アイテムに分割するリクエストを送信']");
        await cut.InvokeAsync(() => splitBtn.Click());

        // Assert
        ISnackbar snackbar = _ctx.Services.GetRequiredService<ISnackbar>();
        Assert.Contains(snackbar.ShownSnackbars, s => s.Message.ToString().Contains("分割するテキストが選択されていません"));
    }

    [Fact]
    public async Task ItemCard_WhenQuoteClicked_AndTextSelectedOnNonOwnedItem_TriggersSplitRequestWithQuote()
    {
        // Arrange
        var item = new DataItem
        {
            Id = 15,
            Content = "他ユーザーのアイテム本文の一部を切り抜く",
            OwnerId = OtherUserId,
            TagRelations = []
        };

        _itemSplitServiceMock.Setup(s => s.GetPendingSplitRequestsForOriginalItemAsync(15, default))
            .ReturnsAsync([]);

        // selectionHelper.getSelectedText が選択テキストを返すように設定
        _ctx.JSInterop.Setup<string>("selectionHelper.getSelectedText").SetResult("一部を切り抜く");

        var dialogRef = new Mock<IDialogReference>();
        dialogRef.Setup(r => r.Result).ReturnsAsync(DialogResult.Ok(new SplitRequestWithQuoteDialogResult(99)));
        _dialogLauncherMock
            .Setup(l => l.ShowAsync(typeof(SplitRequestWithQuoteDialog), "アイテム分割リクエストの送信", It.IsAny<DialogParameters>(), It.IsAny<DialogOptions>()))
            .ReturnsAsync(dialogRef.Object);

        var splitRequest = new ItemSplitRequest
        {
            Id = 201,
            OriginalItemId = 15,
            SelectedText = "一部を切り抜く",
            RequesterUserId = CurrentUserId,
            OwnerUserId = OtherUserId,
            OwnerId = CurrentUserId,
            Status = TradeStatus.Proposed,
            RequesterQuoteItemId = 99
        };

        _itemSplitServiceMock.Setup(s => s.RequestSplitAsync(15, "一部を切り抜く", CurrentUserId, 99, default))
            .ReturnsAsync(new Success<ItemSplitRequest>(splitRequest));

        IRenderedComponent<ItemCard> cut = _ctx.Render<ItemCard>(parameters => parameters
            .Add(p => p.Item, item)
            .Add(p => p.CurrentUserId, CurrentUserId));

        // Act: 引用ボタンをクリック
        var quoteBtn = cut.Find($"[data-testid='quote-button-{item.Id}']");
        await cut.InvokeAsync(() => quoteBtn.Click());

        // Assert: SplitRequestWithQuoteDialog が呼び出され、RequestSplitAsync に渡されたこと
        _dialogLauncherMock.Verify(l => l.ShowAsync(
            typeof(SplitRequestWithQuoteDialog),
            "アイテム分割リクエストの送信",
            It.IsAny<DialogParameters>(),
            It.IsAny<DialogOptions>()), Times.Once);

        _itemSplitServiceMock.Verify(s => s.RequestSplitAsync(
            15, "一部を切り抜く", CurrentUserId, 99, default), Times.Once);
    }

    [Fact]
    public async Task ItemCard_WhenQuoteClicked_OnOwnedItem_OpensStandardQuoteDialog()
    {
        // Arrange: 自分のアイテム（OwnerId == CurrentUserId）
        var item = new DataItem
        {
            Id = 16,
            Content = "自分のアイテム本文の一部",
            OwnerId = CurrentUserId,
            TagRelations = []
        };

        _itemSplitServiceMock.Setup(s => s.GetPendingSplitRequestsForOriginalItemAsync(16, default))
            .ReturnsAsync([]);

        // テキスト選択があっても自分のアイテムなら通常の引用ダイアログが開く
        _ctx.JSInterop.Setup<string>("selectionHelper.getSelectedText").SetResult("一部");

        var dialogRef = new Mock<IDialogReference>();
        dialogRef.Setup(r => r.Result).ReturnsAsync(DialogResult.Cancel());
        _dialogLauncherMock
            .Setup(l => l.ShowAsync(typeof(QuoteItemDialog), "引用して投稿", It.IsAny<DialogParameters>(), It.IsAny<DialogOptions>()))
            .ReturnsAsync(dialogRef.Object);

        IRenderedComponent<ItemCard> cut = _ctx.Render<ItemCard>(parameters => parameters
            .Add(p => p.Item, item)
            .Add(p => p.CurrentUserId, CurrentUserId));

        // Act: 引用ボタンをクリック
        var quoteBtn = cut.Find($"[data-testid='quote-button-{item.Id}']");
        await cut.InvokeAsync(() => quoteBtn.Click());

        // Assert: QuoteItemDialog が開かれ、SplitRequestWithQuoteDialog は開かれないこと
        _dialogLauncherMock.Verify(l => l.ShowAsync(
            typeof(QuoteItemDialog),
            "引用して投稿",
            It.IsAny<DialogParameters>(),
            It.IsAny<DialogOptions>()), Times.Once);

        _dialogLauncherMock.Verify(l => l.ShowAsync(
            typeof(SplitRequestWithQuoteDialog),
            It.IsAny<string>(),
            It.IsAny<DialogParameters>(),
            It.IsAny<DialogOptions>()), Times.Never);
    }

    [Fact]
    public async Task ItemCard_WhenQuoteClicked_WithoutTextSelection_OpensStandardQuoteDialog()
    {
        // Arrange: 他人のアイテムだがテキスト選択なし
        var item = new DataItem
        {
            Id = 17,
            Content = "他人のアイテム本文",
            OwnerId = OtherUserId,
            TagRelations = []
        };

        _itemSplitServiceMock.Setup(s => s.GetPendingSplitRequestsForOriginalItemAsync(17, default))
            .ReturnsAsync([]);

        _ctx.JSInterop.Setup<string>("selectionHelper.getSelectedText").SetResult("");

        var dialogRef = new Mock<IDialogReference>();
        dialogRef.Setup(r => r.Result).ReturnsAsync(DialogResult.Cancel());
        _dialogLauncherMock
            .Setup(l => l.ShowAsync(typeof(QuoteItemDialog), "引用して投稿", It.IsAny<DialogParameters>(), It.IsAny<DialogOptions>()))
            .ReturnsAsync(dialogRef.Object);

        IRenderedComponent<ItemCard> cut = _ctx.Render<ItemCard>(parameters => parameters
            .Add(p => p.Item, item)
            .Add(p => p.CurrentUserId, CurrentUserId));

        // Act: 引用ボタンをクリック
        var quoteBtn = cut.Find($"[data-testid='quote-button-{item.Id}']");
        await cut.InvokeAsync(() => quoteBtn.Click());

        // Assert: QuoteItemDialog が開かれ、SplitRequestWithQuoteDialog は開かれないこと
        _dialogLauncherMock.Verify(l => l.ShowAsync(
            typeof(QuoteItemDialog),
            "引用して投稿",
            It.IsAny<DialogParameters>(),
            It.IsAny<DialogOptions>()), Times.Once);

        _dialogLauncherMock.Verify(l => l.ShowAsync(
            typeof(SplitRequestWithQuoteDialog),
            It.IsAny<string>(),
            It.IsAny<DialogParameters>(),
            It.IsAny<DialogOptions>()), Times.Never);
    }

    [Fact]
    public async Task ItemCard_WhenReactionClicked_WithTextSelectionOnOtherUserItem_TriggersQuoteAndReactionAndSplitRequest()
    {
        // Arrange: 他ユーザーのアイテムかつテキスト選択あり
        var item = new DataItem
        {
            Id = 18,
            Content = "他ユーザーのアイテム本文の一部をリアクション",
            OwnerId = OtherUserId,
            TagRelations = []
        };

        _itemSplitServiceMock.Setup(s => s.GetPendingSplitRequestsForOriginalItemAsync(18, default))
            .ReturnsAsync([]);

        _ctx.JSInterop.Setup<string>("selectionHelper.getSelectedText").SetResult("一部をリアクション");

        var quoteItem = new DataItem
        {
            Id = 88,
            OwnerId = CurrentUserId,
            Content = "一部をリアクション",
            QuotedItemId = 18
        };
        _itemQuoteServiceMock
            .Setup(q => q.CreateQuoteItemAsync(18, "一部をリアクション", CurrentUserId, null, default))
            .ReturnsAsync(quoteItem);

        var voteResult = new ItemVoteResult(ItemVoteAction.Added, 201, 1);
        _voteCoordinatorMock
            .Setup(v => v.ToggleReactionAsync(88, CurrentUserId, ReactionTagNames.Shinji, 1, null, It.IsAny<IReadOnlyList<DataTag>>(), It.IsAny<Func<Task>?>()))
            .ReturnsAsync(voteResult);

        var splitRequest = new ItemSplitRequest
        {
            Id = 301,
            OwnerId = CurrentUserId,
            OriginalItemId = 18,
            SelectedText = "一部をリアクション",
            RequesterUserId = CurrentUserId,
            RequesterQuoteItemId = 88
        };
        _itemSplitServiceMock
            .Setup(s => s.RequestSplitAsync(18, "一部をリアクション", CurrentUserId, 88, default))
            .ReturnsAsync(new Success<ItemSplitRequest>(splitRequest));

        var dialogRef = new Mock<IDialogReference>();
        dialogRef.Setup(r => r.Result).ReturnsAsync(DialogResult.Cancel());
        _dialogLauncherMock
            .Setup(l => l.ShowAsync(typeof(ReactionCommentDialog), "リアクションを追加", It.IsAny<DialogParameters>(), It.IsAny<DialogOptions>()))
            .ReturnsAsync(dialogRef.Object);

        IRenderedComponent<ItemCard> cut = _ctx.Render<ItemCard>(parameters => parameters
            .Add(p => p.Item, item)
            .Add(p => p.CurrentUserId, CurrentUserId));

        // Act: リアクション（真実）の Upvote ボタンをクリック
        var upvoteBtn = cut.Find("[data-testid='reaction-shinji-upvote']");
        await cut.InvokeAsync(() => upvoteBtn.Click());

        // Assert: 引用作成、引用アイテムへのリアクション投票、分割リクエスト送信、コメントダイアログ呼び出しが順次行われること
        _itemQuoteServiceMock.Verify(q => q.CreateQuoteItemAsync(18, "一部をリアクション", CurrentUserId, null, default), Times.Once);
        _voteCoordinatorMock.Verify(v => v.ToggleReactionAsync(88, CurrentUserId, ReactionTagNames.Shinji, 1, null, It.IsAny<IReadOnlyList<DataTag>>(), It.IsAny<Func<Task>?>()), Times.Once);
        _itemSplitServiceMock.Verify(s => s.RequestSplitAsync(18, "一部をリアクション", CurrentUserId, 88, default), Times.Once);
        _dialogLauncherMock.Verify(l => l.ShowAsync(typeof(ReactionCommentDialog), "リアクションを追加", It.IsAny<DialogParameters>(), It.IsAny<DialogOptions>()), Times.Once);

        // 元アイテム（Id = 18）に対する直接投票は行われないこと
        _voteCoordinatorMock.Verify(v => v.ToggleReactionAsync(18, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<IReadOnlyList<DataTag>>(), It.IsAny<Func<Task>?>()), Times.Never);
    }

    [Fact]
    public async Task ItemCard_WhenReactionClicked_WithoutTextSelection_TriggersNormalReaction()
    {
        // Arrange: 他ユーザーのアイテムだがテキスト選択なし
        var item = new DataItem
        {
            Id = 19,
            Content = "他ユーザーのアイテム本文",
            OwnerId = OtherUserId,
            TagRelations = []
        };

        _itemSplitServiceMock.Setup(s => s.GetPendingSplitRequestsForOriginalItemAsync(19, default))
            .ReturnsAsync([]);

        _ctx.JSInterop.Setup<string>("selectionHelper.getSelectedText").SetResult("");

        var voteResult = new ItemVoteResult(ItemVoteAction.Added, 202, 1);
        _voteCoordinatorMock
            .Setup(v => v.ToggleReactionAsync(19, CurrentUserId, ReactionTagNames.Shinji, 1, null, It.IsAny<IReadOnlyList<DataTag>>(), It.IsAny<Func<Task>?>()))
            .ReturnsAsync(voteResult);

        var dialogRef = new Mock<IDialogReference>();
        dialogRef.Setup(r => r.Result).ReturnsAsync(DialogResult.Cancel());
        _dialogLauncherMock
            .Setup(l => l.ShowAsync(typeof(ReactionCommentDialog), "リアクションを追加", It.IsAny<DialogParameters>(), It.IsAny<DialogOptions>()))
            .ReturnsAsync(dialogRef.Object);

        IRenderedComponent<ItemCard> cut = _ctx.Render<ItemCard>(parameters => parameters
            .Add(p => p.Item, item)
            .Add(p => p.CurrentUserId, CurrentUserId));

        // Act: リアクション（真実）の Upvote ボタンをクリック
        var upvoteBtn = cut.Find("[data-testid='reaction-shinji-upvote']");
        await cut.InvokeAsync(() => upvoteBtn.Click());

        // Assert: 元アイテムへの直接投票が行われること
        _voteCoordinatorMock.Verify(v => v.ToggleReactionAsync(19, CurrentUserId, ReactionTagNames.Shinji, 1, null, It.IsAny<IReadOnlyList<DataTag>>(), It.IsAny<Func<Task>?>()), Times.Once);

        // 引用作成や分割リクエストは行われないこと
        _itemQuoteServiceMock.Verify(q => q.CreateQuoteItemAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), null, default), Times.Never);
        _itemSplitServiceMock.Verify(s => s.RequestSplitAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), default), Times.Never);
    }

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }
}