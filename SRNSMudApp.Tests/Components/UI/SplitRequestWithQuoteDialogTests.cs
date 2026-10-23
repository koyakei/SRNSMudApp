using Bunit;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.Item;
using SRNSMudApp.Components.UI;
using SRNSMudApp.Data;
using SRNSMudApp.Services;
using SRNSMudApp.Tests.TestSupport;

namespace SRNSMudApp.Tests.Components.UI;

using DataItem = SRNSMudApp.Data.Item;

/// <summary>
///     <see cref="SplitRequestWithQuoteDialog" /> の bUnit 単体テスト。
///     選択テキストのプレビュー表示、引用コメント入力有無による投稿分岐、
///     引用作成失敗時のハンドリング、キャンセル動作を検証する。
/// </summary>
public sealed class SplitRequestWithQuoteDialogTests : IAsyncLifetime
{
    private const string CurrentUserId = "test-user-1";
    private readonly BunitContext _ctx = new();
    private readonly Mock<IItemQuoteService> _itemQuoteServiceMock = new();
    private readonly Mock<IItemReplyService> _itemReplyServiceMock = new();
    private readonly Mock<IItemCardDataProvider> _itemCardDataMock = new();
    private readonly Mock<ILinkPreviewService> _linkPreviewServiceMock = new();

    public SplitRequestWithQuoteDialogTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices().AddMockSrnsServices();

        _ = _ctx.Services.AddScoped(_ => _itemQuoteServiceMock.Object);
        _ = _ctx.Services.AddScoped(_ => _itemReplyServiceMock.Object);
        _ = _ctx.Services.AddScoped(_ => _itemCardDataMock.Object);
        _ = _ctx.Services.AddScoped(_ => _linkPreviewServiceMock.Object);
        _ = _ctx.Services.AddTransient<QuoteItemViewModel>();

        _ = _ctx.Render<MudPopoverProvider>();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _ctx.DisposeAsync().AsTask();

    [Fact]
    public async Task SplitRequestWithQuoteDialog_RendersSelectedTextAndQuotedItem()
    {
        // Arrange
        var host = _ctx.Render<DialogHost>();
        var dialogService = _ctx.Services.GetRequiredService<IDialogService>();

        var quotedItem = new DataItem
        {
            Id = 10,
            Content = "元アイテムの本文",
            OwnerId = "other-user"
        };

        var parameters = new DialogParameters<SplitRequestWithQuoteDialog>
        {
            { x => x.SelectedText, "切り抜くテキスト部分" },
            { x => x.QuotedItem, quotedItem },
            { x => x.CurrentUserId, CurrentUserId }
        };

        // Act
        _ = await dialogService.ShowAsync<SplitRequestWithQuoteDialog>("アイテム分割リクエストの送信", parameters);
        host.WaitForAssertion(() =>
        {
            Assert.Contains("アイテム分割リクエストの送信", host.Markup);
            Assert.Contains("切り抜くテキスト部分", host.Markup);
        });
    }

