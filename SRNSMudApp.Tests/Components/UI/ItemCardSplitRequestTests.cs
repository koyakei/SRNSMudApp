using Bunit;

using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Moq;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.Item;
using SRNSMudApp.Components.UI;
using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;
using SRNSMudApp.Services.Dialogs;
using SRNSMudApp.Tests.TestSupport;

namespace SRNSMudApp.Tests.Components.UI;

using DataItem = SRNSMudApp.Data.Item;
using DataTag = SRNSMudApp.Data.Tag;

/// <summary>
///     ItemCard におけるテキスト分割・分割リクエスト・保留中リクエスト関連の
///     UI 表示およびイベントハンドラーの結線を検証する bUnit テスト。
///     ドメインロジックや多段階オーケストレーションは <see cref="IItemCardSplitCoordinator" />
///     および <see cref="ItemCardSplitCoordinatorTests" /> (単体テスト) に寄託し、
///     本クラスではコンポーネントの UI 責務に純化して検証する。
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
    private readonly Mock<IItemCardVoteCoordinator> _voteCoordinatorMock = new();
    private readonly Mock<IItemCardSplitCoordinator> _splitCoordinatorMock = new();
    private readonly Mock<IItemCardTagCoordinator> _tagCoordinatorMock = new();

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

        _ctx.Services.RemoveAll<IItemCardVoteCoordinator>();
        _ctx.Services.AddScoped(_ => _voteCoordinatorMock.Object);

        _ctx.Services.RemoveAll<IItemCardSplitCoordinator>();
        _ctx.Services.AddScoped(_ => _splitCoordinatorMock.Object);

        _ctx.Services.RemoveAll<IItemCardTagCoordinator>();
        _ctx.Services.AddScoped(_ => _tagCoordinatorMock.Object);

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
    public async Task ItemCard_WhenDirectSplitClicked_CallsCoordinatorSplitSelectionAsync()
    {
        // Arrange: 所有者のアイテム
        var item = new DataItem
        {
            Id = 14,
            Content = "自分のアイテム本文",
            OwnerId = CurrentUserId,
            TagRelations = []
        };

        _itemSplitServiceMock.Setup(s => s.GetPendingSplitRequestsForOriginalItemAsync(14, default))
            .ReturnsAsync([]);

        _splitCoordinatorMock.Setup(c => c.SplitSelectionAsync(item, CurrentUserId))
            .ReturnsAsync(true);

        IRenderedComponent<ItemCard> cut = _ctx.Render<ItemCard>(parameters => parameters
            .Add(p => p.Item, item)
            .Add(p => p.CurrentUserId, CurrentUserId));

        // Act: 直接分割ボタンをクリック
        var splitBtn = cut.Find("button[title='選択テキストを別アイテムに分割']");
        await cut.InvokeAsync(() => splitBtn.Click());

        // Assert
        _splitCoordinatorMock.Verify(c => c.SplitSelectionAsync(item, CurrentUserId), Times.Once);
    }

    [Fact]
    public async Task ItemCard_WhenRequestSplitClicked_CallsCoordinatorRequestSplitSelectionAsync()
    {
        // Arrange: 他ユーザーのアイテム
        var item = new DataItem
        {
            Id = 15,
            Content = "他ユーザーのアイテム本文",
            OwnerId = OtherUserId,
            TagRelations = []
        };

        _itemSplitServiceMock.Setup(s => s.GetPendingSplitRequestsForOriginalItemAsync(15, default))
            .ReturnsAsync([]);

        _splitCoordinatorMock.Setup(c => c.RequestSplitSelectionAsync(item, CurrentUserId))
            .ReturnsAsync(new ItemSplitRequest { OwnerId = CurrentUserId });

        IRenderedComponent<ItemCard> cut = _ctx.Render<ItemCard>(parameters => parameters
            .Add(p => p.Item, item)
            .Add(p => p.CurrentUserId, CurrentUserId));

        // Act: 分割リクエストボタンをクリック
        var splitBtn = cut.Find("button[title='選択テキストを別アイテムに分割するリクエストを送信']");
        await cut.InvokeAsync(() => splitBtn.Click());

        // Assert
        _splitCoordinatorMock.Verify(c => c.RequestSplitSelectionAsync(item, CurrentUserId), Times.Once);
    }

    [Fact]
    public async Task ItemCard_WhenQuoteClicked_AndValidSelectedTextExists_CallsCoordinatorRequestSplitSelectionAsync()
    {
        // Arrange: 他ユーザーのアイテムで有効なテキスト選択がある場合
        var item = new DataItem
        {
            Id = 16,
            Content = "他ユーザーのアイテム本文の一部を切り抜く",
            OwnerId = OtherUserId,
            TagRelations = []
        };

        _itemSplitServiceMock.Setup(s => s.GetPendingSplitRequestsForOriginalItemAsync(16, default))
            .ReturnsAsync([]);

        _splitCoordinatorMock.Setup(c => c.GetValidSelectedTextForSplitAsync(item, CurrentUserId))
            .ReturnsAsync("一部を切り抜く");

        _splitCoordinatorMock.Setup(c => c.RequestSplitSelectionAsync(item, CurrentUserId))
            .ReturnsAsync(new ItemSplitRequest { OwnerId = CurrentUserId });

        IRenderedComponent<ItemCard> cut = _ctx.Render<ItemCard>(parameters => parameters
            .Add(p => p.Item, item)
            .Add(p => p.CurrentUserId, CurrentUserId));

        // Act: 引用ボタンをクリック
        var quoteBtn = cut.Find($"[data-testid='quote-button-{item.Id}']");
        await cut.InvokeAsync(() => quoteBtn.Click());

        // Assert: Coordinator の RequestSplitSelectionAsync が呼ばれ、通常の引用ダイアログは開かれないこと
        _splitCoordinatorMock.Verify(c => c.GetValidSelectedTextForSplitAsync(item, CurrentUserId), Times.Once);
        _splitCoordinatorMock.Verify(c => c.RequestSplitSelectionAsync(item, CurrentUserId), Times.Once);
        _dialogLauncherMock.Verify(l => l.ShowAsync(
            typeof(QuoteItemDialog),
            It.IsAny<string>(),
            It.IsAny<DialogParameters>(),
            It.IsAny<DialogOptions>()), Times.Never);
    }

    [Fact]
    public async Task ItemCard_WhenQuoteClicked_WithoutValidSelectedText_OpensStandardQuoteDialog()
    {
        // Arrange: 有効なテキスト選択がない場合
        var item = new DataItem
        {
            Id = 17,
            Content = "通常の引用アイテム本文",
            OwnerId = OtherUserId,
            TagRelations = []
        };

        _itemSplitServiceMock.Setup(s => s.GetPendingSplitRequestsForOriginalItemAsync(17, default))
            .ReturnsAsync([]);

        _splitCoordinatorMock.Setup(c => c.GetValidSelectedTextForSplitAsync(item, CurrentUserId))
            .ReturnsAsync((string?)null);

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

        // Assert: 通常の QuoteItemDialog が開かれ、分割リクエストは呼ばれないこと
        _dialogLauncherMock.Verify(l => l.ShowAsync(
            typeof(QuoteItemDialog),
            "引用して投稿",
            It.IsAny<DialogParameters>(),
            It.IsAny<DialogOptions>()), Times.Once);

        _splitCoordinatorMock.Verify(c => c.RequestSplitSelectionAsync(It.IsAny<DataItem>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ItemCard_WhenReactionClicked_WithValidSelectedText_CallsCoordinatorRequestSplitWithReactionAsync()
    {
        // Arrange: 有効な選択テキストがある場合
        var item = new DataItem
        {
            Id = 18,
            Content = "他ユーザーのアイテム本文の一部をリアクション",
            OwnerId = OtherUserId,
            TagRelations = []
        };

        _itemSplitServiceMock.Setup(s => s.GetPendingSplitRequestsForOriginalItemAsync(18, default))
            .ReturnsAsync([]);

        _splitCoordinatorMock.Setup(c => c.GetValidSelectedTextForSplitAsync(item, CurrentUserId))
            .ReturnsAsync("一部をリアクション");

        var quoteItem = new DataItem { Id = 88, Content = "一部をリアクション", OwnerId = CurrentUserId };
        var voteResult = new ItemVoteResult(ItemVoteAction.Added, 201, 1);
        var splitRequest = new ItemSplitRequest { Id = 301, SelectedText = "一部をリアクション", OwnerId = CurrentUserId };
        var splitWithReactionResult = new ItemSplitWithReactionResult(splitRequest, voteResult, quoteItem);

        _splitCoordinatorMock.Setup(c => c.RequestSplitWithReactionAsync(
                item, CurrentUserId, ReactionTagNames.Shinji, 1, null, It.IsAny<IReadOnlyList<DataTag>>(), It.IsAny<Func<Task>?>(), "一部をリアクション"))
            .ReturnsAsync(splitWithReactionResult);

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

        // Assert: Coordinator の RequestSplitWithReactionAsync が呼ばれること
        _splitCoordinatorMock.Verify(c => c.RequestSplitWithReactionAsync(
            item, CurrentUserId, ReactionTagNames.Shinji, 1, null, It.IsAny<IReadOnlyList<DataTag>>(), It.IsAny<Func<Task>?>(), "一部をリアクション"), Times.Once);

        // 元アイテムへの通常投票は直接呼ばれないこと
        _voteCoordinatorMock.Verify(v => v.ToggleReactionAsync(
            item.Id, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<IReadOnlyList<DataTag>>(), It.IsAny<Func<Task>?>()), Times.Never);
    }

    [Fact]
    public async Task ItemCard_WhenReactionClicked_WithoutValidSelectedText_TriggersNormalReaction()
    {
        // Arrange: 有効な選択テキストがない場合
        var item = new DataItem
        {
            Id = 19,
            Content = "他ユーザーのアイテム本文",
            OwnerId = OtherUserId,
            TagRelations = []
        };

        _itemSplitServiceMock.Setup(s => s.GetPendingSplitRequestsForOriginalItemAsync(19, default))
            .ReturnsAsync([]);

        _splitCoordinatorMock.Setup(c => c.GetValidSelectedTextForSplitAsync(item, CurrentUserId))
            .ReturnsAsync((string?)null);

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

        // Coordinator 経由の分割リクエストは行われないこと
        _splitCoordinatorMock.Verify(c => c.RequestSplitWithReactionAsync(
            It.IsAny<DataItem>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<IReadOnlyList<DataTag>>(), It.IsAny<Func<Task>?>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task ItemCard_WhenApproveSplitRequestClicked_CallsCoordinatorApproveSplitRequestAsync()
    {
        // Arrange
        var item = new DataItem
        {
            Id = 30,
            Content = "元アイテム本文テスト",
            OwnerId = CurrentUserId,
            TagRelations = []
        };

        var requester = new ApplicationUser { Id = OtherUserId, UserName = "Alice" };
        var pendingRequest = new ItemSplitRequest
        {
            Id = 301,
            OriginalItemId = 30,
            RequesterUserId = OtherUserId,
            RequesterUser = requester,
            OwnerUserId = CurrentUserId,
            OwnerId = OtherUserId,
            SelectedText = "本文テスト",
            Status = TradeStatus.Proposed
        };

        _itemSplitServiceMock.Setup(s => s.GetPendingSplitRequestsForOriginalItemAsync(30, default))
            .ReturnsAsync([pendingRequest]);

        _splitCoordinatorMock.Setup(c => c.ApproveSplitRequestAsync(item, pendingRequest, CurrentUserId))
            .ReturnsAsync(true);

        IRenderedComponent<ItemCard> cut = _ctx.Render<ItemCard>(parameters => parameters
            .Add(p => p.Item, item)
            .Add(p => p.CurrentUserId, CurrentUserId));

        // Act: 承認ボタンをクリック
        var approveBtn = cut.Find($"[data-testid='approve-split-request-{pendingRequest.Id}']");
        await cut.InvokeAsync(() => approveBtn.Click());

        // Assert: Coordinator の ApproveSplitRequestAsync が呼ばれること
        _splitCoordinatorMock.Verify(c => c.ApproveSplitRequestAsync(item, pendingRequest, CurrentUserId), Times.Once);
        _itemSplitServiceMock.Verify(s => s.GetPendingSplitRequestsForOriginalItemAsync(30, default), Times.AtLeast(2));
    }

    [Fact]
    public async Task ItemCard_WhenRejectSplitRequestClicked_CallsCoordinatorRejectSplitRequestAsync()
    {
        // Arrange
        var item = new DataItem
        {
            Id = 31,
            Content = "元アイテム本文テスト",
            OwnerId = CurrentUserId,
            TagRelations = []
        };

        var requester = new ApplicationUser { Id = OtherUserId, UserName = "Alice" };
        var pendingRequest = new ItemSplitRequest
        {
            Id = 302,
            OriginalItemId = 31,
            RequesterUserId = OtherUserId,
            RequesterUser = requester,
            OwnerUserId = CurrentUserId,
            OwnerId = OtherUserId,
            SelectedText = "本文テスト",
            Status = TradeStatus.Proposed
        };

        _itemSplitServiceMock.Setup(s => s.GetPendingSplitRequestsForOriginalItemAsync(31, default))
            .ReturnsAsync([pendingRequest]);

        _splitCoordinatorMock.Setup(c => c.RejectSplitRequestAsync(item, pendingRequest, CurrentUserId))
            .ReturnsAsync(true);

        IRenderedComponent<ItemCard> cut = _ctx.Render<ItemCard>(parameters => parameters
            .Add(p => p.Item, item)
            .Add(p => p.CurrentUserId, CurrentUserId));

        // Act: 却下ボタンをクリック
        var rejectBtn = cut.Find($"[data-testid='reject-split-request-{pendingRequest.Id}']");
        await cut.InvokeAsync(() => rejectBtn.Click());

        // Assert: Coordinator の RejectSplitRequestAsync が呼ばれること
        _splitCoordinatorMock.Verify(c => c.RejectSplitRequestAsync(item, pendingRequest, CurrentUserId), Times.Once);
        _itemSplitServiceMock.Verify(s => s.GetPendingSplitRequestsForOriginalItemAsync(31, default), Times.AtLeast(2));
    }

    [Fact]
    public async Task ItemCard_WhenCancelSplitRequestClicked_CallsCoordinatorCancelSplitRequestAsync()
    {
        // Arrange
        var item = new DataItem
        {
            Id = 32,
            Content = "他ユーザーのアイテム本文",
            OwnerId = OtherUserId,
            TagRelations = []
        };

        var requester = new ApplicationUser { Id = CurrentUserId, UserName = "Bob" };
        var pendingRequest = new ItemSplitRequest
        {
            Id = 303,
            OriginalItemId = 32,
            RequesterUserId = CurrentUserId,
            RequesterUser = requester,
            OwnerUserId = OtherUserId,
            OwnerId = CurrentUserId,
            SelectedText = "アイテム本文",
            Status = TradeStatus.Proposed
        };

        _itemSplitServiceMock.Setup(s => s.GetPendingSplitRequestsForOriginalItemAsync(32, default))
            .ReturnsAsync([pendingRequest]);

        _splitCoordinatorMock.Setup(c => c.CancelSplitRequestAsync(pendingRequest, CurrentUserId))
            .ReturnsAsync(true);

        IRenderedComponent<ItemCard> cut = _ctx.Render<ItemCard>(parameters => parameters
            .Add(p => p.Item, item)
            .Add(p => p.CurrentUserId, CurrentUserId));

        // Act: 取り下げボタンをクリック
        var cancelBtn = cut.Find($"[data-testid='cancel-split-request-{pendingRequest.Id}']");
        await cut.InvokeAsync(() => cancelBtn.Click());

        // Assert: Coordinator の CancelSplitRequestAsync が呼ばれること
        _splitCoordinatorMock.Verify(c => c.CancelSplitRequestAsync(pendingRequest, CurrentUserId), Times.Once);
        _itemSplitServiceMock.Verify(s => s.GetPendingSplitRequestsForOriginalItemAsync(32, default), Times.AtLeast(2));
    }

    [Fact]
    public async Task ItemCard_WhenReactionClicked_AndUnauthenticated_ShowsLoginRequiredWarning()
    {
        // Arrange: 未ログイン状態（CurrentUserId が空）
        var item = new DataItem
        {
            Id = 33,
            Content = "他ユーザーのアイテム本文",
            OwnerId = OtherUserId,
            TagRelations = []
        };

        _itemSplitServiceMock.Setup(s => s.GetPendingSplitRequestsForOriginalItemAsync(33, default))
            .ReturnsAsync([]);

        IRenderedComponent<ItemCard> cut = _ctx.Render<ItemCard>(parameters => parameters
            .Add(p => p.Item, item)
            .Add(p => p.CurrentUserId, ""));

        // Act: リアクションボタンをクリック
        var upvoteBtn = cut.Find("[data-testid='reaction-shinji-upvote']");
        await cut.InvokeAsync(() => upvoteBtn.Click());

        // Assert: 警告スナックバーが表示され、コーディネーターは呼ばれないこと
        ISnackbar snackbar = _ctx.Services.GetRequiredService<ISnackbar>();
        Assert.Contains(snackbar.ShownSnackbars, s => s.Message.ToString().Contains("ログインが必要です"));
        _splitCoordinatorMock.Verify(c => c.GetValidSelectedTextForSplitAsync(It.IsAny<DataItem>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task ItemCard_WhenQuoteClicked_AndUnauthenticated_ShowsLoginRequiredWarning()
    {
        // Arrange: 未ログイン状態（CurrentUserId が空）
        var item = new DataItem
        {
            Id = 34,
            Content = "他ユーザーのアイテム本文",
            OwnerId = OtherUserId,
            TagRelations = []
        };

        _itemSplitServiceMock.Setup(s => s.GetPendingSplitRequestsForOriginalItemAsync(34, default))
            .ReturnsAsync([]);

        IRenderedComponent<ItemCard> cut = _ctx.Render<ItemCard>(parameters => parameters
            .Add(p => p.Item, item)
            .Add(p => p.CurrentUserId, ""));

        // Act: 引用ボタンをクリック
        var quoteBtn = cut.Find($"[data-testid='quote-button-{item.Id}']");
        await cut.InvokeAsync(() => quoteBtn.Click());

        // Assert: 警告スナックバーが表示され、ダイアログランチャーは呼ばれないこと
        ISnackbar snackbar = _ctx.Services.GetRequiredService<ISnackbar>();
        Assert.Contains(snackbar.ShownSnackbars, s => s.Message.ToString().Contains("ログインが必要です"));
        _dialogLauncherMock.Verify(l => l.ShowAsync(
            It.IsAny<Type>(), It.IsAny<string>(), It.IsAny<DialogParameters>(), It.IsAny<DialogOptions>()), Times.Never);
    }

    [Fact]
    public async Task ItemCard_WhenReactionClicked_WithValidSelectedText_ShowsReactionCommentDialogAndReloadsRequests()
    {
        // Arrange
        var item = new DataItem
        {
            Id = 35,
            Content = "他ユーザーのアイテム本文の一部をリアクション",
            OwnerId = OtherUserId,
            TagRelations = []
        };

        _itemSplitServiceMock.Setup(s => s.GetPendingSplitRequestsForOriginalItemAsync(35, default))
            .ReturnsAsync([]);

        _splitCoordinatorMock.Setup(c => c.GetValidSelectedTextForSplitAsync(item, CurrentUserId))
            .ReturnsAsync("一部をリアクション");

        var quoteItem = new DataItem { Id = 99, Content = "一部をリアクション", OwnerId = CurrentUserId };
        var voteResult = new ItemVoteResult(ItemVoteAction.Added, 777, 1);
        var splitRequest = new ItemSplitRequest { Id = 401, SelectedText = "一部をリアクション", OwnerId = CurrentUserId };
        var splitWithReactionResult = new ItemSplitWithReactionResult(splitRequest, voteResult, quoteItem);

        _splitCoordinatorMock.Setup(c => c.RequestSplitWithReactionAsync(
                item, CurrentUserId, ReactionTagNames.Shinji, 1, null, It.IsAny<IReadOnlyList<DataTag>>(), It.IsAny<Func<Task>?>(), "一部をリアクション"))
            .ReturnsAsync(splitWithReactionResult);

        var dialogRef = new Mock<IDialogReference>();
        dialogRef.Setup(r => r.Result).ReturnsAsync(DialogResult.Cancel());
        _dialogLauncherMock
            .Setup(l => l.ShowAsync(typeof(ReactionCommentDialog), "リアクションを追加", It.IsAny<DialogParameters>(), It.IsAny<DialogOptions>()))
            .ReturnsAsync(dialogRef.Object);

        IRenderedComponent<ItemCard> cut = _ctx.Render<ItemCard>(parameters => parameters
            .Add(p => p.Item, item)
            .Add(p => p.CurrentUserId, CurrentUserId));

        // Act: リアクションボタンをクリック
        var upvoteBtn = cut.Find("[data-testid='reaction-shinji-upvote']");
        await cut.InvokeAsync(() => upvoteBtn.Click());

        // Assert: コメント入力ダイアログが表示され、保留中リクエストが再ロードされること
        _dialogLauncherMock.Verify(l => l.ShowAsync(
            typeof(ReactionCommentDialog), "リアクションを追加", It.IsAny<DialogParameters>(), It.IsAny<DialogOptions>()), Times.Once);
        _itemSplitServiceMock.Verify(s => s.GetPendingSplitRequestsForOriginalItemAsync(35, default), Times.AtLeast(2));
    }

    [Fact]
    public async Task ItemCard_WhenReactionClicked_WithValidSelectedText_AndCommentSaved_UpdatesReactionCommentAndNotifiesDataChanged()
    {
        // Arrange: 文字選択状態でリアクションをクリックし、ReactionCommentDialog でコメントを保存するケース
        var item = new DataItem
        {
            Id = 38,
            Content = "他ユーザーのアイテム本文の一部をリアクションしてコメントする",
            OwnerId = OtherUserId,
            TagRelations = []
        };

        _itemSplitServiceMock.Setup(s => s.GetPendingSplitRequestsForOriginalItemAsync(38, default))
            .ReturnsAsync([]);

        _splitCoordinatorMock.Setup(c => c.GetValidSelectedTextForSplitAsync(item, CurrentUserId))
            .ReturnsAsync("一部をリアクションしてコメントする");

        var quoteItem = new DataItem { Id = 109, Content = "一部をリアクションしてコメントする", OwnerId = CurrentUserId };
        var voteResult = new ItemVoteResult(ItemVoteAction.Added, 888, 1);
        var splitRequest = new ItemSplitRequest { Id = 402, SelectedText = "一部をリアクションしてコメントする", OwnerId = CurrentUserId };
        var splitWithReactionResult = new ItemSplitWithReactionResult(splitRequest, voteResult, quoteItem);

        _splitCoordinatorMock.Setup(c => c.RequestSplitWithReactionAsync(
                item, CurrentUserId, ReactionTagNames.Shinji, 1, null, It.IsAny<IReadOnlyList<DataTag>>(), It.IsAny<Func<Task>?>(), "一部をリアクションしてコメントする"))
            .ReturnsAsync(splitWithReactionResult);

        var dialogRef = new Mock<IDialogReference>();
        dialogRef.Setup(r => r.Result).ReturnsAsync(
            DialogResult.Ok(new ReactionCommentDialogResult(Saved: true, Comment: "この部分に深く共感しました")));
        _dialogLauncherMock
            .Setup(l => l.ShowAsync(typeof(ReactionCommentDialog), "リアクションを追加", It.IsAny<DialogParameters>(), It.IsAny<DialogOptions>()))
            .ReturnsAsync(dialogRef.Object);

        _voteCoordinatorMock
            .Setup(v => v.UpdateReactionCommentAsync(888, CurrentUserId, "この部分に深く共感しました"))
            .ReturnsAsync(true);

        int dataChangedCount = 0;
        IRenderedComponent<ItemCard> cut = _ctx.Render<ItemCard>(parameters => parameters
            .Add(p => p.Item, item)
            .Add(p => p.CurrentUserId, CurrentUserId)
            .Add(p => p.OnDataChanged, () => dataChangedCount++));

        // Act: リアクションボタンをクリック
        var upvoteBtn = cut.Find("[data-testid='reaction-shinji-upvote']");
        await cut.InvokeAsync(() => upvoteBtn.Click());

        // Assert: 引用アイテム上の TagRelation (RelationId=888) に対してリアクションコメントが保存されること
        _voteCoordinatorMock.Verify(
            v => v.UpdateReactionCommentAsync(888, CurrentUserId, "この部分に深く共感しました"),
            Times.Once);
        Assert.Equal(2, dataChangedCount);
    }

    [Fact]
    public async Task ItemCard_WhenAddTagClicked_WithValidSelectedText_CallsCoordinatorRequestSplitWithTagAddAsync()
    {
        // Arrange: 他ユーザーのアイテムで有効な選択テキストがある状態で「+ タグを追加」をクリック
        var item = new DataItem
        {
            Id = 39,
            Content = "他ユーザーのアイテム本文の一部を選択してタグを追加する",
            OwnerId = OtherUserId,
            TagRelations = []
        };

        _itemSplitServiceMock.Setup(s => s.GetPendingSplitRequestsForOriginalItemAsync(39, default))
            .ReturnsAsync([]);

        _splitCoordinatorMock.Setup(c => c.GetValidSelectedTextForSplitAsync(item, CurrentUserId))
            .ReturnsAsync("一部を選択してタグを追加する");

        _splitCoordinatorMock.Setup(c => c.RequestSplitWithTagAddAsync(item, CurrentUserId, "一部を選択してタグを追加する"))
            .ReturnsAsync(TagAddOutcome.AddedDirectly);

        int dataChangedCount = 0;
        IRenderedComponent<ItemCard> cut = _ctx.Render<ItemCard>(parameters => parameters
            .Add(p => p.Item, item)
            .Add(p => p.CurrentUserId, CurrentUserId)
            .Add(p => p.OnDataChanged, () => dataChangedCount++));

        // Act: タグ追加ボタンをクリック
        var addTagBtn = cut.Find("button[title='タグを追加']");
        await cut.InvokeAsync(() => addTagBtn.Click());

        // Assert: SplitCoordinator.RequestSplitWithTagAddAsync が呼ばれ、元アイテムへの直接タグ追加は呼ばれないこと
        _splitCoordinatorMock.Verify(
            c => c.RequestSplitWithTagAddAsync(item, CurrentUserId, "一部を選択してタグを追加する"),
            Times.Once);
        _tagCoordinatorMock.Verify(
            t => t.PromptAndAddTagAsync(It.IsAny<DataItem>(), It.IsAny<string>()),
            Times.Never);
        _itemSplitServiceMock.Verify(s => s.GetPendingSplitRequestsForOriginalItemAsync(39, default), Times.AtLeast(2));
        Assert.Equal(1, dataChangedCount);
    }

    [Fact]
    public async Task ItemCard_WhenAddTagClicked_WithoutValidSelectedText_CallsTagCoordinatorPromptAndAddTagAsync()
    {
        // Arrange: テキスト未選択時は従来どおり TagCoordinator.PromptAndAddTagAsync を呼ぶ
        var item = new DataItem
        {
            Id = 40,
            Content = "他ユーザーのアイテム本文",
            OwnerId = OtherUserId,
            TagRelations = []
        };

        _itemSplitServiceMock.Setup(s => s.GetPendingSplitRequestsForOriginalItemAsync(40, default))
            .ReturnsAsync([]);

        _splitCoordinatorMock.Setup(c => c.GetValidSelectedTextForSplitAsync(item, CurrentUserId))
            .ReturnsAsync((string?)null);

        _tagCoordinatorMock.Setup(t => t.PromptAndAddTagAsync(item, CurrentUserId))
            .ReturnsAsync(TagAddOutcome.AddedDirectly);

        IRenderedComponent<ItemCard> cut = _ctx.Render<ItemCard>(parameters => parameters
            .Add(p => p.Item, item)
            .Add(p => p.CurrentUserId, CurrentUserId));

        // Act
        var addTagBtn = cut.Find("button[title='タグを追加']");
        await cut.InvokeAsync(() => addTagBtn.Click());

        // Assert
        _tagCoordinatorMock.Verify(t => t.PromptAndAddTagAsync(item, CurrentUserId), Times.Once);
        _splitCoordinatorMock.Verify(
            c => c.RequestSplitWithTagAddAsync(It.IsAny<DataItem>(), It.IsAny<string>(), It.IsAny<string?>()),
            Times.Never);
    }

    [Fact]
    public void ItemCard_WhenNoPendingRequests_DoesNotRenderPendingRequestsSection()
    {
        // Arrange
        var item = new DataItem
        {
            Id = 36,
            Content = "リクエストのないアイテム本文",
            OwnerId = CurrentUserId,
            TagRelations = []
        };

        _itemSplitServiceMock.Setup(s => s.GetPendingSplitRequestsForOriginalItemAsync(36, default))
            .ReturnsAsync([]);

        // Act
        IRenderedComponent<ItemCard> cut = _ctx.Render<ItemCard>(parameters => parameters
            .Add(p => p.Item, item)
            .Add(p => p.CurrentUserId, CurrentUserId));

        // Assert: 承認ボタンも却下ボタンも取り下げボタンも描画されないこと
        Assert.DoesNotContain("approve-split-request", cut.Markup);
        Assert.DoesNotContain("reject-split-request", cut.Markup);
        Assert.DoesNotContain("cancel-split-request", cut.Markup);
    }

    [Fact]
    public void ItemCard_WhenSplitRequestHasRequesterQuoteItemId_RendersQuoteItemLink()
    {
        // Arrange
        var item = new DataItem
        {
            Id = 37,
            Content = "分割リクエストのあるアイテム本文",
            OwnerId = CurrentUserId,
            TagRelations = []
        };

        var splitReq = new ItemSplitRequest
        {
            Id = 99,
            OriginalItemId = 37,
            SelectedText = "分割対象テキスト",
            RequesterUserId = OtherUserId,
            RequesterUser = new ApplicationUser { UserName = "Bob", Id = OtherUserId },
            OwnerUserId = CurrentUserId,
            OwnerId = OtherUserId,
            RequesterQuoteItemId = 555,
            Status = TradeStatus.Proposed
        };

        _itemSplitServiceMock.Setup(s => s.GetPendingSplitRequestsForOriginalItemAsync(37, default))
            .ReturnsAsync([splitReq]);

        // Act
        IRenderedComponent<ItemCard> cut = _ctx.Render<ItemCard>(parameters => parameters
            .Add(p => p.Item, item)
            .Add(p => p.CurrentUserId, CurrentUserId));

        // Assert
        Assert.Contains("quote-item-link-99", cut.Markup);
        Assert.Contains("/ItemDetail/555", cut.Markup);
        Assert.Contains("引用投稿: #555", cut.Markup);
    }

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }
}