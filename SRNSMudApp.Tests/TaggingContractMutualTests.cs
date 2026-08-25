using Microsoft.EntityFrameworkCore;
using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;
using SRNSMudApp.Tests.TestSupport;

namespace SRNSMudApp.Tests;

public class TaggingContractMutualTests : TaggingContractTestBase
{
    [Fact]
    public async Task ScenarioC_MutualTagging_ShouldSwapTagsAndMintComplementaryAsset()
    {
        // Arrange
        await using var scope = CreateTestScope();
        var (dbContext, service, tid) = scope;

        var userA = $"ua_{tid}";
        var userB = $"ub_{tid}";
        await dbContext.SeedUsersAsync(userA, userB);

        var targetItemA = new Item { Content = $"TargetItemA_{tid}", OwnerId = userA };
        var targetItemB = new Item { Content = $"TargetItemB_{tid}", OwnerId = userB };
        var tagA = new Tag { Name = $"TagA_{tid}", OwnerId = userA, CachedWeight = 50 };
        var tagB = new Tag { Name = $"TagB_{tid}", OwnerId = userB, CachedWeight = 100 };

        dbContext.Items!.AddRange(targetItemA, targetItemB);
        dbContext.Tags!.AddRange(tagA, tagB);
        await dbContext.SaveChangesAsync();

        var rightAssetA = new RightAsset { Amount = 1, OwnerId = userA, TargetTagId = tagB.Id };
        dbContext.RightAssets!.Add(rightAssetA);
        await dbContext.SaveChangesAsync();

        var contract = new TaggingRequestEntity
        {
            ContractType = "Mutual",
            RequesterUserId = userA,
            TagOwnerUserId = userB,
            TargetItemId = targetItemA.Id,
            RequestedTagId = tagB.Id,
            Payload = new MutualPayload(targetItemB.Id, tagA.Id),
            ConsumedRightAssetId = rightAssetA.Id,
            Status = TradeStatus.Proposed,
            OwnerId = userA
        };
        dbContext.TaggingRequestEntities!.Add(contract);
        await dbContext.SaveChangesAsync();

        // Act
        var acceptResult = await service.AcceptContractAsync(contract.Id, userB);
        Assert.True(acceptResult is Success<string>, acceptResult switch { Failure f => f.ErrorMessage, _ => "Expected Success" });

        // Assert
        var assetIsBurned = await dbContext.RightAssets.AnyAsync(a => a.Id == rightAssetA.Id && a.IsBurned);
        Assert.True(assetIsBurned);

        TagRelation? relation1 =
            await dbContext.TagRelations!.FirstOrDefaultAsync(tr =>
                tr.ItemId == targetItemA.Id && tr.TagId == tagB.Id);
        TagRelation? relation2 =
            await dbContext.TagRelations!.FirstOrDefaultAsync(tr =>
                tr.ItemId == targetItemB.Id && tr.TagId == tagA.Id);
        Assert.NotNull(relation1);
        Assert.NotNull(relation2);

        TagWeightLedger? ledger1 =
            await dbContext.TagWeightLedgers!.FirstOrDefaultAsync(l => l.SourceId == relation1.Id);
        Assert.NotNull(ledger1);
        Assert.Equal(rightAssetA.Id, ledger1.ConsumedRightAssetId);
        Assert.True(ledger1.IsOwnerAction);

        TagWeightLedger? ledger2 =
            await dbContext.TagWeightLedgers!.FirstOrDefaultAsync(l => l.SourceId == relation2.Id);
        Assert.NotNull(ledger2);
        Assert.False(ledger2.IsOwnerAction);

        RightAsset? mintedAssetA = await dbContext.RightAssets!.IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Id == ledger2.ConsumedRightAssetId);
        Assert.NotNull(mintedAssetA);
        Assert.True(mintedAssetA.IsBurned);
        Assert.Equal(userA, mintedAssetA.OwnerId);
    }

    [Fact]
    public async Task ScenarioC_MutualTagging_Error_NoAssetProvided()
    {
        // Arrange
        await using var scope = CreateTestScope();
        var (dbContext, service, tid) = scope;

        var userA = $"ua_{tid}";
        var userB = $"ub_{tid}";
        await dbContext.SeedUsersAsync(userA, userB);

        var targetItemA = new Item { Content = $"TargetItemA_{tid}", OwnerId = userA };
        var targetItemB = new Item { Content = $"TargetItemB_{tid}", OwnerId = userB };
        var tagA = new Tag { Name = $"TagA_{tid}", OwnerId = userA, CachedWeight = 50 };
        var tagB = new Tag { Name = $"TagB_{tid}", OwnerId = userB, CachedWeight = 100 };

        dbContext.Items!.AddRange(targetItemA, targetItemB);
        dbContext.Tags!.AddRange(tagA, tagB);
        await dbContext.SaveChangesAsync();

        var contract = new TaggingRequestEntity
        {
            ContractType = "Mutual",
            RequesterUserId = userA,
            TagOwnerUserId = userB,
            TargetItemId = targetItemA.Id,
            RequestedTagId = tagB.Id,
            Payload = new MutualPayload(targetItemB.Id, tagA.Id),
            ConsumedRightAssetId = null,
            Status = TradeStatus.Proposed,
            OwnerId = userA
        };
        dbContext.TaggingRequestEntities!.Add(contract);
        await dbContext.SaveChangesAsync();

        // Act & Assert
        var res = await service.AcceptContractAsync(contract.Id, userB);
        Assert.True(res is Failure);
        var ex = res switch { Failure f => f, _ => throw new InvalidOperationException("Expected Failure") };
        Assert.Equal("相互タグ付けには対価のアセットが必要です。", ex.ErrorMessage);
    }
}
