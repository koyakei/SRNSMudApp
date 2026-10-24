#region

using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services.Contracts;

#endregion

namespace SRNSMudApp.Tests.Services.Contracts;

/// <summary>
///     <see cref="MutualContractExecutor" /> のドメインバリデーションロジックに対する高速な分離単体テスト。
///     DB 接続や Testcontainers を介さずに、引数検証および RightAsset の所有・有効期限・タグ一致・数量検証をテストする。
/// </summary>
public sealed class MutualContractExecutorUnitTests
{
    private readonly ApplicationDbContext _dummyContext = new(new DbContextOptions<ApplicationDbContext>());
    private readonly MutualContractExecutor _executor = new();

    [Fact]
    public void ContractType_ReturnsMutual()
    {
        // Assert
        Assert.Equal(ContractTypes.Mutual, _executor.ContractType);
    }

    [Fact]
    public async Task ExecuteAsync_WhenDbContextIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var contract = new TaggingRequestEntity { OwnerId = "userA" };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _executor.ExecuteAsync(null!, contract, "user1"));
    }

    [Fact]
    public async Task ExecuteAsync_WhenContractIsNull_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _executor.ExecuteAsync(_dummyContext, null!, "user1"));
    }

    [Fact]
    public async Task ExecuteAsync_WhenConsumedRightAssetIsNull_ReturnsFailure()
    {
        // Arrange
        var contract = new TaggingRequestEntity
        {
            OwnerId = "userA",
            ContractType = ContractTypes.Mutual,
            RequesterUserId = "userA",
            TagOwnerUserId = "userB",
            RequestedTagId = 10,
            RequestType = TaggingRequestType.Add,
            ProposedWeight = 1,
            ConsumedRightAsset = null
        };

        // Act
        Result<string> result = await _executor.ExecuteAsync(_dummyContext, contract, "userB");

        // Assert
        Assert.True(result is Failure failure && failure.ErrorMessage == "相互タグ付けには対価のアセットが必要です。");
    }

    [Fact]
    public async Task ExecuteAsync_WhenAssetOwnerDoesNotMatchRequester_ReturnsFailure()
    {
        // Arrange
        var contract = new TaggingRequestEntity
        {
            OwnerId = "userA",
            ContractType = ContractTypes.Mutual,
            RequesterUserId = "userA",
            TagOwnerUserId = "userB",
            RequestedTagId = 10,
            RequestType = TaggingRequestType.Add,
            ProposedWeight = 1,
            ConsumedRightAsset = new RightAsset
            {
                Id = 101,
                OwnerId = "otherUser",
                TargetTagId = 10,
                Amount = 1,
                IsBurned = false
            }
        };

        // Act
        Result<string> result = await _executor.ExecuteAsync(_dummyContext, contract, "userB");

        // Assert
        Assert.True(result is Failure failure && failure.ErrorMessage == "指定された RightAsset を所有していません。");
    }

    [Fact]
    public async Task ExecuteAsync_WhenAssetIsBurned_ReturnsFailure()
    {
        // Arrange
        var contract = new TaggingRequestEntity
        {
            OwnerId = "userA",
            ContractType = ContractTypes.Mutual,
            RequesterUserId = "userA",
            TagOwnerUserId = "userB",
            RequestedTagId = 10,
            RequestType = TaggingRequestType.Add,
            ProposedWeight = 1,
            ConsumedRightAsset = new RightAsset
            {
                Id = 101,
                OwnerId = "userA",
                TargetTagId = 10,
                Amount = 1,
                IsBurned = true
            }
        };

        // Act
        Result<string> result = await _executor.ExecuteAsync(_dummyContext, contract, "userB");

        // Assert
        Assert.True(result is Failure failure && failure.ErrorMessage == "指定された RightAsset は既に消費済みです。");
    }

    [Fact]
    public async Task ExecuteAsync_WhenAssetTargetTagDoesNotMatchRequestedTag_ReturnsFailure()
    {
        // Arrange
        var contract = new TaggingRequestEntity
        {
            OwnerId = "userA",
            ContractType = ContractTypes.Mutual,
            RequesterUserId = "userA",
            TagOwnerUserId = "userB",
            RequestedTagId = 10,
            RequestType = TaggingRequestType.Add,
            ProposedWeight = 1,
            ConsumedRightAsset = new RightAsset
            {
                Id = 101,
                OwnerId = "userA",
                TargetTagId = 999, // 不一致
                Amount = 5,
                IsBurned = false
            }
        };

        // Act
        Result<string> result = await _executor.ExecuteAsync(_dummyContext, contract, "userB");

        // Assert
        Assert.True(result is Failure failure && failure.ErrorMessage == "指定された RightAsset は対象タグの権利ではありません。");
    }

    [Fact]
    public async Task ExecuteAsync_WhenAssetAmountIsLessThanRequiredForAdd_ReturnsFailure()
    {
        // Arrange: 付与時は Math.Max(1, ProposedWeight) が要求される
        var contract = new TaggingRequestEntity
        {
            OwnerId = "userA",
            ContractType = ContractTypes.Mutual,
            RequesterUserId = "userA",
            TagOwnerUserId = "userB",
            RequestedTagId = 10,
            RequestType = TaggingRequestType.Add,
            ProposedWeight = 5,
            ConsumedRightAsset = new RightAsset
            {
                Id = 101,
                OwnerId = "userA",
                TargetTagId = 10,
                Amount = 4, // 5 未満で残量不足
                IsBurned = false
            }
        };

        // Act
        Result<string> result = await _executor.ExecuteAsync(_dummyContext, contract, "userB");

        // Assert
        Assert.True(result is Failure failure && failure.ErrorMessage == "指定された RightAsset の残量が不足しています。");
    }

    [Fact]
    public async Task ExecuteAsync_WhenAssetAmountIsZeroForRemove_ReturnsFailure()
    {
        // Arrange: 解除時は最低 1 が要求される
        var contract = new TaggingRequestEntity
        {
            OwnerId = "userA",
            ContractType = ContractTypes.Mutual,
            RequesterUserId = "userA",
            TagOwnerUserId = "userB",
            RequestedTagId = 10,
            RequestType = TaggingRequestType.Remove,
            ProposedWeight = 0,
            ConsumedRightAsset = new RightAsset
            {
                Id = 101,
                OwnerId = "userA",
                TargetTagId = 10,
                Amount = 0, // 1 未満で残量不足
                IsBurned = false
            }
        };

        // Act
        Result<string> result = await _executor.ExecuteAsync(_dummyContext, contract, "userB");

        // Assert
        Assert.True(result is Failure failure && failure.ErrorMessage == "指定された RightAsset の残量が不足しています。");
    }
}