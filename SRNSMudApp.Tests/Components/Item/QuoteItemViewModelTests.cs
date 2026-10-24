using Moq;

using SRNSMudApp.Components.Item;
using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;

using ItemEntity = SRNSMudApp.Data.Item;

namespace SRNSMudApp.Tests.Components.Item;

/// <summary>
///     <see cref="QuoteItemViewModel"/> の単体テスト。
///     DOM 描画や bUnit を伴わずに高速かつ決定論的に引用投稿、バリデーション、エラーハンドリングを検証する。
/// </summary>
public sealed class QuoteItemViewModelTests
{
    private readonly Mock<IItemQuoteService> _itemQuoteServiceMock = new();
    private readonly Mock<ILinkPreviewService> _linkPreviewServiceMock = new();
    private readonly QuoteItemViewModel _sut;

    public QuoteItemViewModelTests()
    {
        _sut = new QuoteItemViewModel(_itemQuoteServiceMock.Object, _linkPreviewServiceMock.Object);
    }

    [Fact]
    public void Constructor_WhenArgumentNull_ThrowsArgumentNullException()
    {
        _ = Assert.Throws<ArgumentNullException>(() => new QuoteItemViewModel(null!, _linkPreviewServiceMock.Object));
        _ = Assert.Throws<ArgumentNullException>(() => new QuoteItemViewModel(_itemQuoteServiceMock.Object, null!));
    }

    [Fact]
    public async Task GetPreviewAsync_DelegatesToLinkPreviewService()
    {
        // Arrange
        const string testUrl = "https://example.com";
        var expectedData = new LinkPreviewData { Url = testUrl, Title = "Example" };
        _linkPreviewServiceMock.Setup(s => s.GetPreviewAsync(testUrl)).ReturnsAsync(expectedData);

        // Act
        var actual = await _sut.GetPreviewAsync(testUrl);

        // Assert
        Assert.Same(expectedData, actual);
        _linkPreviewServiceMock.Verify(s => s.GetPreviewAsync(testUrl), Times.Once);
    }

    [Fact]
    public void InitialState_IsConfiguredProperly()
    {
        // Assert
        Assert.Null(_sut.QuotedItem);
        Assert.Empty(_sut.CurrentUserId);
        Assert.Empty(_sut.Content);
        Assert.False(_sut.IsSubmitting);
        Assert.False(_sut.CanSubmit);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("素晴らしい考察ですね", true)]
    public void CanSubmit_ValidatesContent(string content, bool expected)
    {
        // Arrange
        _sut.Content = content;

        // Assert
        Assert.Equal(expected, _sut.CanSubmit);
    }

