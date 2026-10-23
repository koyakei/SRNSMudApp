using Microsoft.JSInterop;

using Moq;

using MudBlazor;

using SRNSMudApp.Components.Contract;
using SRNSMudApp.Components.Tag;
using SRNSMudApp.Components.UI;
using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Resources;
using SRNSMudApp.Services;
using SRNSMudApp.Services.Dialogs;

#pragma warning disable BL0016

namespace SRNSMudApp.Tests.Services;

/// <summary>
///     ItemCardSplitCoordinator の単体テスト。
///     テキスト直接分割、リクエスト送信、承認・却下・取消の各フローを検証する。
/// </summary>
public class ItemCardSplitCoordinatorTests
{
    private const string OwnerId = "owner-user";
    private const string RequesterId = "requester-user";

    private readonly Mock<IItemSplitService> _itemSplitServiceMock = new();
    private readonly Mock<IItemQuoteService> _itemQuoteServiceMock = new();
    private readonly Mock<IItemCardVoteCoordinator> _voteCoordinatorMock = new();
    private readonly Mock<IItemCardDataProvider> _itemCardDataProviderMock = new();
    private readonly Mock<IDialogLauncher> _dialogLauncherMock = new();
    private readonly Mock<ISnackbar> _snackbarMock = new();
    private readonly Mock<IJSRuntime> _jsMock = new();
    private readonly ItemCardSplitCoordinator _coordinator;

    public ItemCardSplitCoordinatorTests()
    {
        _coordinator = new ItemCardSplitCoordinator(
            _itemSplitServiceMock.Object,
            _itemQuoteServiceMock.Object,
            _voteCoordinatorMock.Object,
            _itemCardDataProviderMock.Object,
            _dialogLauncherMock.Object,
            _snackbarMock.Object,
            _jsMock.Object);
    }

