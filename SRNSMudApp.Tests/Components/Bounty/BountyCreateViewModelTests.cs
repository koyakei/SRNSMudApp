#region

using Moq;

using SRNSMudApp.Components.Bounty;
using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;
using SRNSMudApp.Services.Commands;

using ItemEntity = SRNSMudApp.Data.Item;
using TagEntity = SRNSMudApp.Data.Tag;

#endregion

namespace SRNSMudApp.Tests.Components.Bounty;

public class BountyCreateViewModelTests
{
    private readonly Mock<IContractLookupDataProvider> _contractDataMock = new();
    private readonly Mock<ICommandHandler<CreateBountyCommand, Result<bool>>> _createBountyHandlerMock = new();
    private readonly BountyCreateViewModel _sut;

    public BountyCreateViewModelTests()
    {
        _sut = new BountyCreateViewModel(
            _contractDataMock.Object,
            _createBountyHandlerMock.Object);
    }

    [Fact]
    public void Constructor_NullArguments_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new BountyCreateViewModel(null!, _createBountyHandlerMock.Object));
        Assert.Throws<ArgumentNullException>(() => new BountyCreateViewModel(_contractDataMock.Object, null!));
    }

    [Fact]
    public void InitialState_IsConfiguredProperly()
    {
        Assert.Empty(_sut.CurrentUserId);
        Assert.Null(_sut.TargetItem);
        Assert.Null(_sut.SelectedTag);
        Assert.Null(_sut.SelectedRewardAsset);
        Assert.Empty(_sut.MyAssets);
        Assert.False(_sut.IsLoadingAssets);
        Assert.False(_sut.IsSubmitting);
        Assert.False(_sut.CanSubmit);
    }

    [Fact]
    public async Task InitializeAsync_ConfiguresPropertiesAndLoadsAssets()
    {
        // Arrange
        var item = new ItemEntity { Id = 10, Content = "Need tag for this", OwnerId = "user-1" };
        var assets = new List<RightAsset>
        {
            new() { Id = 1, OwnerId = "user-1", Amount = 5, TargetTagId = 100 }
        };
        _contractDataMock.Setup(d => d.GetAvailableRightAssetsAsync("user-1"))
            .ReturnsAsync(assets);

        // Act
        await _sut.InitializeAsync(item, "user-1");

        // Assert
        Assert.Same(item, _sut.TargetItem);
        Assert.Equal("user-1", _sut.CurrentUserId);
        Assert.Null(_sut.SelectedTag);
        Assert.Null(_sut.SelectedRewardAsset);
        Assert.Single(_sut.MyAssets);
        Assert.False(_sut.IsLoadingAssets);
        Assert.False(_sut.CanSubmit); // SelectedTag is still null
    }

    [Fact]
    public async Task SearchItemsAsync_CallsContractData()
    {
        var items = new List<ItemEntity>
        {
            new() { Id = 1, Content = "Found", OwnerId = "u" }
        };
        _contractDataMock.Setup(d => d.SearchItemsAsync("query", It.IsAny<CancellationToken>()))
            .ReturnsAsync(items);

        var result = await _sut.SearchItemsAsync("query");

        Assert.Single(result);
        _contractDataMock.Verify(d => d.SearchItemsAsync("query", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchTagsAsync_CallsContractData()
    {
        var tags = new List<TagEntity>
        {
            new() { Id = 1, Name = "FoundTag", OwnerId = "u" }
        };
        _contractDataMock.Setup(d => d.SearchTagsByNameAsync("tag", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tags);

        var result = await _sut.SearchTagsAsync("tag");

        Assert.Single(result);
        _contractDataMock.Verify(d => d.SearchTagsByNameAsync("tag", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(true, true, "user-1", true)]
    [InlineData(false, true, "user-1", false)] // No item
    [InlineData(true, false, "user-1", false)] // No tag
    [InlineData(true, true, "", false)] // No user
    public async Task CanSubmit_ValidatesConditions(bool hasItem, bool hasTag, string userId, bool expected)
    {
        _sut.TargetItem = hasItem ? new ItemEntity { Id = 1, Content = "Item", OwnerId = "u" } : null;
        _sut.SelectedTag = hasTag ? new TagEntity { Id = 2, Name = "Tag", OwnerId = "u" } : null;
        _contractDataMock.Setup(d => d.GetAvailableRightAssetsAsync(It.IsAny<string>()))
            .ReturnsAsync([]);

        await _sut.InitializeAsync(_sut.TargetItem, userId);
        _sut.SelectedTag = hasTag ? new TagEntity { Id = 2, Name = "Tag", OwnerId = "u" } : null;

        Assert.Equal(expected, _sut.CanSubmit);
    }

    [Fact]
    public async Task SubmitAsync_WhenUserIdEmpty_ReturnsFailure()
    {
        _sut.TargetItem = new ItemEntity { Id = 1, Content = "Item", OwnerId = "u" };
        _sut.SelectedTag = new TagEntity { Id = 2, Name = "Tag", OwnerId = "u" };

        var result = await _sut.SubmitAsync();

        Assert.True(result is Failure);
        if (result is Failure failure)
        {
            Assert.Contains("ユーザー情報が取得できませんでした", failure.ErrorMessage);
        }
    }

    [Fact]
    public async Task SubmitAsync_WhenTargetItemNull_ReturnsFailure()
    {
        await _sut.InitializeAsync(null, "user-1");
        _sut.SelectedTag = new TagEntity { Id = 2, Name = "Tag", OwnerId = "u" };

        var result = await _sut.SubmitAsync();

        Assert.True(result is Failure);
        if (result is Failure failure)
        {
            Assert.Contains("アイテムを選択してください", failure.ErrorMessage);
        }
    }

    [Fact]
    public async Task SubmitAsync_WhenSelectedTagNull_ReturnsFailure()
    {
        var item = new ItemEntity { Id = 1, Content = "Item", OwnerId = "u" };
        await _sut.InitializeAsync(item, "user-1");
        _sut.SelectedTag = null;

        var result = await _sut.SubmitAsync();

        Assert.True(result is Failure);
        if (result is Failure failure)
        {
            Assert.Contains("タグを選択してください", failure.ErrorMessage);
        }
    }

    [Fact]
    public async Task SubmitAsync_WhenValidWithoutReward_CallsCreateBountyHandler()
    {
        // Arrange
        var item = new ItemEntity { Id = 5, Content = "TargetItem", OwnerId = "requester" };
        var tag = new TagEntity { Id = 20, Name = "TargetTag", OwnerId = "tag-owner" };
        await _sut.InitializeAsync(item, "requester");
        _sut.SelectedTag = tag;
        _sut.SelectedRewardAsset = null;

        _createBountyHandlerMock.Setup(h => h.HandleAsync(
                It.Is<CreateBountyCommand>(c =>
                    c.Bounty.ContractType == "Bounty" &&
                    c.Bounty.OwnerId == "requester" &&
                    c.Bounty.RequesterUserId == "requester" &&
                    c.Bounty.TagOwnerUserId == "tag-owner" &&
                    c.Bounty.TargetItemId == 5 &&
                    c.Bounty.RequestedTagId == 20 &&
                    HasRewardAssetId(c.Bounty.Payload, 0)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Success<bool>(true));

        // Act
        var result = await _sut.SubmitAsync();

        // Assert
        Assert.True(result is Success<bool>);
        Assert.False(_sut.IsSubmitting);
        _createBountyHandlerMock.Verify(h => h.HandleAsync(It.IsAny<CreateBountyCommand>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SubmitAsync_WhenValidWithReward_CallsCreateBountyHandlerWithRewardAssetId()
    {
        // Arrange
        var item = new ItemEntity { Id = 5, Content = "TargetItem", OwnerId = "requester" };
        var tag = new TagEntity { Id = 20, Name = "TargetTag", OwnerId = "tag-owner" };
        var rewardAsset = new RightAsset { Id = 77, OwnerId = "requester", Amount = 1, TargetTagId = 99 };
        await _sut.InitializeAsync(item, "requester");
        _sut.SelectedTag = tag;
        _sut.SelectedRewardAsset = rewardAsset;

        _createBountyHandlerMock.Setup(h => h.HandleAsync(
                It.Is<CreateBountyCommand>(c =>
                    c.Bounty.TargetItemId == 5 &&
                    c.Bounty.RequestedTagId == 20 &&
                    HasRewardAssetId(c.Bounty.Payload, 77)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Success<bool>(true));

        // Act
        var result = await _sut.SubmitAsync();

        // Assert
        Assert.True(result is Success<bool>);
        _createBountyHandlerMock.Verify(h => h.HandleAsync(It.IsAny<CreateBountyCommand>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static bool HasRewardAssetId(ContractPayload payload, int expectedRewardAssetId)
    {
        return payload is BountyPayload bp && bp.OfferedRewardAssetId == expectedRewardAssetId;
    }

    [Fact]
    public async Task SubmitAsync_WhenAlreadySubmitting_ReturnsFailureWithDuplicateSubmissionMessage()
    {
        // Arrange
        var item = new ItemEntity { Id = 5, Content = "TargetItem", OwnerId = "requester" };
        var tag = new TagEntity { Id = 20, Name = "TargetTag", OwnerId = "tag-owner" };
        await _sut.InitializeAsync(item, "requester");
        _sut.SelectedTag = tag;

        var tcs = new TaskCompletionSource<Result<bool>>();
        _createBountyHandlerMock.Setup(h => h.HandleAsync(It.IsAny<CreateBountyCommand>(), It.IsAny<CancellationToken>()))
            .Returns(tcs.Task);

        // Act: 最初の送信タスクを開始
        var firstTask = _sut.SubmitAsync();
        Assert.True(_sut.IsSubmitting);

        // 二重送信
        var secondResult = await _sut.SubmitAsync();

        // Assert
        Assert.True(secondResult is Failure);
        if (secondResult is Failure failure)
        {
            Assert.Equal(SRNSMudApp.Client.Services.DoubleSubmissionGuard.DuplicateSubmissionMessage, failure.ErrorMessage);
        }

        tcs.SetResult(new Success<bool>(true));
        var firstResult = await firstTask;
        Assert.True(firstResult is Success<bool>);
        Assert.False(_sut.IsSubmitting);

        _createBountyHandlerMock.Verify(h => h.HandleAsync(It.IsAny<CreateBountyCommand>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}