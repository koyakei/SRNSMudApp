#region

using Moq;

using SRNSMudApp.Components.Bounty;
using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;

using ItemEntity = SRNSMudApp.Data.Item;
using TagEntity = SRNSMudApp.Data.Tag;

#endregion

namespace SRNSMudApp.Tests.Components.Bounty;

public class FulfillBountyViewModelTests
{
    private readonly Mock<IContractLookupDataProvider> _contractDataMock = new();
    private readonly Mock<ITaggingContractService> _contractServiceMock = new();
    private readonly FulfillBountyViewModel _sut;

    public FulfillBountyViewModelTests()
    {
        _sut = new FulfillBountyViewModel(
            _contractDataMock.Object,
            _contractServiceMock.Object);
    }

    [Fact]
    public void Constructor_NullArguments_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new FulfillBountyViewModel(null!, _contractServiceMock.Object));
        Assert.Throws<ArgumentNullException>(() => new FulfillBountyViewModel(_contractDataMock.Object, null!));
    }

    [Fact]
    public void InitialState_IsConfiguredProperly()
    {
        Assert.Null(_sut.Bounty);
        Assert.Empty(_sut.CurrentUserId);
        Assert.True(_sut.IsLoading);
        Assert.False(_sut.IsSubmitting);
        Assert.False(_sut.CanFulfillAsOwner);
        Assert.Empty(_sut.MyValidAssets);
        Assert.Null(_sut.SelectedAsset);
        Assert.Null(_sut.OfferedRewardAsset);
        Assert.False(_sut.CanSubmit);
    }

    [Fact]
    public async Task InitializeAsync_OwnerOfTag_CanFulfillAsOwnerWithoutAssets()
    {
        // Arrange
        var tag = new TagEntity { Id = 10, Name = "TagName", OwnerId = "user-owner" };
        var item = new ItemEntity { Id = 20, Content = "Content", OwnerId = "other" };
        var bounty = new TaggingRequestEntity
        {
            Id = 1,
            ContractType = "Bounty",
            RequestedTagId = 10,
            RequestedTag = tag,
            TargetItemId = 20,
            TargetItem = item,
            OwnerId = "requester",
            RequesterUserId = "requester",
            TagOwnerUserId = "user-owner",
            Payload = new BountyPayload(0)
        };

        // Act
        await _sut.InitializeAsync(bounty, "user-owner");

        // Assert
        Assert.Same(bounty, _sut.Bounty);
        Assert.Equal("user-owner", _sut.CurrentUserId);
        Assert.True(_sut.CanFulfillAsOwner);
        Assert.False(_sut.IsLoading);
        Assert.True(_sut.CanSubmit);
    }

    [Fact]
    public async Task InitializeAsync_NonOwner_WithOneAsset_AutoSelectsAsset()
    {
        // Arrange
        var tag = new TagEntity { Id = 10, Name = "TagName", OwnerId = "other-owner" };
        var item = new ItemEntity { Id = 20, Content = "Content", OwnerId = "requester" };
        var bounty = new TaggingRequestEntity
        {
            Id = 1,
            ContractType = "Bounty",
            RequestedTagId = 10,
            RequestedTag = tag,
            TargetItemId = 20,
            TargetItem = item,
            OwnerId = "requester",
            RequesterUserId = "requester",
            TagOwnerUserId = "other-owner",
            Payload = new BountyPayload(0)
        };

        var asset = new RightAsset { Id = 99, OwnerId = "user-fulfiller", Amount = 3, TargetTagId = 10 };
        _contractDataMock.Setup(d => d.GetValidRightAssetsAsync("user-fulfiller", 10))
            .ReturnsAsync([asset]);

        // Act
        await _sut.InitializeAsync(bounty, "user-fulfiller");

        // Assert
        Assert.False(_sut.CanFulfillAsOwner);
        Assert.Single(_sut.MyValidAssets);
        Assert.Same(asset, _sut.SelectedAsset);
        Assert.True(_sut.CanSubmit);
    }

    [Fact]
    public async Task InitializeAsync_LoadsOfferedRewardAsset_WhenPayloadHasRewardId()
    {
        // Arrange
        var tag = new TagEntity { Id = 10, Name = "TagName", OwnerId = "owner" };
        var item = new ItemEntity { Id = 20, Content = "Content", OwnerId = "requester" };
        var bounty = new TaggingRequestEntity
        {
            Id = 1,
            ContractType = "Bounty",
            RequestedTagId = 10,
            RequestedTag = tag,
            TargetItemId = 20,
            TargetItem = item,
            OwnerId = "requester",
            RequesterUserId = "requester",
            TagOwnerUserId = "owner",
            Payload = new BountyPayload(88)
        };

        var reward = new RightAsset { Id = 88, OwnerId = "requester", Amount = 1, TargetTagId = 55 };
        _contractDataMock.Setup(d => d.GetRightAssetByIdAsync(88))
            .ReturnsAsync(reward);

        // Act
        await _sut.InitializeAsync(bounty, "owner");

        // Assert
        Assert.Same(reward, _sut.OfferedRewardAsset);
    }

    [Fact]
    public async Task SubmitAsync_WhenOwner_CallsAcceptContractWithNullAssetId()
    {
        // Arrange
        var tag = new TagEntity { Id = 10, Name = "TagName", OwnerId = "user-owner" };
        var item = new ItemEntity { Id = 20, Content = "Content", OwnerId = "other" };
        var bounty = new TaggingRequestEntity
        {
            Id = 42,
            ContractType = "Bounty",
            RequestedTagId = 10,
            RequestedTag = tag,
            TargetItemId = 20,
            TargetItem = item,
            OwnerId = "requester",
            RequesterUserId = "requester",
            TagOwnerUserId = "user-owner",
            Payload = new BountyPayload(0)
        };

        await _sut.InitializeAsync(bounty, "user-owner");

        _contractServiceMock.Setup(s => s.AcceptContractAsync(42, "user-owner", null))
            .ReturnsAsync(new Success<string>("Contract completed successfully"));

        // Act
        var result = await _sut.SubmitAsync();

        // Assert
        Assert.True(result is Success<string>);
        if (result is Success<string> success)
        {
            Assert.Contains("successfully", success.Value);
        }

        Assert.False(_sut.IsSubmitting);
        _contractServiceMock.Verify(s => s.AcceptContractAsync(42, "user-owner", null), Times.Once);
    }

    [Fact]
    public async Task SubmitAsync_WhenNonOwner_CallsAcceptContractWithSelectedAssetId()
    {
        // Arrange
        var tag = new TagEntity { Id = 10, Name = "TagName", OwnerId = "other-owner" };
        var item = new ItemEntity { Id = 20, Content = "Content", OwnerId = "requester" };
        var bounty = new TaggingRequestEntity
        {
            Id = 55,
            ContractType = "Bounty",
            RequestedTagId = 10,
            RequestedTag = tag,
            TargetItemId = 20,
            TargetItem = item,
            OwnerId = "requester",
            RequesterUserId = "requester",
            TagOwnerUserId = "other-owner",
            Payload = new BountyPayload(0)
        };

        var asset = new RightAsset { Id = 123, OwnerId = "user-fulfiller", Amount = 1, TargetTagId = 10 };
        _contractDataMock.Setup(d => d.GetValidRightAssetsAsync("user-fulfiller", 10))
            .ReturnsAsync([asset]);

        await _sut.InitializeAsync(bounty, "user-fulfiller");

        _contractServiceMock.Setup(s => s.AcceptContractAsync(55, "user-fulfiller", 123))
            .ReturnsAsync(new Success<string>("Bounty fulfilled"));

        // Act
        var result = await _sut.SubmitAsync();

        // Assert
        Assert.True(result is Success<string>);
        _contractServiceMock.Verify(s => s.AcceptContractAsync(55, "user-fulfiller", 123), Times.Once);
    }

    [Fact]
    public async Task SubmitAsync_WhenNonOwnerAndNoAssetSelected_ReturnsFailure()
    {
        // Arrange
        var tag = new TagEntity { Id = 10, Name = "TagName", OwnerId = "other-owner" };
        var item = new ItemEntity { Id = 20, Content = "Content", OwnerId = "requester" };
        var bounty = new TaggingRequestEntity
        {
            Id = 55,
            ContractType = "Bounty",
            RequestedTagId = 10,
            RequestedTag = tag,
            TargetItemId = 20,
            TargetItem = item,
            OwnerId = "requester",
            RequesterUserId = "requester",
            TagOwnerUserId = "other-owner",
            Payload = new BountyPayload(0)
        };

        _contractDataMock.Setup(d => d.GetValidRightAssetsAsync("user-fulfiller", 10))
            .ReturnsAsync([]);

        await _sut.InitializeAsync(bounty, "user-fulfiller");
        _sut.SelectedAsset = null;

        // Act
        var result = await _sut.SubmitAsync();

        // Assert
        Assert.True(result is Failure);
        if (result is Failure failure)
        {
            Assert.Contains("消費するアセットを選択してください", failure.ErrorMessage);
        }
    }

    [Fact]
    public async Task SubmitAsync_WhenAlreadySubmitting_ReturnsFailureWithDuplicateSubmissionMessage()
    {
        // Arrange
        var tag = new TagEntity { Id = 10, Name = "TagName", OwnerId = "user-owner" };
        var item = new ItemEntity { Id = 20, Content = "Content", OwnerId = "other" };
        var bounty = new TaggingRequestEntity
        {
            Id = 42,
            ContractType = "Bounty",
            RequestedTagId = 10,
            RequestedTag = tag,
            TargetItemId = 20,
            TargetItem = item,
            OwnerId = "requester",
            RequesterUserId = "requester",
            TagOwnerUserId = "user-owner",
            Payload = new BountyPayload(0)
        };

        await _sut.InitializeAsync(bounty, "user-owner");

        var tcs = new TaskCompletionSource<Result<string>>();
        _contractServiceMock.Setup(s => s.AcceptContractAsync(42, "user-owner", null))
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

        tcs.SetResult(new Success<string>("Contract completed"));
        var firstResult = await firstTask;
        Assert.True(firstResult is Success<string>);
        Assert.False(_sut.IsSubmitting);

        _contractServiceMock.Verify(s => s.AcceptContractAsync(42, "user-owner", null), Times.Once);
    }
}