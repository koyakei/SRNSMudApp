using Moq;

using SRNSMudApp.Components.PublicOffer;
using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;
using SRNSMudApp.Services.Commands;

using TagEntity = SRNSMudApp.Data.Tag;

namespace SRNSMudApp.Tests.Components.PublicOffer;

/// <summary>
///     <see cref="CreatePublicOfferViewModel"/> の単体テスト。
///     DOM 描画や bUnit を伴わずに高速かつ決定論的にオファー作成・タグ検索・バリデーションを検証する。
/// </summary>
public sealed class CreatePublicOfferViewModelTests
{
    private readonly Mock<IContractLookupDataProvider> _contractDataMock = new();
    private readonly Mock<ICommandHandler<CreatePublicOfferCommand, Result<bool>>> _createHandlerMock = new();

    private readonly CreatePublicOfferViewModel _sut;

    public CreatePublicOfferViewModelTests()
    {
        _sut = new CreatePublicOfferViewModel(_contractDataMock.Object, _createHandlerMock.Object);
    }

    [Fact]
    public void InitialState_IsConfiguredProperly()
    {
        // Assert
        Assert.Empty(_sut.CurrentUserId);
        Assert.Null(_sut.SelectedTag);
        Assert.Equal(0, _sut.RequiredAssetAmount);
        Assert.False(_sut.CanSubmit);
    }

    [Theory]
    [InlineData("user-1", true, 0, true)]
    [InlineData("user-1", true, 50, true)]
    [InlineData("", true, 10, false)]
    [InlineData("user-1", false, 10, false)]
    [InlineData("user-1", true, -1, false)]
    public void CanSubmit_ValidatesConditions(string userId, bool hasTag, int requiredAsset, bool expected)
    {
        // Arrange
        _sut.CurrentUserId = userId;
        _sut.SelectedTag = hasTag ? new TagEntity { Id = 1, Name = "TestTag", OwnerId = userId } : null;
        _sut.RequiredAssetAmount = requiredAsset;

        // Assert
        Assert.Equal(expected, _sut.CanSubmit);
    }

    [Fact]
    public async Task SearchMyTagsAsync_CallsDataProviderWithCurrentUserId()
    {
        // Arrange
        _sut.CurrentUserId = "user-123";
        var tags = new List<TagEntity> { new() { Id = 1, Name = "TagA", OwnerId = "user-123" } };
        _contractDataMock.Setup(d => d.SearchMyTagsAsync("user-123", "Tag", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tags);

        // Act
        var result = await _sut.SearchMyTagsAsync("Tag");

        // Assert
        Assert.Single(result);
        _contractDataMock.Verify(d => d.SearchMyTagsAsync("user-123", "Tag", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SubmitAsync_WhenTagNotSelected_ReturnsFailure()
    {
        // Arrange
        _sut.CurrentUserId = "user-123";
        _sut.SelectedTag = null;

        // Act
        var result = await _sut.SubmitAsync();

        // Assert
        Assert.True(result is Failure);
        if (result is Failure failure)
        {
            Assert.Contains("タグを選択してください", failure.ErrorMessage);
        }

        _createHandlerMock.Verify(h => h.HandleAsync(It.IsAny<CreatePublicOfferCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SubmitAsync_WhenUserIdEmpty_ReturnsFailure()
    {
        // Arrange
        _sut.CurrentUserId = "";
        _sut.SelectedTag = new TagEntity { Id = 1, Name = "Tag", OwnerId = "user-1" };

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
    public async Task SubmitAsync_WhenNegativeRequiredAsset_ReturnsFailure()
    {
        // Arrange
        _sut.CurrentUserId = "user-123";
        _sut.SelectedTag = new TagEntity { Id = 1, Name = "Tag", OwnerId = "user-123" };
        _sut.RequiredAssetAmount = -5;

        // Act
        var result = await _sut.SubmitAsync();

        // Assert
        Assert.True(result is Failure);
        if (result is Failure failure)
        {
            Assert.Contains("0以上を指定してください", failure.ErrorMessage);
        }
    }

    [Fact]
    public async Task SubmitAsync_WhenNotTagOwner_ReturnsFailure()
    {
        // Arrange
        _sut.CurrentUserId = "attacker";
        _sut.SelectedTag = new TagEntity { Id = 1, Name = "Tag", OwnerId = "real-owner" };
        _sut.RequiredAssetAmount = 5;

        // Act
        var result = await _sut.SubmitAsync();

        // Assert
        Assert.True(result is Failure);
        if (result is Failure failure)
        {
            Assert.Contains("自身が所有していないタグ", failure.ErrorMessage);
        }
    }

    [Fact]
    public async Task SubmitAsync_WhenValid_DispatchesCommandSuccessfully()
    {
        // Arrange
        _sut.CurrentUserId = "user-123";
        _sut.SelectedTag = new TagEntity { Id = 42, Name = "MyTag", OwnerId = "user-123" };
        _sut.RequiredAssetAmount = 100;

        CreatePublicOfferCommand? capturedCommand = null;
        _createHandlerMock.Setup(h => h.HandleAsync(It.IsAny<CreatePublicOfferCommand>(), It.IsAny<CancellationToken>()))
            .Callback<CreatePublicOfferCommand, CancellationToken>((cmd, _) => capturedCommand = cmd)
            .ReturnsAsync(Result.Ok(true));

        // Act
        var result = await _sut.SubmitAsync();

        // Assert
        Assert.True(result is Success<bool>);
        Assert.NotNull(capturedCommand);
        Assert.Equal("user-123", capturedCommand.Offer.OwnerId);
        Assert.Equal(42, capturedCommand.Offer.OfferedTagId);
        Assert.Equal(100, capturedCommand.Offer.RequiredAssetAmount);
        Assert.True(capturedCommand.Offer.IsActive);
    }

    [Fact]
    public async Task SubmitAsync_WhenHandlerThrows_ReturnsFailure()
    {
        // Arrange
        _sut.CurrentUserId = "user-123";
        _sut.SelectedTag = new TagEntity { Id = 42, Name = "MyTag", OwnerId = "user-123" };
        _createHandlerMock.Setup(h => h.HandleAsync(It.IsAny<CreatePublicOfferCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DB error"));

        // Act
        var result = await _sut.SubmitAsync();

        // Assert
        Assert.True(result is Failure);
        if (result is Failure failure)
        {
            Assert.Contains("DB error", failure.ErrorMessage);
        }
    }
}