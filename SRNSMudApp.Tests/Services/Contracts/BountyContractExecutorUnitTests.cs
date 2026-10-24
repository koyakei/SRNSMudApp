#region

using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services.Contracts;

#endregion

namespace SRNSMudApp.Tests.Services.Contracts;

/// <summary>
///     <see cref="BountyContractExecutor" /> のドメインバリデーションロジックに対する高速な分離単体テスト。
///     DB 接続や Testcontainers を介さずに、引数検証および承認者アセット前提条件をテストする。
/// </summary>
public sealed class BountyContractExecutorUnitTests
{
    private readonly ApplicationDbContext _dummyContext = new(new DbContextOptions<ApplicationDbContext>());
    private readonly BountyContractExecutor _executor = new();

    [Fact]
    public void ContractType_ReturnsBounty()
    {
        // Assert
        Assert.Equal(ContractTypes.Bounty, _executor.ContractType);
    }

    [Fact]
    public async Task ExecuteAsync_WhenDbContextIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var contract = new TaggingRequestEntity { OwnerId = "user1" };

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
    public async Task ExecuteAsync_WhenFulfillerAssetIdIsNullAndFulfillerIsNotTagOwner_ReturnsFailure()
    {
        // Arrange: アセット ID が指定されておらず、かつ承認者がタグオーナーでもない場合、アセット不足で即時 Failure
        var contract = new TaggingRequestEntity
        {
            OwnerId = "requesterUser",
            ContractType = ContractTypes.Bounty,
            RequesterUserId = "requesterUser",
            TagOwnerUserId = "tagOwnerUser",
            RequestedTag = new Tag { Id = 10, Name = "bounty_tag", OwnerId = "tagOwnerUser" },
            RequestedTagId = 10,
            RequestType = TaggingRequestType.Add,
            ProposedWeight = 1
        };

        // Act: 実行者は "thirdPartyUser" (非タグオーナー) かつ fulfillerAssetId = null
        Result<string> result = await _executor.ExecuteAsync(_dummyContext, contract, "thirdPartyUser", fulfillerAssetId: null);

        // Assert
        Assert.True(result is Failure failure && failure.ErrorMessage == "バウンティを承認するには対象タグの RightAsset が必要です。");
    }
}