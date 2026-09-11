using Bunit;

using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.JSInterop;

using Moq;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.UI;
using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;
using SRNSMudApp.Services.Dialogs;
using SRNSMudApp.Tests.TestSupport;

namespace SRNSMudApp.Tests.Components.UI;

using DataItem = SRNSMudApp.Data.Item;

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

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }
}