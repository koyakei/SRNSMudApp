using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;
using SRNSMudApp.Tests.TestSupport;

namespace SRNSMudApp.Tests;

public class TaggingContractPublicOfferTests : TaggingContractTestBase
{
    [Fact]
    public async Task ScenarioD_PublicOffer_ExactAsset_ShouldTriggerAndConsume()
    {
        // Arrange
        await using var scope = CreateTestScope();
        var (dbContext, service, tid) = scope;

        var userA = $"ua_{tid}";
        var userB = $"ub_{tid}";
        await dbContext.SeedUsersAsync(userA, userB);

        var targetItemA = new Item { Content = $"TargetItemA_{tid}", OwnerId = userA };
        var tagB = new Tag { Name = $"TagB_{tid}", OwnerId = userB, CachedWeight = 100 };

        dbContext.Items!.Add(targetItemA);
        dbContext.Tags!.Add(tagB);
        await dbContext.SaveChangesAsync();

        var rightAsset = new RightAsset { Amount = 10, OwnerId = userA, TargetTagId = tagB.Id };
        dbContext.RightAssets!.Add(rightAsset);
        await dbContext.SaveChangesAsync();

        var publicOffer = new PublicTradeOffer
        {
            OwnerId = userB,
            OfferedTagId = tagB.Id,
            RequiredAssetAmount = 10,
            IsActive = true
        };
        dbContext.PublicTradeOffers!.Add(publicOffer);
        await dbContext.SaveChangesAsync();

        var contract = new TaggingRequestEntity
        {
            ContractType = "Trigger",
            RequesterUserId = userA,
            TagOwnerUserId = userB,
            TargetItemId = targetItemA.Id,
            RequestedTagId = tagB.Id,
            ConsumedRightAssetId = rightAsset.Id,
            Payload = new PublicOfferPayload(publicOffer.Id),
            Status = TradeStatus.Proposed,
            OwnerId = userA
        };
        dbContext.TaggingRequestEntities!.Add(contract);
        await dbContext.SaveChangesAsync();

        // Act
        var acceptResult = await service.AcceptContractAsync(contract.Id, userA);
        Assert.True(acceptResult is Success<string>, acceptResult switch { Failure f => f.ErrorMessage, _ => "Expected Success" });

        // Assert
        var assetIsBurned = await dbContext.RightAssets.AnyAsync(a => a.Id == rightAsset.Id && a.IsBurned);
        Assert.True(assetIsBurned, "要求量ピッタリのアセットは消費されるべき");

        TagRelation? relation = await dbContext.TagRelations!.FirstOrDefaultAsync(tr => tr.ItemId == targetItemA.Id && tr.TagId == tagB.Id);
        Assert.NotNull(relation);

        TagWeightLedger? ledger = await dbContext.TagWeightLedgers!.FirstOrDefaultAsync(l => l.SourceId == relation.Id);
        Assert.NotNull(ledger);
        Assert.False(ledger.IsOwnerAction);
        Assert.Equal("Public Offer Triggered", ledger.Reason);
    }