    [Fact]
    public async Task SplitRequestWithQuoteDialog_WhenSubmittedWithoutQuoteContent_ClosesWithNullCreatedQuoteId()
    {
        // Arrange
        var host = _ctx.Render<DialogHost>();
        var dialogService = _ctx.Services.GetRequiredService<IDialogService>();

        var quotedItem = new DataItem { Id = 11, Content = "元アイテム本文", OwnerId = "other-user" };
        var parameters = new DialogParameters<SplitRequestWithQuoteDialog>
        {
            { x => x.SelectedText, "対象テキスト" },
            { x => x.QuotedItem, quotedItem },
            { x => x.CurrentUserId, CurrentUserId }
        };

        var dialogRef = await dialogService.ShowAsync<SplitRequestWithQuoteDialog>("アイテム分割リクエストの送信", parameters);

        // Act: 引用コメントを空のまま送信ボタンをクリック
        host.WaitForAssertion(() =>
        {
            var submitButton = host.Find("button[data-testid='confirm-split-request-with-quote-button']");
            Assert.NotNull(submitButton);
        });

        var submitBtn = host.Find("button[data-testid='confirm-split-request-with-quote-button']");
        await host.InvokeAsync(() => submitBtn.Click());

        // Assert: 引用アイテム作成は呼ばれず、CreatedQuoteItemId == null でダイアログが閉じる
        var result = await dialogRef.Result;
        Assert.NotNull(result);
        Assert.False(result.Canceled);
        var dialogResult = Assert.IsType<SplitRequestWithQuoteDialogResult>(result.Data);
        Assert.Null(dialogResult.CreatedQuoteItemId);
        Assert.Null(dialogResult.CreatedQuoteItem);

        _itemQuoteServiceMock.Verify(s => s.CreateQuoteItemAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<int>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SplitRequestWithQuoteDialog_WhenSubmittedWithQuoteContent_CreatesQuoteWithSelectedTextAndReplyComment_AndClosesWithId()
    {
        // Arrange
        var host = _ctx.Render<DialogHost>();
        var dialogService = _ctx.Services.GetRequiredService<IDialogService>();

        var quotedItem = new DataItem { Id = 12, Content = "元アイテム本文", OwnerId = "other-user" };
        var createdQuoteItem = new DataItem { Id = 77, Content = "対象テキスト", OwnerId = CurrentUserId };
        var createdReplyItem = new DataItem { Id = 78, Content = "私の引用コメント", OwnerId = CurrentUserId, ParentItemId = 77 };

        _itemQuoteServiceMock
            .Setup(s => s.CreateQuoteItemAsync(12, "対象テキスト", CurrentUserId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdQuoteItem);

        _itemReplyServiceMock
            .Setup(r => r.AddItemReplyAsync(77, "私の引用コメント", CurrentUserId, null, false, null))
            .ReturnsAsync(createdReplyItem);

        var parameters = new DialogParameters<SplitRequestWithQuoteDialog>
        {
            { x => x.SelectedText, "対象テキスト" },
            { x => x.QuotedItem, quotedItem },
            { x => x.CurrentUserId, CurrentUserId }
        };

        var dialogRef = await dialogService.ShowAsync<SplitRequestWithQuoteDialog>("アイテム分割リクエストの送信", parameters);

        // Act: 引用コメントを入力して送信
        host.WaitForAssertion(() =>
        {
            var input = host.Find("textarea");
            Assert.NotNull(input);
        });

        var textarea = host.Find("textarea");
        textarea.Input("私の引用コメント");

        var submitBtn = host.Find("button[data-testid='confirm-split-request-with-quote-button']");
        await host.InvokeAsync(() => submitBtn.Click());

        // Assert
        var result = await dialogRef.Result;
        Assert.NotNull(result);
        Assert.False(result.Canceled);
        var dialogResult = Assert.IsType<SplitRequestWithQuoteDialogResult>(result.Data);
        Assert.Equal(77, dialogResult.CreatedQuoteItemId);
        Assert.Equal(createdQuoteItem, dialogResult.CreatedQuoteItem);

        _itemQuoteServiceMock.Verify(s => s.CreateQuoteItemAsync(12, "対象テキスト", CurrentUserId, null, It.IsAny<CancellationToken>()), Times.Once);
        _itemReplyServiceMock.Verify(r => r.AddItemReplyAsync(77, "私の引用コメント", CurrentUserId, null, false, null), Times.Once);
    }

    [Fact]
    public async Task SplitRequestWithQuoteDialog_WhenCreateQuoteFails_ShowsErrorSnackbarAndKeepsDialogOpen()
    {
        // Arrange
        var host = _ctx.Render<DialogHost>();
        var dialogService = _ctx.Services.GetRequiredService<IDialogService>();
        var snackbar = _ctx.Services.GetRequiredService<ISnackbar>();

        var quotedItem = new DataItem { Id = 13, Content = "元アイテム本文", OwnerId = "other-user" };

        _itemQuoteServiceMock
            .Setup(s => s.CreateQuoteItemAsync(13, "対象テキスト", CurrentUserId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DataItem?)null);

        var parameters = new DialogParameters<SplitRequestWithQuoteDialog>
        {
            { x => x.SelectedText, "対象テキスト" },
            { x => x.QuotedItem, quotedItem },
            { x => x.CurrentUserId, CurrentUserId }
        };

        var dialogRef = await dialogService.ShowAsync<SplitRequestWithQuoteDialog>("アイテム分割リクエストの送信", parameters);

        host.WaitForAssertion(() =>
        {
            var input = host.Find("textarea");
            Assert.NotNull(input);
        });

        var textarea = host.Find("textarea");
        textarea.Input("失敗する引用");

        var submitBtn = host.Find("button[data-testid='confirm-split-request-with-quote-button']");
        await host.InvokeAsync(() => submitBtn.Click());

        // Assert: エラー Snackbar が表示され、ダイアログは閉じない
        Assert.Contains(snackbar.ShownSnackbars, s => s.Message.ToString().Contains("引用の投稿に失敗しました"));
        Assert.False(dialogRef.Result.IsCompleted);
    }

    [Fact]
    public async Task SplitRequestWithQuoteDialog_WhenReplySaveFails_DeletesQuoteItem_ShowsErrorSnackbar_AndKeepsDialogOpen()
    {
        // Arrange
        var host = _ctx.Render<DialogHost>();
        var dialogService = _ctx.Services.GetRequiredService<IDialogService>();
        var snackbar = _ctx.Services.GetRequiredService<ISnackbar>();

        var quotedItem = new DataItem { Id = 15, Content = "元アイテム本文", OwnerId = "other-user" };
        var createdQuoteItem = new DataItem { Id = 88, Content = "対象テキスト", OwnerId = CurrentUserId };

        _itemQuoteServiceMock
            .Setup(s => s.CreateQuoteItemAsync(15, "対象テキスト", CurrentUserId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdQuoteItem);

        _itemReplyServiceMock
            .Setup(r => r.AddItemReplyAsync(88, "失敗するコメント", CurrentUserId, null, false, null))
            .ReturnsAsync((DataItem?)null);

        var parameters = new DialogParameters<SplitRequestWithQuoteDialog>
        {
            { x => x.SelectedText, "対象テキスト" },
            { x => x.QuotedItem, quotedItem },
            { x => x.CurrentUserId, CurrentUserId }
        };

        var dialogRef = await dialogService.ShowAsync<SplitRequestWithQuoteDialog>("アイテム分割リクエストの送信", parameters);

        host.WaitForAssertion(() =>
        {
            var input = host.Find("textarea");
            Assert.NotNull(input);
        });

        var textarea = host.Find("textarea");
        textarea.Input("失敗するコメント");

        var submitBtn = host.Find("button[data-testid='confirm-split-request-with-quote-button']");
        await host.InvokeAsync(() => submitBtn.Click());

        // Assert: 作成された引用アイテムがロールバック削除され、エラー Snackbar が表示され、ダイアログは閉じない
        _itemCardDataMock.Verify(d => d.DeleteItemAsync(88), Times.Once);
        Assert.Contains(snackbar.ShownSnackbars, s => s.Message.ToString().Contains("引用コメントの保存に失敗しました"));
        Assert.False(dialogRef.Result.IsCompleted);
    }

    [Fact]
    public async Task SplitRequestWithQuoteDialog_WhenCancelClicked_CancelsDialog()
    {
        // Arrange
        var host = _ctx.Render<DialogHost>();
        var dialogService = _ctx.Services.GetRequiredService<IDialogService>();

        var quotedItem = new DataItem { Id = 14, Content = "元アイテム本文", OwnerId = "other-user" };
        var parameters = new DialogParameters<SplitRequestWithQuoteDialog>
        {
            { x => x.SelectedText, "対象テキスト" },
            { x => x.QuotedItem, quotedItem },
            { x => x.CurrentUserId, CurrentUserId }
        };

        var dialogRef = await dialogService.ShowAsync<SplitRequestWithQuoteDialog>("アイテム分割リクエストの送信", parameters);

        host.WaitForAssertion(() =>
        {
            var cancelButton = host.Find("button:contains('キャンセル')");
            Assert.NotNull(cancelButton);
        });

        var cancelBtn = host.Find("button:contains('キャンセル')");
        await host.InvokeAsync(() => cancelBtn.Click());

        // Assert: キャンセルされること
        var result = await dialogRef.Result;
        Assert.NotNull(result);
        Assert.True(result.Canceled);
    }

    private sealed class DialogHost : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<MudDialogProvider>(0);
            builder.CloseComponent();
        }
    }
}