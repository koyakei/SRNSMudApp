using Microsoft.JSInterop;

using Moq;

using MudBlazor;

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
    private readonly Mock<IDialogLauncher> _dialogLauncherMock = new();
    private readonly Mock<ISnackbar> _snackbarMock = new();
    private readonly Mock<IJSRuntime> _jsMock = new();
    private readonly ItemCardSplitCoordinator _coordinator;

    public ItemCardSplitCoordinatorTests()
    {
        _coordinator = new ItemCardSplitCoordinator(
            _itemSplitServiceMock.Object,
            _dialogLauncherMock.Object,
            _snackbarMock.Object,
            _jsMock.Object);
    }

    [Fact]
    public void Constructor_NullParameters_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new ItemCardSplitCoordinator(null!, _dialogLauncherMock.Object, _snackbarMock.Object, _jsMock.Object));
        Assert.Throws<ArgumentNullException>(() => new ItemCardSplitCoordinator(_itemSplitServiceMock.Object, null!, _snackbarMock.Object, _jsMock.Object));
        Assert.Throws<ArgumentNullException>(() => new ItemCardSplitCoordinator(_itemSplitServiceMock.Object, _dialogLauncherMock.Object, null!, _jsMock.Object));
        Assert.Throws<ArgumentNullException>(() => new ItemCardSplitCoordinator(_itemSplitServiceMock.Object, _dialogLauncherMock.Object, _snackbarMock.Object, null!));
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
}