    [Fact]
    public async Task SubmitAsync_WhenContentEmpty_ReturnsFailure()
    {
        // Arrange
        _sut.Content = "   ";
        _sut.CurrentUserId = "user-1";
        _sut.QuotedItem = new ItemEntity { Id = 1, OwnerId = "owner-1", Content = "Original" };

        // Act
        var result = await _sut.SubmitAsync();

        // Assert
        Assert.True(result is Failure);
        if (result is Failure failure)
        {
            Assert.Contains("本文を入力してください", failure.ErrorMessage);
        }

        _itemQuoteServiceMock.Verify(
            s => s.CreateQuoteItemAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<int>?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SubmitAsync_WhenUserIdEmpty_ReturnsFailure()
    {
        // Arrange
        _sut.Content = "引用コメント";
        _sut.CurrentUserId = "";
        _sut.QuotedItem = new ItemEntity { Id = 1, OwnerId = "owner-1", Content = "Original" };

        // Act
        var result = await _sut.SubmitAsync();

        // Assert
        Assert.True(result is Failure);
        if (result is Failure failure)
        {
            Assert.Contains("ログインが必要です", failure.ErrorMessage);
        }
    }

    [Fact]
    public async Task SubmitAsync_WhenQuotedItemNull_ReturnsFailure()
    {
        // Arrange
        _sut.Content = "引用コメント";
        _sut.CurrentUserId = "user-1";
        _sut.QuotedItem = null;

        // Act
        var result = await _sut.SubmitAsync();

        // Assert
        Assert.True(result is Failure);
        if (result is Failure failure)
        {
            Assert.Contains("指定されていません", failure.ErrorMessage);
        }
    }

    [Fact]
    public async Task SubmitAsync_WhenValid_CallsServiceAndReturnsSuccess()
    {
        // Arrange
        const int quotedId = 42;
        const string comment = "この引用について";
        const string userId = "user-100";
        var quotedItem = new ItemEntity { Id = quotedId, OwnerId = "owner-1", Content = "Original item" };
        var createdItem = new ItemEntity { Id = 101, OwnerId = userId, Content = comment };

        _sut.QuotedItem = quotedItem;
        _sut.CurrentUserId = userId;
        _sut.Content = comment;

        _itemQuoteServiceMock.Setup(s => s.CreateQuoteItemAsync(
                quotedId, comment, userId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdItem);

        // Act
        var result = await _sut.SubmitAsync();

        // Assert
        Assert.True(result is Success<ItemEntity>);
        if (result is Success<ItemEntity> success)
        {
            Assert.Equal(101, success.Value.Id);
            Assert.Equal(comment, success.Value.Content);
        }

        Assert.False(_sut.IsSubmitting);
        _itemQuoteServiceMock.Verify(s => s.CreateQuoteItemAsync(
            quotedId, comment, userId, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SubmitAsync_WhenServiceReturnsNull_ReturnsFailure()
    {
        // Arrange
        _sut.QuotedItem = new ItemEntity { Id = 99, OwnerId = "owner-1", Content = "Deleted" };
        _sut.CurrentUserId = "user-1";
        _sut.Content = "Quote";

        _itemQuoteServiceMock.Setup(s => s.CreateQuoteItemAsync(
                99, "Quote", "user-1", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ItemEntity?)null);

        // Act
        var result = await _sut.SubmitAsync();

        // Assert
        Assert.True(result is Failure);
        if (result is Failure failure)
        {
            Assert.Contains("見つかりませんでした", failure.ErrorMessage);
        }

        Assert.False(_sut.IsSubmitting);
    }

    [Fact]
    public async Task SubmitAsync_WhenServiceThrows_ReturnsFailureAndResetsSubmitting()
    {
        // Arrange
        _sut.QuotedItem = new ItemEntity { Id = 99, OwnerId = "owner-1", Content = "Test" };
        _sut.CurrentUserId = "user-1";
        _sut.Content = "Quote";

        _itemQuoteServiceMock.Setup(s => s.CreateQuoteItemAsync(
                99, "Quote", "user-1", null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DB error"));

        // Act
        var result = await _sut.SubmitAsync();

        // Assert
        Assert.True(result is Failure);
        if (result is Failure failure)
        {
            Assert.Contains("DB error", failure.ErrorMessage);
        }

        Assert.False(_sut.IsSubmitting);
    }

    [Fact]
    public async Task SubmitAsync_WhenCalledRapidlyOrInFlight_ReturnsDuplicateFailure()
    {
        // Arrange
        _sut.QuotedItem = new ItemEntity { Id = 88, OwnerId = "owner-1", Content = "Test" };
        _sut.CurrentUserId = "user-1";
        _sut.Content = "Quote message";

        var tcs = new TaskCompletionSource<ItemEntity?>();
        _itemQuoteServiceMock.Setup(s => s.CreateQuoteItemAsync(
                88, "Quote message", "user-1", null, It.IsAny<CancellationToken>()))
            .Returns(tcs.Task);

        // 1回目の送信を開始
        var firstTask = _sut.SubmitAsync();

        // 2回目の送信（処理中のため重複抑止）
        var secondResult = await _sut.SubmitAsync();

        Assert.True(secondResult is Failure);
        if (secondResult is Failure failure)
        {
            Assert.Contains(SRNSMudApp.Client.Services.DoubleSubmissionGuard.DuplicateSubmissionMessage, failure.ErrorMessage);
        }

        // 1回目の処理を完了
        tcs.SetResult(new ItemEntity { Id = 89, Content = "Quote message", OwnerId = "user-1" });
        var firstResult = await firstTask;

        Assert.True(firstResult is Success<ItemEntity>);
        _itemQuoteServiceMock.Verify(s => s.CreateQuoteItemAsync(
            88, "Quote message", "user-1", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SubmitAsync_WhenServerReturnsDuplicateItem_ReturnsDuplicateFailure()
    {
        // Arrange
        _sut.QuotedItem = new ItemEntity { Id = 88, OwnerId = "owner-1", Content = "Test" };
        _sut.CurrentUserId = "user-1";
        _sut.Content = "Quote message";

        var duplicateItem = new ItemEntity { Id = 89, Content = "Quote message", OwnerId = "user-1", IsDuplicate = true };
        _itemQuoteServiceMock.Setup(s => s.CreateQuoteItemAsync(
                88, "Quote message", "user-1", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(duplicateItem);

        // Act
        var result = await _sut.SubmitAsync();

        // Assert
        Assert.True(result is Failure);
        if (result is Failure failure)
        {
            Assert.Contains(SRNSMudApp.Client.Services.DoubleSubmissionGuard.DuplicateSubmissionMessage, failure.ErrorMessage);
        }
    }
}