    [Fact]
    public async Task ScenarioF_PublicOffer_FreeCampaign_ShouldMintAssetByOwner()
    {
        // Arrange
        await using var scope = CreateTestScope();
        var (dbContext, service, tid) = scope;

        var userA = $"ua_{tid}";
        var userB = $"ub_{tid}";
        await dbContext.SeedUsersAsync(userA, userB);

        var targetItemA = new Item { Content = $"TargetItemA_{tid}", OwnerId = userA };
        var tagB = new Tag { Name = $"TagB_{tid}", OwnerId = userB, CachedWeight = 100 };

        dbContext.Items!.Add(targetItemA);
        dbContext.Tags!.Add(tagB);
        await dbContext.SaveChangesAsync();

        var publicOffer = new PublicTradeOffer
        {
            OwnerId = userB,
            OfferedTagId = tagB.Id,
            RequiredAssetAmount = 0,
            IsActive = true
        };
        dbContext.PublicTradeOffers!.Add(publicOffer);
        await dbContext.SaveChangesAsync();

        var contract = new TaggingRequestEntity
        {
            ContractType = "Trigger",
            RequesterUserId = userA,
            TagOwnerUserId = userB,
            TargetItemId = targetItemA.Id,
            RequestedTagId = tagB.Id,
            ConsumedRightAssetId = null,
            Payload = new PublicOfferPayload(publicOffer.Id),
            Status = TradeStatus.Proposed,
            OwnerId = userA
        };
        dbContext.TaggingRequestEntities!.Add(contract);
        await dbContext.SaveChangesAsync();

        // Act
        var acceptResult = await service.AcceptContractAsync(contract.Id, userA);
        Assert.True(acceptResult is Success<string>, acceptResult switch { Failure f => f.ErrorMessage, _ => "Expected Success" });

        // Assert
        TagRelation? relation = await dbContext.TagRelations!.FirstOrDefaultAsync(tr => tr.ItemId == targetItemA.Id && tr.TagId == tagB.Id);
        Assert.NotNull(relation);

        TagWeightLedger? ledger = await dbContext.TagWeightLedgers!.FirstOrDefaultAsync(l => l.SourceId == relation.Id);
        Assert.NotNull(ledger);

        RightAsset? mintedAsset = await dbContext.RightAssets!.IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Id == ledger.ConsumedRightAssetId);
        Assert.NotNull(mintedAsset);
        Assert.True(mintedAsset.IsBurned);
        Assert.Equal(userB, mintedAsset.OwnerId);
    }

    [Fact]
    public async Task ScenarioE_PublicOffer_Error_InsufficientAsset()
    {
        // Arrange
        await using var scope = CreateTestScope();
        var (dbContext, service, tid) = scope;

        var userA = $"ua_{tid}";
        var userB = $"ub_{tid}";
        await dbContext.SeedUsersAsync(userA, userB);

        var targetItemA = new Item { Content = $"TargetItemA_{tid}", OwnerId = userA };
        var tagB = new Tag { Name = $"TagB_{tid}", OwnerId = userB };

        dbContext.Items!.Add(targetItemA);
        dbContext.Tags!.Add(tagB);
        await dbContext.SaveChangesAsync();

        var rightAsset = new RightAsset { Amount = 5, OwnerId = userA, TargetTagId = tagB.Id };
        dbContext.RightAssets!.Add(rightAsset);

        var publicOffer = new PublicTradeOffer
        {
            OwnerId = userB,
            OfferedTagId = tagB.Id,
            RequiredAssetAmount = 10,
            IsActive = true
        };
        dbContext.PublicTradeOffers!.Add(publicOffer);
        await dbContext.SaveChangesAsync();

        var contract = new TaggingRequestEntity
        {
            ContractType = "Trigger",
            RequesterUserId = userA,
            TagOwnerUserId = userB,
            TargetItemId = targetItemA.Id,
            RequestedTagId = tagB.Id,
            ConsumedRightAssetId = rightAsset.Id,
            Payload = new PublicOfferPayload(publicOffer.Id),
            Status = TradeStatus.Proposed,
            OwnerId = userA
        };
        dbContext.TaggingRequestEntities!.Add(contract);
        await dbContext.SaveChangesAsync();

        // Act & Assert
        var res = await service.AcceptContractAsync(contract.Id, userA);
        Assert.True(res is Failure);
        var ex = res switch { Failure f => f, _ => throw new InvalidOperationException("Expected Failure") };
        Assert.Equal("提供された RightAsset の量が不足しています。", ex.ErrorMessage);
    }

    [Fact]
    public async Task ScenarioE_PublicOffer_Error_WrongTagAsset()
    {
        // Arrange
        await using var scope = CreateTestScope();
        var (dbContext, service, tid) = scope;

        var userA = $"ua_{tid}";
        var userB = $"ub_{tid}";
        await dbContext.SeedUsersAsync(userA, userB);

        var targetItemA = new Item { Content = $"TargetItemA_{tid}", OwnerId = userA };
        var tagB = new Tag { Name = $"TagB_{tid}", OwnerId = userB };
        var tagC = new Tag { Name = $"TagC_{tid}", OwnerId = userA };

        dbContext.Items!.Add(targetItemA);
        dbContext.Tags!.AddRange(tagB, tagC);
        await dbContext.SaveChangesAsync();

        var rightAsset = new RightAsset { Amount = 10, OwnerId = userA, TargetTagId = tagC.Id };
        dbContext.RightAssets!.Add(rightAsset);

        var publicOffer = new PublicTradeOffer
        {
            OwnerId = userB,
            OfferedTagId = tagB.Id,
            RequiredAssetAmount = 10,
            IsActive = true
        };
        dbContext.PublicTradeOffers!.Add(publicOffer);
        await dbContext.SaveChangesAsync();

        var contract = new TaggingRequestEntity
        {
            ContractType = "Trigger",
            RequesterUserId = userA,
            TagOwnerUserId = userB,
            TargetItemId = targetItemA.Id,
            RequestedTagId = tagB.Id,
            ConsumedRightAssetId = rightAsset.Id,
            Payload = new PublicOfferPayload(publicOffer.Id),
            Status = TradeStatus.Proposed,
            OwnerId = userA
        };
        dbContext.TaggingRequestEntities!.Add(contract);
        await dbContext.SaveChangesAsync();

        // Act & Assert
        var res = await service.AcceptContractAsync(contract.Id, userA);
        Assert.True(res is Failure);
        var ex = res switch { Failure f => f, _ => throw new InvalidOperationException("Expected Failure") };
        Assert.Equal("提供された RightAsset は対象のタグの権利ではありません。", ex.ErrorMessage);
    }

    [Fact]
    public async Task ScenarioE_PublicOffer_Error_InactiveOffer()
    {
        // Arrange
        await using var scope = CreateTestScope();
        var (dbContext, service, tid) = scope;

        var userA = $"ua_{tid}";
        var userB = $"ub_{tid}";
        await dbContext.SeedUsersAsync(userA, userB);

        var targetItemA = new Item { Content = $"TargetItemA_{tid}", OwnerId = userA };
        var tagB = new Tag { Name = $"TagB_{tid}", OwnerId = userB };

        dbContext.Items!.Add(targetItemA);
        dbContext.Tags!.Add(tagB);
        await dbContext.SaveChangesAsync();

        var rightAsset = new RightAsset { Amount = 10, OwnerId = userA, TargetTagId = tagB.Id };
        dbContext.RightAssets!.Add(rightAsset);

        var publicOffer = new PublicTradeOffer
        {
            OwnerId = userB,
            OfferedTagId = tagB.Id,
            RequiredAssetAmount = 10,
            IsActive = false
        };
        dbContext.PublicTradeOffers!.Add(publicOffer);
        await dbContext.SaveChangesAsync();

        var contract = new TaggingRequestEntity
        {
            ContractType = "Trigger",
            RequesterUserId = userA,
            TagOwnerUserId = userB,
            TargetItemId = targetItemA.Id,
            RequestedTagId = tagB.Id,
            ConsumedRightAssetId = rightAsset.Id,
            Payload = new PublicOfferPayload(publicOffer.Id),
            Status = TradeStatus.Proposed,
            OwnerId = userA
        };
        dbContext.TaggingRequestEntities!.Add(contract);
        await dbContext.SaveChangesAsync();

        // Act & Assert
        var res = await service.AcceptContractAsync(contract.Id, userA);
        Assert.True(res is Failure);
        var ex = res switch { Failure f => f, _ => throw new InvalidOperationException("Expected Failure") };
        Assert.Equal("この公開オファーは現在有効ではありません。", ex.ErrorMessage);
    }
}