    [Fact]
    public void Constructor_NullParameters_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new ItemCardSplitCoordinator(null!, _itemQuoteServiceMock.Object, _voteCoordinatorMock.Object, _itemCardDataProviderMock.Object, _dialogLauncherMock.Object, _snackbarMock.Object, _jsMock.Object));
        Assert.Throws<ArgumentNullException>(() => new ItemCardSplitCoordinator(_itemSplitServiceMock.Object, null!, _voteCoordinatorMock.Object, _itemCardDataProviderMock.Object, _dialogLauncherMock.Object, _snackbarMock.Object, _jsMock.Object));
        Assert.Throws<ArgumentNullException>(() => new ItemCardSplitCoordinator(_itemSplitServiceMock.Object, _itemQuoteServiceMock.Object, null!, _itemCardDataProviderMock.Object, _dialogLauncherMock.Object, _snackbarMock.Object, _jsMock.Object));
        Assert.Throws<ArgumentNullException>(() => new ItemCardSplitCoordinator(_itemSplitServiceMock.Object, _itemQuoteServiceMock.Object, _voteCoordinatorMock.Object, null!, _dialogLauncherMock.Object, _snackbarMock.Object, _jsMock.Object));
        Assert.Throws<ArgumentNullException>(() => new ItemCardSplitCoordinator(_itemSplitServiceMock.Object, _itemQuoteServiceMock.Object, _voteCoordinatorMock.Object, _itemCardDataProviderMock.Object, null!, _snackbarMock.Object, _jsMock.Object));
        Assert.Throws<ArgumentNullException>(() => new ItemCardSplitCoordinator(_itemSplitServiceMock.Object, _itemQuoteServiceMock.Object, _voteCoordinatorMock.Object, _itemCardDataProviderMock.Object, _dialogLauncherMock.Object, null!, _jsMock.Object));
        Assert.Throws<ArgumentNullException>(() => new ItemCardSplitCoordinator(_itemSplitServiceMock.Object, _itemQuoteServiceMock.Object, _voteCoordinatorMock.Object, _itemCardDataProviderMock.Object, _dialogLauncherMock.Object, _snackbarMock.Object, null!));
    }

    [Fact]
    public async Task SplitSelectionAsync_WhenNotOwner_ShowsNotAuthorizedError()
    {
        var item = new Item { Id = 1, OwnerId = OwnerId, Content = "Hello World" };

        var result = await _coordinator.SplitSelectionAsync(item, "other-user");

        Assert.False(result);
        _snackbarMock.Verify(s => s.Add(ErrorMessages.NotAuthorizedToEdit, Severity.Error, It.IsAny<Action<SnackbarOptions>>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task SplitSelectionAsync_WhenTextEmpty_ShowsWarningSnackbar()
    {
        var item = new Item { Id = 1, OwnerId = OwnerId, Content = "Hello World" };

        _ = _jsMock.Setup(j => j.InvokeAsync<string>("selectionHelper.getSelectedText", It.IsAny<object[]>()))
            .Returns(ValueTask.FromResult(""));

        var result = await _coordinator.SplitSelectionAsync(item, OwnerId);

        Assert.False(result);
        _snackbarMock.Verify(s => s.Add("分割するテキストが選択されていません。", Severity.Warning, It.IsAny<Action<SnackbarOptions>>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task SplitSelectionAsync_WhenTextNotInContent_ShowsErrorSnackbar()
    {
        var item = new Item { Id = 1, OwnerId = OwnerId, Content = "Hello World" };

        _ = _jsMock.Setup(j => j.InvokeAsync<string>("selectionHelper.getSelectedText", It.IsAny<object[]>()))
            .Returns(ValueTask.FromResult("Unmatched Text"));

        var result = await _coordinator.SplitSelectionAsync(item, OwnerId);

        Assert.False(result);
        _snackbarMock.Verify(s => s.Add("選択したテキストがこのアイテムの本文に含まれていません。", Severity.Error, It.IsAny<Action<SnackbarOptions>>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task SplitSelectionAsync_OnSuccess_ReplacesTextWithLinkAndShowsSuccess()
    {
        var item = new Item { Id = 1, OwnerId = OwnerId, Content = "Hello World today" };
        var createdItem = new Item { Id = 50, OwnerId = OwnerId, Content = "World" };

        _ = _jsMock.Setup(j => j.InvokeAsync<string>("selectionHelper.getSelectedText", It.IsAny<object[]>()))
            .Returns(ValueTask.FromResult("World"));

        _ = _itemSplitServiceMock
            .Setup(s => s.SplitDirectlyAsync(1, "World", OwnerId, default))
            .ReturnsAsync(new Success<Item>(createdItem));

        var result = await _coordinator.SplitSelectionAsync(item, OwnerId);

        Assert.True(result);
        Assert.Equal("Hello /ItemDetail/50 today", item.Content);
        _snackbarMock.Verify(s => s.Add("アイテムを分割しました。", Severity.Success, It.IsAny<Action<SnackbarOptions>>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task ApproveSplitRequestAsync_WhenNotOwner_ShowsError()
    {
        var item = new Item { Id = 1, OwnerId = OwnerId, Content = "Hello World" };
        var request = new ItemSplitRequest { Id = 10, OwnerId = OwnerId, SelectedText = "World" };

        var result = await _coordinator.ApproveSplitRequestAsync(item, request, "other-user");

        Assert.False(result);
        _snackbarMock.Verify(s => s.Add(ErrorMessages.NotAuthorizedToEdit, Severity.Error, It.IsAny<Action<SnackbarOptions>>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task ApproveSplitRequestAsync_OnSuccess_ReplacesTextWithLink()
    {
        var item = new Item { Id = 1, OwnerId = OwnerId, Content = "Hello World" };
        var request = new ItemSplitRequest { Id = 10, OwnerId = OwnerId, SelectedText = "World" };
        var createdItem = new Item { Id = 88, OwnerId = OwnerId, Content = "World" };

        _ = _itemSplitServiceMock
            .Setup(s => s.ApproveSplitAsync(10, OwnerId, default))
            .ReturnsAsync(new Success<Item>(createdItem));

        var result = await _coordinator.ApproveSplitRequestAsync(item, request, OwnerId);

        Assert.True(result);
        Assert.Equal("Hello /ItemDetail/88", item.Content);
        _snackbarMock.Verify(s => s.Add("分割リクエストを承認しました。", Severity.Success, It.IsAny<Action<SnackbarOptions>>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task CancelSplitRequestAsync_WhenRequesterMismatch_ShowsError()
    {
        var request = new ItemSplitRequest { Id = 10, OwnerId = OwnerId, RequesterUserId = RequesterId };

        var result = await _coordinator.CancelSplitRequestAsync(request, "different-user");

        Assert.False(result);
        _snackbarMock.Verify(s => s.Add("リクエストを取り下げる権限がありません。", Severity.Error, It.IsAny<Action<SnackbarOptions>>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task CancelSplitRequestAsync_OnSuccess_ReturnsTrue()
    {
        var request = new ItemSplitRequest { Id = 10, OwnerId = OwnerId, RequesterUserId = RequesterId };

        _ = _itemSplitServiceMock
            .Setup(s => s.CancelSplitAsync(10, RequesterId, default))
            .ReturnsAsync(new Success<bool>(true));

        var result = await _coordinator.CancelSplitRequestAsync(request, RequesterId);

        Assert.True(result);
        _snackbarMock.Verify(s => s.Add("分割リクエストを取り下げました。", Severity.Success, It.IsAny<Action<SnackbarOptions>>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task SplitSelectionAsync_WhenJSThrows_ReturnsFalse()
    {
        var item = new Item { Id = 1, OwnerId = OwnerId, Content = "Hello World" };

        _ = _jsMock.Setup(j => j.InvokeAsync<string>("selectionHelper.getSelectedText", It.IsAny<object[]>()))
            .ThrowsAsync(new JSException("JS Error"));

        var result = await _coordinator.SplitSelectionAsync(item, OwnerId);

        Assert.False(result);
    }

    [Fact]
    public async Task SplitSelectionAsync_WhenServiceReturnsFailure_ShowsErrorSnackbar()
    {
        var item = new Item { Id = 1, OwnerId = OwnerId, Content = "Hello World" };

        _ = _jsMock.Setup(j => j.InvokeAsync<string>("selectionHelper.getSelectedText", It.IsAny<object[]>()))
            .Returns(ValueTask.FromResult("World"));

        _ = _itemSplitServiceMock
            .Setup(s => s.SplitDirectlyAsync(1, "World", OwnerId, default))
            .ReturnsAsync(new Failure("Split Failed"));

        var result = await _coordinator.SplitSelectionAsync(item, OwnerId);

        Assert.False(result);
        _snackbarMock.Verify(s => s.Add("Split Failed", Severity.Error, It.IsAny<Action<SnackbarOptions>>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task RequestSplitSelectionAsync_WhenUserIdIsNullOrWhiteSpace_ShowsWarning()
    {
        var item = new Item { Id = 1, OwnerId = OwnerId, Content = "Hello World" };

        var result = await _coordinator.RequestSplitSelectionAsync(item, "");

        Assert.Null(result);
        _snackbarMock.Verify(s => s.Add("リクエストを送信するにはログインが必要です。", Severity.Warning, It.IsAny<Action<SnackbarOptions>>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task RequestSplitSelectionAsync_OnSuccess_ReturnsRequest()
    {
        var item = new Item { Id = 1, OwnerId = OwnerId, Content = "Hello World" };
        var createdRequest = new ItemSplitRequest { Id = 12, OwnerId = OwnerId, RequesterUserId = RequesterId, SelectedText = "World" };

        _ = _jsMock.Setup(j => j.InvokeAsync<string>("selectionHelper.getSelectedText", It.IsAny<object[]>()))
            .Returns(ValueTask.FromResult("World"));

        var dialogRef = new Mock<IDialogReference>();
        dialogRef.Setup(r => r.Result).ReturnsAsync(DialogResult.Ok(new SplitRequestWithQuoteDialogResult(null)));
        _ = _dialogLauncherMock
            .Setup(l => l.ShowAsync(typeof(SplitRequestWithQuoteDialog), "アイテム分割リクエストの送信", It.IsAny<DialogParameters>(), It.IsAny<DialogOptions>()))
            .ReturnsAsync(dialogRef.Object);

        _ = _itemSplitServiceMock
            .Setup(s => s.RequestSplitAsync(1, "World", RequesterId, null, default))
            .ReturnsAsync(new Success<ItemSplitRequest>(createdRequest));

        var result = await _coordinator.RequestSplitSelectionAsync(item, RequesterId);

        Assert.NotNull(result);
        Assert.Equal(12, result.Id);
        _snackbarMock.Verify(s => s.Add("アイテム分割リクエストを送信しました。", Severity.Success, It.IsAny<Action<SnackbarOptions>>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task RejectSplitRequestAsync_WhenNotOwner_ShowsError()
    {
        var item = new Item { Id = 1, OwnerId = OwnerId, Content = "Hello World" };
        var request = new ItemSplitRequest { Id = 10, OwnerId = OwnerId, SelectedText = "World" };

        var result = await _coordinator.RejectSplitRequestAsync(item, request, "different-user");

        Assert.False(result);
        _snackbarMock.Verify(s => s.Add(ErrorMessages.NotAuthorizedToEdit, Severity.Error, It.IsAny<Action<SnackbarOptions>>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task RejectSplitRequestAsync_OnSuccess_ReturnsTrue()
    {
        var item = new Item { Id = 1, OwnerId = OwnerId, Content = "Hello World" };
        var request = new ItemSplitRequest { Id = 10, OwnerId = OwnerId, SelectedText = "World" };

        var dialogRef = new Mock<IDialogReference>();
        dialogRef.Setup(r => r.Result).ReturnsAsync(DialogResult.Ok<string?>("Rejection reason"));
        _ = _dialogLauncherMock
            .Setup(l => l.ShowAsync(typeof(RejectRequestDialog), "分割リクエストを却下", It.IsAny<DialogParameters>(), It.IsAny<DialogOptions>()))
            .ReturnsAsync(dialogRef.Object);

        _ = _itemSplitServiceMock
            .Setup(s => s.RejectSplitAsync(10, OwnerId, "Rejection reason", default))
            .ReturnsAsync(new Success<bool>(true));

        var result = await _coordinator.RejectSplitRequestAsync(item, request, OwnerId);

        Assert.True(result);
        _snackbarMock.Verify(s => s.Add("分割リクエストを却下しました。", Severity.Success, It.IsAny<Action<SnackbarOptions>>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task Methods_ThrowArgumentNullException_WhenRequiredParametersAreNull()
    {
        var item = new Item { Id = 1, OwnerId = OwnerId };
        var request = new ItemSplitRequest { Id = 10, OwnerId = OwnerId };

        await Assert.ThrowsAsync<ArgumentNullException>(() => _coordinator.SplitSelectionAsync(null!, OwnerId));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _coordinator.RequestSplitSelectionAsync(null!, RequesterId));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _coordinator.ApproveSplitRequestAsync(null!, request, OwnerId));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _coordinator.ApproveSplitRequestAsync(item, null!, OwnerId));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _coordinator.RejectSplitRequestAsync(null!, request, OwnerId));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _coordinator.RejectSplitRequestAsync(item, null!, OwnerId));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _coordinator.CancelSplitRequestAsync(null!, RequesterId));
    }

    [Fact]
    public async Task RequestSplitSelectionAsync_WithCrlfInItemAndLfInSelection_Succeeds()
    {
        var item = new Item { Id = 1, OwnerId = OwnerId, Content = "Hello\r\nWorld\r\nTest" };
        var createdRequest = new ItemSplitRequest { Id = 15, OwnerId = OwnerId, RequesterUserId = RequesterId, SelectedText = "World\nTest" };

        _ = _jsMock.Setup(j => j.InvokeAsync<string>("selectionHelper.getSelectedText", It.IsAny<object[]>()))
            .Returns(ValueTask.FromResult("World\nTest"));

        var dialogRef = new Mock<IDialogReference>();
        dialogRef.Setup(r => r.Result).ReturnsAsync(DialogResult.Ok(new SplitRequestWithQuoteDialogResult(null)));
        _ = _dialogLauncherMock
            .Setup(l => l.ShowAsync(typeof(SplitRequestWithQuoteDialog), "アイテム分割リクエストの送信", It.IsAny<DialogParameters>(), It.IsAny<DialogOptions>()))
            .ReturnsAsync(dialogRef.Object);

        _ = _itemSplitServiceMock
            .Setup(s => s.RequestSplitAsync(1, "World\nTest", RequesterId, null, default))
            .ReturnsAsync(new Success<ItemSplitRequest>(createdRequest));

        var result = await _coordinator.RequestSplitSelectionAsync(item, RequesterId);

        Assert.NotNull(result);
        Assert.Equal(15, result.Id);
        _snackbarMock.Verify(s => s.Add("アイテム分割リクエストを送信しました。", Severity.Success, It.IsAny<Action<SnackbarOptions>>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task ApproveSplitRequestAsync_WithCrlfInItemAndLfInSelectedText_ReplacesContentCorrectly()
    {
        var item = new Item { Id = 1, OwnerId = OwnerId, Content = "Hello\r\nWorld\r\nEnd" };
        var request = new ItemSplitRequest { Id = 10, OwnerId = OwnerId, SelectedText = "World\nEnd" };
        var newItem = new Item { Id = 99, Content = "World\nEnd", OwnerId = OwnerId };

        _ = _itemSplitServiceMock
            .Setup(s => s.ApproveSplitAsync(10, OwnerId, default))
            .ReturnsAsync(new Success<Item>(newItem));

        var result = await _coordinator.ApproveSplitRequestAsync(item, request, OwnerId);

        Assert.True(result);
        Assert.Equal("Hello\r\n/ItemDetail/99", item.Content);
        _snackbarMock.Verify(s => s.Add("分割リクエストを承認しました。", Severity.Success, It.IsAny<Action<SnackbarOptions>>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task RequestSplitSelectionAsync_WithCreatedQuoteItem_PassesQuoteItemIdToService()
    {
        var item = new Item { Id = 1, OwnerId = OwnerId, Content = "Hello World" };
        var createdQuote = new Item { Id = 77, OwnerId = RequesterId, Content = "Quote comment" };
        var createdRequest = new ItemSplitRequest { Id = 18, OwnerId = OwnerId, RequesterUserId = RequesterId, SelectedText = "World", RequesterQuoteItemId = 77 };

        _ = _jsMock.Setup(j => j.InvokeAsync<string>("selectionHelper.getSelectedText", It.IsAny<object[]>()))
            .Returns(ValueTask.FromResult("World"));

        var dialogRef = new Mock<IDialogReference>();
        dialogRef.Setup(r => r.Result).ReturnsAsync(DialogResult.Ok(new SplitRequestWithQuoteDialogResult(77, createdQuote)));
        _ = _dialogLauncherMock
            .Setup(l => l.ShowAsync(typeof(SplitRequestWithQuoteDialog), "アイテム分割リクエストの送信", It.IsAny<DialogParameters>(), It.IsAny<DialogOptions>()))
            .ReturnsAsync(dialogRef.Object);

        _ = _itemSplitServiceMock
            .Setup(s => s.RequestSplitAsync(1, "World", RequesterId, 77, default))
            .ReturnsAsync(new Success<ItemSplitRequest>(createdRequest));

        var result = await _coordinator.RequestSplitSelectionAsync(item, RequesterId);

        Assert.NotNull(result);
        Assert.Equal(18, result.Id);
        Assert.Equal(77, result.RequesterQuoteItemId);
        _itemSplitServiceMock.Verify(s => s.RequestSplitAsync(1, "World", RequesterId, 77, default), Times.Once);
    }

    [Fact]
    public async Task RequestSplitSelectionAsync_WhenDialogCancelled_ReturnsNull()
    {
        var item = new Item { Id = 1, OwnerId = OwnerId, Content = "Hello World" };

        _ = _jsMock.Setup(j => j.InvokeAsync<string>("selectionHelper.getSelectedText", It.IsAny<object[]>()))
            .Returns(ValueTask.FromResult("World"));

        var dialogRef = new Mock<IDialogReference>();
        dialogRef.Setup(r => r.Result).ReturnsAsync(DialogResult.Cancel());
        _ = _dialogLauncherMock
            .Setup(l => l.ShowAsync(typeof(SplitRequestWithQuoteDialog), "アイテム分割リクエストの送信", It.IsAny<DialogParameters>(), It.IsAny<DialogOptions>()))
            .ReturnsAsync(dialogRef.Object);

        var result = await _coordinator.RequestSplitSelectionAsync(item, RequesterId);

        Assert.Null(result);
        _itemSplitServiceMock.Verify(s => s.RequestSplitAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RequestSplitSelectionAsync_WhenDialogReturnsNullQuoteId_SubmitsWithNullQuoteId()
    {
        var item = new Item { Id = 1, OwnerId = OwnerId, Content = "Hello World" };
        var createdRequest = new ItemSplitRequest { Id = 20, OwnerId = OwnerId, RequesterUserId = RequesterId, SelectedText = "World", RequesterQuoteItemId = null };

        _ = _jsMock.Setup(j => j.InvokeAsync<string>("selectionHelper.getSelectedText", It.IsAny<object[]>()))
            .Returns(ValueTask.FromResult("World"));

        var dialogRef = new Mock<IDialogReference>();
        dialogRef.Setup(r => r.Result).ReturnsAsync(DialogResult.Ok(new SplitRequestWithQuoteDialogResult(null)));
        _ = _dialogLauncherMock
            .Setup(l => l.ShowAsync(typeof(SplitRequestWithQuoteDialog), "アイテム分割リクエストの送信", It.IsAny<DialogParameters>(), It.IsAny<DialogOptions>()))
            .ReturnsAsync(dialogRef.Object);

        _ = _itemSplitServiceMock
            .Setup(s => s.RequestSplitAsync(1, "World", RequesterId, null, default))
            .ReturnsAsync(new Success<ItemSplitRequest>(createdRequest));

        var result = await _coordinator.RequestSplitSelectionAsync(item, RequesterId);

        Assert.NotNull(result);
        Assert.Equal(20, result.Id);
        Assert.Null(result.RequesterQuoteItemId);
        _itemSplitServiceMock.Verify(s => s.RequestSplitAsync(1, "World", RequesterId, null, default), Times.Once);
    }

    [Fact]
    public async Task RequestSplitSelectionAsync_WhenServiceReturnsFailure_ShowsErrorSnackbar()
    {
        var item = new Item { Id = 1, OwnerId = OwnerId, Content = "Hello World" };

        _ = _jsMock.Setup(j => j.InvokeAsync<string>("selectionHelper.getSelectedText", It.IsAny<object[]>()))
            .Returns(ValueTask.FromResult("World"));

        var dialogRef = new Mock<IDialogReference>();
        dialogRef.Setup(r => r.Result).ReturnsAsync(DialogResult.Ok(new SplitRequestWithQuoteDialogResult(null)));
        _ = _dialogLauncherMock
            .Setup(l => l.ShowAsync(typeof(SplitRequestWithQuoteDialog), "アイテム分割リクエストの送信", It.IsAny<DialogParameters>(), It.IsAny<DialogOptions>()))
            .ReturnsAsync(dialogRef.Object);

        _ = _itemSplitServiceMock
            .Setup(s => s.RequestSplitAsync(1, "World", RequesterId, null, default))
            .ReturnsAsync(new Failure("このテキストに対する分割リクエストは既に申請中です。"));

        var result = await _coordinator.RequestSplitSelectionAsync(item, RequesterId);

        Assert.Null(result);
        _snackbarMock.Verify(s => s.Add("このテキストに対する分割リクエストは既に申請中です。", Severity.Error, It.IsAny<Action<SnackbarOptions>>(), It.IsAny<string?>()), Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RequestSplitWithReactionAsync_WhenUserIdIsNullOrWhiteSpace_ReturnsNullAndShowsWarning(string? userId)
    {
        var item = new Item { Id = 1, OwnerId = OwnerId, Content = "Hello World" };

        var result = await _coordinator.RequestSplitWithReactionAsync(
            item, userId!, ReactionTagNames.Shinji, 1, null, [], null, "World");

        Assert.Null(result);
        _snackbarMock.Verify(s => s.Add("リクエストを送信するにはログインが必要です。", Severity.Warning, It.IsAny<Action<SnackbarOptions>>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task RequestSplitWithReactionAsync_WhenTextNotSelected_ReturnsNullAndShowsWarning()
    {
        var item = new Item { Id = 1, OwnerId = OwnerId, Content = "Hello World" };

        _ = _jsMock.Setup(j => j.InvokeAsync<string>("selectionHelper.getSelectedText", It.IsAny<object[]>()))
            .Returns(ValueTask.FromResult(""));

        var result = await _coordinator.RequestSplitWithReactionAsync(
            item, RequesterId, ReactionTagNames.Shinji, 1, null, [], null, null);

        Assert.Null(result);
        _snackbarMock.Verify(s => s.Add("分割するテキストが選択されていません。", Severity.Warning, It.IsAny<Action<SnackbarOptions>>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task RequestSplitWithReactionAsync_WhenSelectedTextNotInContent_ReturnsNullAndShowsError()
    {
        var item = new Item { Id = 1, OwnerId = OwnerId, Content = "Hello World" };

        var result = await _coordinator.RequestSplitWithReactionAsync(
            item, RequesterId, ReactionTagNames.Shinji, 1, null, [], null, "NotPresent");

        Assert.Null(result);
        _snackbarMock.Verify(s => s.Add("選択したテキストがこのアイテムの本文に含まれていません。", Severity.Error, It.IsAny<Action<SnackbarOptions>>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task RequestSplitWithReactionAsync_WhenQuoteCreationFails_ReturnsNullAndShowsError()
    {
        var item = new Item { Id = 1, OwnerId = OwnerId, Content = "Hello World" };

        _ = _itemQuoteServiceMock
            .Setup(q => q.CreateQuoteItemAsync(1, "World", RequesterId, null, default))
            .ReturnsAsync((Item?)null);

        var result = await _coordinator.RequestSplitWithReactionAsync(
            item, RequesterId, ReactionTagNames.Shinji, 1, null, [], null, "World");

        Assert.Null(result);
        _snackbarMock.Verify(s => s.Add("引用アイテムの作成に失敗しました。", Severity.Error, It.IsAny<Action<SnackbarOptions>>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task RequestSplitWithReactionAsync_WhenReactionVoteFails_DeletesQuoteItemAndReturnsNull()
    {
        var item = new Item { Id = 1, OwnerId = OwnerId, Content = "Hello World" };
        var quoteItem = new Item { Id = 10, OwnerId = RequesterId, Content = "World", QuotedItemId = 1 };

        _ = _itemQuoteServiceMock
            .Setup(q => q.CreateQuoteItemAsync(1, "World", RequesterId, null, default))
            .ReturnsAsync(quoteItem);

        _ = _voteCoordinatorMock
            .Setup(v => v.ToggleReactionAsync(10, RequesterId, ReactionTagNames.Shinji, 1, null, It.IsAny<IReadOnlyList<Tag>>(), null))
            .ReturnsAsync((ItemVoteResult?)null);

        var result = await _coordinator.RequestSplitWithReactionAsync(
            item, RequesterId, ReactionTagNames.Shinji, 1, null, [], null, "World");

        Assert.Null(result);
        _itemCardDataProviderMock.Verify(d => d.DeleteItemAsync(10), Times.Once);
        _snackbarMock.Verify(s => s.Add("リアクションの投票に失敗しました。", Severity.Error, It.IsAny<Action<SnackbarOptions>>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task RequestSplitWithReactionAsync_WhenSplitRequestFails_DeletesQuoteItemAndReturnsNull()
    {
        var item = new Item { Id = 1, OwnerId = OwnerId, Content = "Hello World" };
        var quoteItem = new Item { Id = 10, OwnerId = RequesterId, Content = "World", QuotedItemId = 1 };
        var voteResult = new ItemVoteResult(ItemVoteAction.Added, 100, 1);

        _ = _itemQuoteServiceMock
            .Setup(q => q.CreateQuoteItemAsync(1, "World", RequesterId, null, default))
            .ReturnsAsync(quoteItem);

        _ = _voteCoordinatorMock
            .Setup(v => v.ToggleReactionAsync(10, RequesterId, ReactionTagNames.Shinji, 1, null, It.IsAny<IReadOnlyList<Tag>>(), null))
            .ReturnsAsync(voteResult);

        _ = _itemSplitServiceMock
            .Setup(s => s.RequestSplitAsync(1, "World", RequesterId, 10, default))
            .ReturnsAsync(new Failure("既に分割リクエストが存在します。"));

        var result = await _coordinator.RequestSplitWithReactionAsync(
            item, RequesterId, ReactionTagNames.Shinji, 1, null, [], null, "World");

        Assert.Null(result);
        _itemCardDataProviderMock.Verify(d => d.DeleteItemAsync(10), Times.Once);
        _snackbarMock.Verify(s => s.Add("既に分割リクエストが存在します。", Severity.Error, It.IsAny<Action<SnackbarOptions>>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task RequestSplitWithReactionAsync_WhenSuccessful_CreatesQuote_VotesReaction_CreatesSplitRequest_ClearsSelection_AndReturnsResult()
    {
        var item = new Item { Id = 1, OwnerId = OwnerId, Content = "Hello World" };
        var quoteItem = new Item { Id = 10, OwnerId = RequesterId, Content = "World", QuotedItemId = 1 };
        var voteResult = new ItemVoteResult(ItemVoteAction.Added, 100, 1);
        var splitRequest = new ItemSplitRequest
        {
            Id = 50,
            OwnerId = OwnerId,
            OriginalItemId = 1,
            SelectedText = "World",
            RequesterUserId = RequesterId,
            RequesterQuoteItemId = 10
        };

        _ = _itemQuoteServiceMock
            .Setup(q => q.CreateQuoteItemAsync(1, "World", RequesterId, null, default))
            .ReturnsAsync(quoteItem);

        _ = _voteCoordinatorMock
            .Setup(v => v.ToggleReactionAsync(10, RequesterId, ReactionTagNames.Shinji, 1, null, It.IsAny<IReadOnlyList<Tag>>(), null))
            .ReturnsAsync(voteResult);

        _ = _itemSplitServiceMock
            .Setup(s => s.RequestSplitAsync(1, "World", RequesterId, 10, default))
            .ReturnsAsync(new Success<ItemSplitRequest>(splitRequest));

        var result = await _coordinator.RequestSplitWithReactionAsync(
            item, RequesterId, ReactionTagNames.Shinji, 1, null, [], null, "World");

        Assert.NotNull(result);
        Assert.Same(splitRequest, result.SplitRequest);
        Assert.Same(voteResult, result.VoteResult);
        Assert.Same(quoteItem, result.QuoteItem);

        _itemCardDataProviderMock.Verify(d => d.DeleteItemAsync(It.IsAny<int>()), Times.Never);
        _snackbarMock.Verify(s => s.Add("引用してリアクションを投票し、アイテム分割リクエストを送信しました。", Severity.Success, It.IsAny<Action<SnackbarOptions>>(), It.IsAny<string?>()), Times.Once);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RequestSplitWithReactionAsync_WhenUserIdIsNullOrWhiteSpace_ShowsWarningAndReturnsNull(string invalidUserId)
    {
        var item = new Item { Id = 1, OwnerId = OwnerId, Content = "Hello World" };

        var result = await _coordinator.RequestSplitWithReactionAsync(
            item, invalidUserId, ReactionTagNames.Shinji, 1, null, [], null, "World");

        Assert.Null(result);
        _snackbarMock.Verify(s => s.Add("リクエストを送信するにはログインが必要です。", Severity.Warning, It.IsAny<Action<SnackbarOptions>>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task RequestSplitWithReactionAsync_WhenReactionVoteThrowsException_DeletesQuoteItem_ShowsError_AndReturnsNull()
    {
        var item = new Item { Id = 1, OwnerId = OwnerId, Content = "Hello World" };
        var quoteItem = new Item { Id = 10, OwnerId = RequesterId, Content = "World" };

        _ = _itemQuoteServiceMock
            .Setup(q => q.CreateQuoteItemAsync(1, "World", RequesterId, null, default))
            .ReturnsAsync(quoteItem);

        _ = _voteCoordinatorMock
            .Setup(v => v.ToggleReactionAsync(10, RequesterId, ReactionTagNames.Shinji, 1, null, It.IsAny<IReadOnlyList<Tag>>(), null))
            .ThrowsAsync(new InvalidOperationException("DB connection error"));

        var result = await _coordinator.RequestSplitWithReactionAsync(
            item, RequesterId, ReactionTagNames.Shinji, 1, null, [], null, "World");

        Assert.Null(result);
        _itemCardDataProviderMock.Verify(d => d.DeleteItemAsync(10), Times.Once);
        _snackbarMock.Verify(s => s.Add(It.Is<string>(msg => msg.Contains("リアクションの投票に失敗しました")), Severity.Error, It.IsAny<Action<SnackbarOptions>>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task RequestSplitWithReactionAsync_WhenSplitRequestThrowsException_DeletesQuoteItem_ShowsError_AndReturnsNull()
    {
        var item = new Item { Id = 1, OwnerId = OwnerId, Content = "Hello World" };
        var quoteItem = new Item { Id = 10, OwnerId = RequesterId, Content = "World" };
        var voteResult = new ItemVoteResult(ItemVoteAction.Added, 10, 100);

        _ = _itemQuoteServiceMock
            .Setup(q => q.CreateQuoteItemAsync(1, "World", RequesterId, null, default))
            .ReturnsAsync(quoteItem);

        _ = _voteCoordinatorMock
            .Setup(v => v.ToggleReactionAsync(10, RequesterId, ReactionTagNames.Shinji, 1, null, It.IsAny<IReadOnlyList<Tag>>(), null))
            .ReturnsAsync(voteResult);

        _ = _itemSplitServiceMock
            .Setup(s => s.RequestSplitAsync(1, "World", RequesterId, 10, default))
            .ThrowsAsync(new TimeoutException("Request timeout"));

        var result = await _coordinator.RequestSplitWithReactionAsync(
            item, RequesterId, ReactionTagNames.Shinji, 1, null, [], null, "World");

        Assert.Null(result);
        _itemCardDataProviderMock.Verify(d => d.DeleteItemAsync(10), Times.Once);
        _snackbarMock.Verify(s => s.Add(It.Is<string>(msg => msg.Contains("分割リクエストの送信に失敗しました")), Severity.Error, It.IsAny<Action<SnackbarOptions>>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task SplitSelectionAsync_WhenUnexpectedExceptionOccurs_ShowsErrorSnackbarAndReturnsFalse()
    {
        var item = new Item { Id = 1, OwnerId = OwnerId, Content = "Hello World" };

        _ = _jsMock.Setup(j => j.InvokeAsync<string>("selectionHelper.getSelectedText", It.IsAny<object[]>()))
            .Returns(ValueTask.FromResult("World"));

        _ = _itemSplitServiceMock
            .Setup(s => s.SplitDirectlyAsync(1, "World", OwnerId, default))
            .ThrowsAsync(new Exception("Unexpected DB error"));

        var result = await _coordinator.SplitSelectionAsync(item, OwnerId);

        Assert.False(result);
        _snackbarMock.Verify(s => s.Add(It.Is<string>(msg => msg.Contains("エラーが発生しました: Unexpected DB error")), Severity.Error, It.IsAny<Action<SnackbarOptions>>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task RequestSplitWithReactionAsync_WithDownvoteWeight_PassesNegativeWeightToVoteCoordinator()
    {
        var item = new Item { Id = 1, OwnerId = OwnerId, Content = "Hello World" };
        var quoteItem = new Item { Id = 20, OwnerId = RequesterId, Content = "World" };
        var voteResult = new ItemVoteResult(ItemVoteAction.Added, 20, 200);
        var splitRequest = new ItemSplitRequest
        {
            Id = 60,
            OwnerId = OwnerId,
            OriginalItemId = 1,
            SelectedText = "World",
            RequesterUserId = RequesterId,
            RequesterQuoteItemId = 20
        };

        _ = _itemQuoteServiceMock
            .Setup(q => q.CreateQuoteItemAsync(1, "World", RequesterId, null, default))
            .ReturnsAsync(quoteItem);

        _ = _voteCoordinatorMock
            .Setup(v => v.ToggleReactionAsync(20, RequesterId, ReactionTagNames.Shinji, -1, null, It.IsAny<IReadOnlyList<Tag>>(), null))
            .ReturnsAsync(voteResult);

        _ = _itemSplitServiceMock
            .Setup(s => s.RequestSplitAsync(1, "World", RequesterId, 20, default))
            .ReturnsAsync(new Success<ItemSplitRequest>(splitRequest));

        var result = await _coordinator.RequestSplitWithReactionAsync(
            item, RequesterId, ReactionTagNames.Shinji, -1, null, [], null, "World");

        Assert.NotNull(result);
        Assert.Same(splitRequest, result.SplitRequest);
        Assert.Same(voteResult, result.VoteResult);
        _voteCoordinatorMock.Verify(v => v.ToggleReactionAsync(20, RequesterId, ReactionTagNames.Shinji, -1, null, It.IsAny<IReadOnlyList<Tag>>(), null), Times.Once);
    }

    [Fact]
    public async Task RequestSplitWithReactionAsync_PassesEnsureSystemTagsCallbackToVoteCoordinator()
    {
        var item = new Item { Id = 1, OwnerId = OwnerId, Content = "Hello World" };
        var quoteItem = new Item { Id = 21, OwnerId = RequesterId, Content = "World" };
        var voteResult = new ItemVoteResult(ItemVoteAction.Added, 21, 201);
        var splitRequest = new ItemSplitRequest
        {
            Id = 61,
            OwnerId = OwnerId,
            OriginalItemId = 1,
            SelectedText = "World",
            RequesterUserId = RequesterId,
            RequesterQuoteItemId = 21
        };

        Func<Task> ensureSystemTags = () => Task.CompletedTask;

        _ = _itemQuoteServiceMock
            .Setup(q => q.CreateQuoteItemAsync(1, "World", RequesterId, null, default))
            .ReturnsAsync(quoteItem);

        _ = _voteCoordinatorMock
            .Setup(v => v.ToggleReactionAsync(21, RequesterId, ReactionTagNames.Shinji, 1, null, It.IsAny<IReadOnlyList<Tag>>(), ensureSystemTags))
            .ReturnsAsync(voteResult);

        _ = _itemSplitServiceMock
            .Setup(s => s.RequestSplitAsync(1, "World", RequesterId, 21, default))
            .ReturnsAsync(new Success<ItemSplitRequest>(splitRequest));

        var result = await _coordinator.RequestSplitWithReactionAsync(
            item, RequesterId, ReactionTagNames.Shinji, 1, null, [], ensureSystemTags, "World");

        Assert.NotNull(result);
        _voteCoordinatorMock.Verify(v => v.ToggleReactionAsync(21, RequesterId, ReactionTagNames.Shinji, 1, null, It.IsAny<IReadOnlyList<Tag>>(), ensureSystemTags), Times.Once);
    }

    [Fact]
    public async Task ApproveSplitRequestAsync_WhenServiceReturnsFailure_ShowsErrorSnackbarAndReturnsFalse()
    {
        var item = new Item { Id = 1, OwnerId = OwnerId, Content = "Hello World" };
        var request = new ItemSplitRequest
        {
            Id = 70,
            OwnerId = OwnerId,
            OriginalItemId = 1,
            SelectedText = "World",
            RequesterUserId = RequesterId
        };

        _ = _itemSplitServiceMock
            .Setup(s => s.ApproveSplitAsync(70, OwnerId, default))
            .ReturnsAsync(new Failure("元アイテムの本文が変更されています。"));

        var result = await _coordinator.ApproveSplitRequestAsync(item, request, OwnerId);

        Assert.False(result);
        _snackbarMock.Verify(s => s.Add("元アイテムの本文が変更されています。", Severity.Error, It.IsAny<Action<SnackbarOptions>>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task CancelSplitRequestAsync_WhenServiceReturnsFailure_ShowsErrorSnackbarAndReturnsFalse()
    {
        var request = new ItemSplitRequest
        {
            Id = 71,
            OwnerId = OwnerId,
            OriginalItemId = 1,
            SelectedText = "World",
            RequesterUserId = RequesterId
        };

        _ = _itemSplitServiceMock
            .Setup(s => s.CancelSplitAsync(71, RequesterId, default))
            .ReturnsAsync(new Failure("既に処理済みのリクエストです。"));

        var result = await _coordinator.CancelSplitRequestAsync(request, RequesterId);

        Assert.False(result);
        _snackbarMock.Verify(s => s.Add("既に処理済みのリクエストです。", Severity.Error, It.IsAny<Action<SnackbarOptions>>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task GetValidSelectedTextForSplitAsync_WhenItemNull_ReturnsNull()
    {
        var result = await _coordinator.GetValidSelectedTextForSplitAsync(null!, RequesterId);

        Assert.Null(result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetValidSelectedTextForSplitAsync_WhenUserIdIsNullOrWhiteSpace_ReturnsNull(string? userId)
    {
        var item = new Item { Id = 1, OwnerId = OwnerId, Content = "Hello World" };

        var result = await _coordinator.GetValidSelectedTextForSplitAsync(item, userId);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetValidSelectedTextForSplitAsync_WhenUserIsOwner_ReturnsNull()
    {
        var item = new Item { Id = 1, OwnerId = OwnerId, Content = "Hello World" };

        var result = await _coordinator.GetValidSelectedTextForSplitAsync(item, OwnerId);

        Assert.Null(result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task GetValidSelectedTextForSplitAsync_WhenNoTextSelected_ReturnsNull(string? selectedText)
    {
        var item = new Item { Id = 1, OwnerId = OwnerId, Content = "Hello World" };

        _ = _jsMock.Setup(j => j.InvokeAsync<string>("selectionHelper.getSelectedText", It.IsAny<object[]>()))
            .Returns(ValueTask.FromResult(selectedText!));

        var result = await _coordinator.GetValidSelectedTextForSplitAsync(item, RequesterId);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetValidSelectedTextForSplitAsync_WhenJSThrowsException_ReturnsNull()
    {
        var item = new Item { Id = 1, OwnerId = OwnerId, Content = "Hello World" };

        _ = _jsMock.Setup(j => j.InvokeAsync<string>("selectionHelper.getSelectedText", It.IsAny<object[]>()))
            .Throws(new JSException("DOM exception"));

        var result = await _coordinator.GetValidSelectedTextForSplitAsync(item, RequesterId);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetValidSelectedTextForSplitAsync_WhenSelectedTextNotInItemContent_ReturnsNull()
    {
        var item = new Item { Id = 1, OwnerId = OwnerId, Content = "Hello World" };

        _ = _jsMock.Setup(j => j.InvokeAsync<string>("selectionHelper.getSelectedText", It.IsAny<object[]>()))
            .Returns(ValueTask.FromResult("Not in content"));

        var result = await _coordinator.GetValidSelectedTextForSplitAsync(item, RequesterId);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetValidSelectedTextForSplitAsync_WhenValidSelectedTextOnOtherUserItem_ReturnsSelectedText()
    {
        var item = new Item { Id = 1, OwnerId = OwnerId, Content = "Hello World" };

        _ = _jsMock.Setup(j => j.InvokeAsync<string>("selectionHelper.getSelectedText", It.IsAny<object[]>()))
            .Returns(ValueTask.FromResult("World"));

        var result = await _coordinator.GetValidSelectedTextForSplitAsync(item, RequesterId);

        Assert.Equal("World", result);
    }

    [Fact]
    public async Task GetValidSelectedTextForSplitAsync_WhenCrlfInItemAndLfInSelectedText_ReturnsSelectedText()
    {
        var item = new Item { Id = 1, OwnerId = OwnerId, Content = "Line1\r\nLine2\r\nLine3" };

        _ = _jsMock.Setup(j => j.InvokeAsync<string>("selectionHelper.getSelectedText", It.IsAny<object[]>()))
            .Returns(ValueTask.FromResult("Line1\nLine2"));

        var result = await _coordinator.GetValidSelectedTextForSplitAsync(item, RequesterId);

        Assert.Equal("Line1\nLine2", result);
    }

    [Fact]
    public async Task RejectSplitRequestAsync_WhenDialogCancelled_ReturnsFalse()
    {
        var item = new Item { Id = 1, OwnerId = OwnerId, Content = "Hello World" };
        var request = new ItemSplitRequest { Id = 10, OwnerId = OwnerId, SelectedText = "World" };

        var dialogRef = new Mock<IDialogReference>();
        dialogRef.Setup(r => r.Result).ReturnsAsync(DialogResult.Cancel());
        _ = _dialogLauncherMock
            .Setup(l => l.ShowAsync(typeof(RejectRequestDialog), "分割リクエストを却下", It.IsAny<DialogParameters>(), It.IsAny<DialogOptions>()))
            .ReturnsAsync(dialogRef.Object);

        var result = await _coordinator.RejectSplitRequestAsync(item, request, OwnerId);

        Assert.False(result);
        _itemSplitServiceMock.Verify(s => s.RejectSplitAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>(), default), Times.Never);
    }

    [Fact]
    public async Task RejectSplitRequestAsync_WhenServiceReturnsFailure_ShowsErrorSnackbarAndReturnsFalse()
    {
        var item = new Item { Id = 1, OwnerId = OwnerId, Content = "Hello World" };
        var request = new ItemSplitRequest { Id = 10, OwnerId = OwnerId, SelectedText = "World" };

        var dialogRef = new Mock<IDialogReference>();
        dialogRef.Setup(r => r.Result).ReturnsAsync(DialogResult.Ok<string?>("却下理由"));
        _ = _dialogLauncherMock
            .Setup(l => l.ShowAsync(typeof(RejectRequestDialog), "分割リクエストを却下", It.IsAny<DialogParameters>(), It.IsAny<DialogOptions>()))
            .ReturnsAsync(dialogRef.Object);

        _ = _itemSplitServiceMock
            .Setup(s => s.RejectSplitAsync(10, OwnerId, "却下理由", default))
            .ReturnsAsync(new Failure("既に承認済みのリクエストです。"));

        var result = await _coordinator.RejectSplitRequestAsync(item, request, OwnerId);

        Assert.False(result);
        _snackbarMock.Verify(s => s.Add("既に承認済みのリクエストです。", Severity.Error, It.IsAny<Action<SnackbarOptions>>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task RequestSplitSelectionAsync_WhenUserIsOwner_ShowsErrorSnackbarWithoutOpeningDialog_AndReturnsNull()
    {
        var item = new Item { Id = 1, OwnerId = OwnerId, Content = "Hello World" };

        var result = await _coordinator.RequestSplitSelectionAsync(item, OwnerId);

        Assert.Null(result);
        _dialogLauncherMock.Verify(
            l => l.ShowAsync(typeof(SplitRequestWithQuoteDialog), It.IsAny<string>(), It.IsAny<DialogParameters>(), It.IsAny<DialogOptions>()),
            Times.Never);
        _itemSplitServiceMock.Verify(
            s => s.RequestSplitAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _snackbarMock.Verify(s => s.Add(ErrorMessages.SplitSelfItemNotAllowed, Severity.Error, It.IsAny<Action<SnackbarOptions>>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task RequestSplitSelectionAsync_WhenServiceReturnsFailure_AndQuoteItemWasCreated_DeletesQuoteItem()
    {
        var item = new Item { Id = 1, OwnerId = OwnerId, Content = "Hello World" };
        var createdQuote = new Item { Id = 91, OwnerId = RequesterId, Content = "World" };

        _ = _jsMock.Setup(j => j.InvokeAsync<string>("selectionHelper.getSelectedText", It.IsAny<object[]>()))
            .Returns(ValueTask.FromResult("World"));

        var dialogRef = new Mock<IDialogReference>();
        dialogRef.Setup(r => r.Result).ReturnsAsync(DialogResult.Ok(new SplitRequestWithQuoteDialogResult(91, createdQuote)));
        _ = _dialogLauncherMock
            .Setup(l => l.ShowAsync(typeof(SplitRequestWithQuoteDialog), "アイテム分割リクエストの送信", It.IsAny<DialogParameters>(), It.IsAny<DialogOptions>()))
            .ReturnsAsync(dialogRef.Object);

        _ = _itemSplitServiceMock
            .Setup(s => s.RequestSplitAsync(1, "World", RequesterId, 91, default))
            .ReturnsAsync(new Failure("このテキストに対する分割リクエストは既に申請中です。"));

        var result = await _coordinator.RequestSplitSelectionAsync(item, RequesterId);

        Assert.Null(result);
        _itemCardDataProviderMock.Verify(d => d.DeleteItemAsync(91), Times.Once);
        _snackbarMock.Verify(s => s.Add("このテキストに対する分割リクエストは既に申請中です。", Severity.Error, It.IsAny<Action<SnackbarOptions>>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task RequestSplitWithTagAddAsync_WhenCanAttachDirectly_CreatesQuote_AddsFreeTag_AndSubmitsSplitRequest()
    {
        var item = new Item { Id = 1, OwnerId = OwnerId, Content = "Hello World" };
        var selectedTag = new Tag { Id = 10, Name = "Tag10", OwnerId = RequesterId };
        var quoteItem = new Item { Id = 55, OwnerId = RequesterId, Content = "World", QuotedItemId = 1, TagRelations = [] };
        var createdRelation = new TagRelation { Id = 101, ItemId = 55, TagId = 10, OwnerId = RequesterId };
        var splitRequest = new ItemSplitRequest
        {
            Id = 201,
            OriginalItemId = 1,
            SelectedText = "World",
            RequesterUserId = RequesterId,
            OwnerId = RequesterId,
            RequesterQuoteItemId = 55
        };

        var tagDialogRef = new Mock<IDialogReference>();
        tagDialogRef.Setup(r => r.Result).ReturnsAsync(DialogResult.Ok(selectedTag));
        _ = _dialogLauncherMock
            .Setup(l => l.ShowAsync(typeof(TagAddDialog), "タグの追加", It.IsAny<DialogParameters>(), It.IsAny<DialogOptions>()))
            .ReturnsAsync(tagDialogRef.Object);

        _ = _itemCardDataProviderMock.Setup(d => d.GetTagWithOwnerAsync(10)).ReturnsAsync(selectedTag);
        _ = _itemQuoteServiceMock.Setup(q => q.CreateQuoteItemAsync(1, "World", RequesterId, null, default)).ReturnsAsync(quoteItem);
        _ = _itemCardDataProviderMock.Setup(d => d.CanUserAttachTagDirectlyAsync(10, RequesterId)).ReturnsAsync(true);
        _ = _itemCardDataProviderMock.Setup(d => d.AddFreeTagRelationAsync(55, 10, RequesterId)).ReturnsAsync(createdRelation);
        _ = _itemSplitServiceMock.Setup(s => s.RequestSplitAsync(1, "World", RequesterId, 55, default)).ReturnsAsync(new Success<ItemSplitRequest>(splitRequest));

        TagAddOutcome outcome = await _coordinator.RequestSplitWithTagAddAsync(item, RequesterId, "World");

        Assert.Equal(TagAddOutcome.AddedDirectly, outcome);
        Assert.Single(quoteItem.TagRelations);
        Assert.Equal(10, quoteItem.TagRelations.Single().TagId);
        _itemCardDataProviderMock.Verify(d => d.DeleteItemAsync(It.IsAny<int>()), Times.Never);
        _snackbarMock.Verify(s => s.Add("引用してタグを追加し、アイテム分割リクエストを送信しました。", Severity.Success, It.IsAny<Action<SnackbarOptions>>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task RequestSplitWithTagAddAsync_WhenCannotAttachDirectly_OpensProposeContractDialogTargetingQuoteItem_AndSubmitsSplitRequest()
    {
        var item = new Item { Id = 1, OwnerId = OwnerId, Content = "Hello World" };
        var selectedTag = new Tag { Id = 20, Name = "ProtectedTag", OwnerId = OwnerId };
        var quoteItem = new Item { Id = 56, OwnerId = RequesterId, Content = "World", QuotedItemId = 1 };
        var splitRequest = new ItemSplitRequest
        {
            Id = 202,
            OriginalItemId = 1,
            SelectedText = "World",
            RequesterUserId = RequesterId,
            OwnerId = RequesterId,
            RequesterQuoteItemId = 56
        };

        var tagDialogRef = new Mock<IDialogReference>();
        tagDialogRef.Setup(r => r.Result).ReturnsAsync(DialogResult.Ok(selectedTag));
        _ = _dialogLauncherMock
            .Setup(l => l.ShowAsync(typeof(TagAddDialog), "タグの追加", It.IsAny<DialogParameters>(), It.IsAny<DialogOptions>()))
            .ReturnsAsync(tagDialogRef.Object);

        var proposeDialogRef = new Mock<IDialogReference>();
        proposeDialogRef.Setup(r => r.Result).ReturnsAsync(DialogResult.Ok<object?>(null));
        _ = _dialogLauncherMock
            .Setup(l => l.ShowAsync(typeof(ProposeContractDialog), "コントラクトの提案", It.IsAny<DialogParameters>(), It.IsAny<DialogOptions>()))
            .ReturnsAsync(proposeDialogRef.Object);

        _ = _itemCardDataProviderMock.Setup(d => d.GetTagWithOwnerAsync(20)).ReturnsAsync(selectedTag);
        _ = _itemQuoteServiceMock.Setup(q => q.CreateQuoteItemAsync(1, "World", RequesterId, null, default)).ReturnsAsync(quoteItem);
        _ = _itemCardDataProviderMock.Setup(d => d.CanUserAttachTagDirectlyAsync(20, RequesterId)).ReturnsAsync(false);
        _ = _itemSplitServiceMock.Setup(s => s.RequestSplitAsync(1, "World", RequesterId, 56, default)).ReturnsAsync(new Success<ItemSplitRequest>(splitRequest));

        TagAddOutcome outcome = await _coordinator.RequestSplitWithTagAddAsync(item, RequesterId, "World");

        Assert.Equal(TagAddOutcome.ContractProposed, outcome);
        _dialogLauncherMock.Verify(
            l => l.ShowAsync(
                typeof(ProposeContractDialog),
                "コントラクトの提案",
                It.Is<DialogParameters>(p => ReferenceEquals(p.Get<Item>(nameof(ProposeContractDialog.TargetItem)), quoteItem)),
                It.IsAny<DialogOptions>()),
            Times.Once);
        _itemCardDataProviderMock.Verify(d => d.DeleteItemAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task RequestSplitWithTagAddAsync_WhenProposeContractDialogCancelled_DeletesQuoteItemAndReturnsNone()
    {
        var item = new Item { Id = 1, OwnerId = OwnerId, Content = "Hello World" };
        var selectedTag = new Tag { Id = 20, Name = "ProtectedTag", OwnerId = OwnerId };
        var quoteItem = new Item { Id = 57, OwnerId = RequesterId, Content = "World", QuotedItemId = 1 };

        var tagDialogRef = new Mock<IDialogReference>();
        tagDialogRef.Setup(r => r.Result).ReturnsAsync(DialogResult.Ok(selectedTag));
        _ = _dialogLauncherMock
            .Setup(l => l.ShowAsync(typeof(TagAddDialog), "タグの追加", It.IsAny<DialogParameters>(), It.IsAny<DialogOptions>()))
            .ReturnsAsync(tagDialogRef.Object);

        var proposeDialogRef = new Mock<IDialogReference>();
        proposeDialogRef.Setup(r => r.Result).ReturnsAsync(DialogResult.Cancel());
        _ = _dialogLauncherMock
            .Setup(l => l.ShowAsync(typeof(ProposeContractDialog), "コントラクトの提案", It.IsAny<DialogParameters>(), It.IsAny<DialogOptions>()))
            .ReturnsAsync(proposeDialogRef.Object);

        _ = _itemCardDataProviderMock.Setup(d => d.GetTagWithOwnerAsync(20)).ReturnsAsync(selectedTag);
        _ = _itemQuoteServiceMock.Setup(q => q.CreateQuoteItemAsync(1, "World", RequesterId, null, default)).ReturnsAsync(quoteItem);
        _ = _itemCardDataProviderMock.Setup(d => d.CanUserAttachTagDirectlyAsync(20, RequesterId)).ReturnsAsync(false);

        TagAddOutcome outcome = await _coordinator.RequestSplitWithTagAddAsync(item, RequesterId, "World");

        Assert.Equal(TagAddOutcome.None, outcome);
        _itemCardDataProviderMock.Verify(d => d.DeleteItemAsync(57), Times.Once);
        _itemSplitServiceMock.Verify(
            s => s.RequestSplitAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RequestSplitWithTagAddAsync_WhenSplitRequestFails_DeletesQuoteItemAndReturnsNone()
    {
        var item = new Item { Id = 1, OwnerId = OwnerId, Content = "Hello World" };
        var selectedTag = new Tag { Id = 10, Name = "Tag10", OwnerId = RequesterId };
        var quoteItem = new Item { Id = 58, OwnerId = RequesterId, Content = "World", QuotedItemId = 1 };
        var createdRelation = new TagRelation { Id = 102, ItemId = 58, TagId = 10, OwnerId = RequesterId };

        var tagDialogRef = new Mock<IDialogReference>();
        tagDialogRef.Setup(r => r.Result).ReturnsAsync(DialogResult.Ok(selectedTag));
        _ = _dialogLauncherMock
            .Setup(l => l.ShowAsync(typeof(TagAddDialog), "タグの追加", It.IsAny<DialogParameters>(), It.IsAny<DialogOptions>()))
            .ReturnsAsync(tagDialogRef.Object);

        _ = _itemCardDataProviderMock.Setup(d => d.GetTagWithOwnerAsync(10)).ReturnsAsync(selectedTag);
        _ = _itemQuoteServiceMock.Setup(q => q.CreateQuoteItemAsync(1, "World", RequesterId, null, default)).ReturnsAsync(quoteItem);
        _ = _itemCardDataProviderMock.Setup(d => d.CanUserAttachTagDirectlyAsync(10, RequesterId)).ReturnsAsync(true);
        _ = _itemCardDataProviderMock.Setup(d => d.AddFreeTagRelationAsync(58, 10, RequesterId)).ReturnsAsync(createdRelation);
        _ = _itemSplitServiceMock.Setup(s => s.RequestSplitAsync(1, "World", RequesterId, 58, default))
            .ReturnsAsync(new Failure("既に分割リクエストが存在します。"));

        TagAddOutcome outcome = await _coordinator.RequestSplitWithTagAddAsync(item, RequesterId, "World");

        Assert.Equal(TagAddOutcome.None, outcome);
        _itemCardDataProviderMock.Verify(d => d.DeleteItemAsync(58), Times.Once);
        _snackbarMock.Verify(s => s.Add("既に分割リクエストが存在します。", Severity.Error, It.IsAny<Action<SnackbarOptions>>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task RequestSplitSelectionAsync_WhenNoTextSelected_ShowsWarningSnackbarAndReturnsNull()
    {
        var item = new Item { Id = 1, OwnerId = OwnerId, Content = "Hello World" };

        _ = _jsMock.Setup(j => j.InvokeAsync<string>("selectionHelper.getSelectedText", It.IsAny<object[]>()))
            .Returns(ValueTask.FromResult("   "));

        var result = await _coordinator.RequestSplitSelectionAsync(item, RequesterId);

        Assert.Null(result);
        _snackbarMock.Verify(s => s.Add("分割するテキストが選択されていません。", Severity.Warning, It.IsAny<Action<SnackbarOptions>>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task RequestSplitSelectionAsync_WhenSelectedTextNotInContent_ShowsErrorSnackbarAndReturnsNull()
    {
        var item = new Item { Id = 1, OwnerId = OwnerId, Content = "Hello World" };

        _ = _jsMock.Setup(j => j.InvokeAsync<string>("selectionHelper.getSelectedText", It.IsAny<object[]>()))
            .Returns(ValueTask.FromResult("別のテキスト"));

        var result = await _coordinator.RequestSplitSelectionAsync(item, RequesterId);

        Assert.Null(result);
        _snackbarMock.Verify(s => s.Add("選択したテキストがこのアイテムの本文に含まれていません。", Severity.Error, It.IsAny<Action<SnackbarOptions>>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task RequestSplitWithVoteAsync_WhenIsOwner_ReturnsNullAndShowsWarning()
    {
        var item = new Item { Id = 1, OwnerId = OwnerId, Content = "Hello World" };

        var result = await _coordinator.RequestSplitWithVoteAsync(item, OwnerId, null, true, null, "World");

        Assert.Null(result);
        _snackbarMock.Verify(s => s.Add(ErrorMessages.SplitSelfItemNotAllowed, Severity.Error, It.IsAny<Action<SnackbarOptions>>(), It.IsAny<string?>()), Times.Once);
        _itemQuoteServiceMock.Verify(q => q.CreateQuoteItemAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<int>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RequestSplitWithVoteAsync_WhenSuccessful_CreatesQuote_TogglesVote_SubmitsSplitRequest_AndReturnsResult()
    {
        var item = new Item { Id = 1, OwnerId = OwnerId, Content = "Hello World" };
        var quoteItem = new Item { Id = 10, Content = "Quote", OwnerId = RequesterId, QuotedItemId = 1 };
        var splitRequest = new ItemSplitRequest { Id = 20, OriginalItemId = 1, RequesterQuoteItemId = 10, OwnerId = RequesterId };

        _ = _itemQuoteServiceMock.Setup(q => q.CreateQuoteItemAsync(1, "World", RequesterId, null, default))
            .ReturnsAsync(quoteItem);
        _ = _voteCoordinatorMock.Setup(v => v.ToggleVoteAsync(10, RequesterId, null, true, null))
            .ReturnsAsync(true);
        _ = _itemSplitServiceMock.Setup(s => s.RequestSplitAsync(1, "World", RequesterId, 10, default))
            .ReturnsAsync(new Success<ItemSplitRequest>(splitRequest));

        var result = await _coordinator.RequestSplitWithVoteAsync(item, RequesterId, null, true, null, "World");

        Assert.NotNull(result);
        Assert.Equal(20, result.Id);
        _voteCoordinatorMock.Verify(v => v.ToggleVoteAsync(10, RequesterId, null, true, null), Times.Once);
        _itemSplitServiceMock.Verify(s => s.RequestSplitAsync(1, "World", RequesterId, 10, default), Times.Once);
        _snackbarMock.Verify(s => s.Add("引用してGood投票し、アイテム分割リクエストを送信しました。", Severity.Success, It.IsAny<Action<SnackbarOptions>>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task RequestSplitWithVoteAsync_WhenSplitRequestFails_DeletesQuoteItemAndReturnsNull()
    {
        var item = new Item { Id = 1, OwnerId = OwnerId, Content = "Hello World" };
        var quoteItem = new Item { Id = 10, Content = "Quote", OwnerId = RequesterId, QuotedItemId = 1 };

        _ = _itemQuoteServiceMock.Setup(q => q.CreateQuoteItemAsync(1, "World", RequesterId, null, default))
            .ReturnsAsync(quoteItem);
        _ = _voteCoordinatorMock.Setup(v => v.ToggleVoteAsync(10, RequesterId, null, true, null))
            .ReturnsAsync(true);
        _ = _itemSplitServiceMock.Setup(s => s.RequestSplitAsync(1, "World", RequesterId, 10, default))
            .ReturnsAsync(new Failure("分割リクエスト失敗"));

        var result = await _coordinator.RequestSplitWithVoteAsync(item, RequesterId, null, true, null, "World");

        Assert.Null(result);
        _itemCardDataProviderMock.Verify(d => d.DeleteItemAsync(10), Times.Once);
        _snackbarMock.Verify(s => s.Add("分割リクエスト失敗", Severity.Error, It.IsAny<Action<SnackbarOptions>>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task RequestSplitWithReactionAsync_WhenIsOwner_ReturnsNullAndShowsWarning()
    {
        var item = new Item { Id = 1, OwnerId = OwnerId, Content = "Hello World" };

        var result = await _coordinator.RequestSplitWithReactionAsync(item, OwnerId, "Shinji", 1, null, [], null, "World");

        Assert.Null(result);
        _snackbarMock.Verify(s => s.Add(ErrorMessages.SplitSelfItemNotAllowed, Severity.Error, It.IsAny<Action<SnackbarOptions>>(), It.IsAny<string?>()), Times.Once);
        _itemQuoteServiceMock.Verify(q => q.CreateQuoteItemAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<int>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RequestSplitWithTagAddAsync_WhenIsOwner_ReturnsNoneAndShowsWarning()
    {
        var item = new Item { Id = 1, OwnerId = OwnerId, Content = "Hello World" };

        TagAddOutcome outcome = await _coordinator.RequestSplitWithTagAddAsync(item, OwnerId, "World");

        Assert.Equal(TagAddOutcome.None, outcome);
        _snackbarMock.Verify(s => s.Add(ErrorMessages.SplitSelfItemNotAllowed, Severity.Error, It.IsAny<Action<SnackbarOptions>>(), It.IsAny<string?>()), Times.Once);
        _itemQuoteServiceMock.Verify(q => q.CreateQuoteItemAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<int>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}