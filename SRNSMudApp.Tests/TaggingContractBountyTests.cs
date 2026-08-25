using Microsoft.EntityFrameworkCore;
using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;
using SRNSMudApp.Tests.TestSupport;

namespace SRNSMudApp.Tests;

public class TaggingContractBountyTests : TaggingContractTestBase
{
    [Fact]
    public async Task ScenarioG_Bounty_Goodwill_ShouldConsumeFulfillerAsset()
    {
        // Arrange
        await using var scope = CreateTestScope();
        var (dbContext, service, tid) = scope;

        var userA = $"ua_{tid}";
        var userB = $"ub_{tid}";
        var userC = $"uc_{tid}";
        await dbContext.SeedUsersAsync(userA, userB, userC);

        var targetItemA = new Item { Content = $"My Item_{tid}", OwnerId = userA };
        var tagB = new Tag { Name = $"Expert_{tid}", OwnerId = userB, CachedWeight = 100 };

        dbContext.Items!.Add(targetItemA);
        dbContext.Tags!.Add(tagB);
        await dbContext.SaveChangesAsync();

        var fulfillerAsset = new RightAsset { Amount = 1, OwnerId = userC, TargetTagId = tagB.Id };
        dbContext.RightAssets!.Add(fulfillerAsset);
        await dbContext.SaveChangesAsync();

        var contract = new TaggingRequestEntity
        {
            ContractType = "Bounty",
            RequesterUserId = userA,
            TagOwnerUserId = userB,
            TargetItemId = targetItemA.Id,
            RequestedTagId = tagB.Id,
            Status = TradeStatus.Proposed,
            OwnerId = userA
        };
        dbContext.TaggingRequestEntities!.Add(contract);
        await dbContext.SaveChangesAsync();

        // Act
        var acceptResult = await service.AcceptContractAsync(contract.Id, userC, fulfillerAsset.Id);
        Assert.True(acceptResult is Success<string>, acceptResult switch { Failure f => f.ErrorMessage, _ => "Expected Success" });

        // Assert
        var assetIsBurned = await dbContext.RightAssets.AnyAsync(a => a.Id == fulfillerAsset.Id && a.IsBurned);
        Assert.True(assetIsBurned);

        TagRelation? relation =
            await dbContext.TagRelations!.FirstOrDefaultAsync(tr =>
                tr.ItemId == targetItemA.Id && tr.TagId == tagB.Id);
        Assert.NotNull(relation);
        Assert.Equal(userA, relation.OwnerId);

        TagWeightLedger? ledger =
            await dbContext.TagWeightLedgers!.FirstOrDefaultAsync(l => l.SourceId == relation.Id);
        Assert.NotNull(ledger);
        Assert.Equal(userC, ledger.OwnerId);
        Assert.False(ledger.IsOwnerAction);
        Assert.Equal("Goodwill Bounty Fulfilled", ledger.Reason);
    }

    [Fact]
    public async Task ScenarioH_Bounty_ReverseMutual_ShouldTransferRewardAndConsumeAsset()
    {
        // Arrange
        await using var scope = CreateTestScope();
        var (dbContext, service, tid) = scope;

        var userA = $"ua_{tid}";
        var userB = $"ub_{tid}";
        var userC = $"uc_{tid}";
        await dbContext.SeedUsersAsync(userA, userB, userC);

        var targetItemA = new Item { Content = $"My Item_{tid}", OwnerId = userA };
        var tagB = new Tag { Name = $"Expert_{tid}", OwnerId = userB };
        var tagC = new Tag { Name = $"RewardTag_{tid}", OwnerId = userA };

        dbContext.Items!.Add(targetItemA);
        dbContext.Tags!.AddRange(tagB, tagC);
        await dbContext.SaveChangesAsync();

        var rewardAsset = new RightAsset { Amount = 1, OwnerId = userA, TargetTagId = tagC.Id };
        var fulfillerAsset = new RightAsset { Amount = 1, OwnerId = userC, TargetTagId = tagB.Id };

        dbContext.RightAssets!.AddRange(rewardAsset, fulfillerAsset);
        await dbContext.SaveChangesAsync();

        var contract = new TaggingRequestEntity
        {
            ContractType = "Bounty",
            RequesterUserId = userA,
            TagOwnerUserId = userB,
            TargetItemId = targetItemA.Id,
            RequestedTagId = tagB.Id,
            Payload = new BountyPayload(rewardAsset.Id),
            Status = TradeStatus.Proposed,
            OwnerId = userA
        };
        dbContext.TaggingRequestEntities!.Add(contract);
        await dbContext.SaveChangesAsync();

        // Act
        var acceptResult = await service.AcceptContractAsync(contract.Id, userC, fulfillerAsset.Id);
        Assert.True(acceptResult is Success<string>, acceptResult switch { Failure f => f.ErrorMessage, _ => "Expected Success" });

        // Assert
        var fulfillerAssetIsBurned =
            await dbContext.RightAssets.AnyAsync(a => a.Id == fulfillerAsset.Id && a.IsBurned);
        Assert.True(fulfillerAssetIsBurned);

        RightAsset? updatedRewardAsset = await dbContext.RightAssets.FirstOrDefaultAsync(a => a.Id == rewardAsset.Id);
        Assert.NotNull(updatedRewardAsset);
        Assert.Equal(userC, updatedRewardAsset.OwnerId);

        TagRelation? relation =
            await dbContext.TagRelations!.FirstOrDefaultAsync(tr =>
                tr.ItemId == targetItemA.Id && tr.TagId == tagB.Id);
        Assert.NotNull(relation);

        TagWeightLedger? ledger =
            await dbContext.TagWeightLedgers!.FirstOrDefaultAsync(l => l.SourceId == relation.Id);
        Assert.NotNull(ledger);
        Assert.Equal("Reward Bounty Fulfilled", ledger.Reason);
    }
}
