using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;

namespace SRNSMudApp.Tests;

public class TaggingContractGratisTests : TaggingContractTestBase
{
    [Fact]
    public async Task ScenarioA_GratisWithTip_ShouldConsumeProvidedAssetAndTagItem()
    {
        // Arrange
        await using var scope = CreateTestScope();
        var (dbContext, service, tid) = scope;

        var userA = $"ua_{tid}";
        var userB = $"ub_{tid}";
        await dbContext.SeedUsersAsync(userA, userB);

        var targetItem = new Item { Content = $"My C# Project_{tid}", OwnerId = userA };
        var tag = new Tag { Name = $"C# Master_{tid}", OwnerId = userB, CachedWeight = 100 };
        var rightAsset = new RightAsset { Amount = 1, OwnerId = userA, TargetTagId = 1 }; // チップ

        dbContext.Items!.Add(targetItem);
        dbContext.Tags!.Add(tag);
        await dbContext.SaveChangesAsync();

        rightAsset.TargetTagId = tag.Id;
        dbContext.RightAssets!.Add(rightAsset);
        await dbContext.SaveChangesAsync();

        var contract = new TaggingRequestEntity
        {
            ContractType = "Gratis",
            RequesterUserId = userA,
            TagOwnerUserId = userB,
            TargetItemId = targetItem.Id,
            RequestedTagId = tag.Id,
            ConsumedRightAssetId = rightAsset.Id, // チップを添付
            Status = TradeStatus.Proposed,
            OwnerId = userA,
            Payload = new GratisPayload("Please tag my item. Here is a tip!")
        };
        dbContext.TaggingRequestEntities!.Add(contract);
        await dbContext.SaveChangesAsync();

        // Act
        // When: ユーザーBが承認を実行する
        var acceptResult = await service.AcceptContractAsync(contract.Id, userB);
        Assert.True(acceptResult is Success<string>, acceptResult switch { Failure f => f.ErrorMessage, _ => "Expected Success" });

        // Assert
        // Then: ユーザーAが提供した RightAsset が消費される
        var assetIsBurned = await dbContext.RightAssets.AnyAsync(a => a.Id == rightAsset.Id && a.IsBurned);
        Assert.True(assetIsBurned, "チップとして提供された RightAsset は消費されるべき");

        // Then: タグリレーションが作成される
        TagRelation? relation =
            await dbContext.TagRelations!.FirstOrDefaultAsync(tr => tr.ItemId == targetItem.Id && tr.TagId == tag.Id);
        Assert.NotNull(relation);
        Assert.Equal(userA, relation.OwnerId);

        // Then: 元帳に IsOwnerAction=true として記録され、提供したアセットIDが結びつく
        TagWeightLedger? ledger =
            await dbContext.TagWeightLedgers!.FirstOrDefaultAsync(l => l.SourceId == relation.Id);
        Assert.NotNull(ledger);
        Assert.Equal(rightAsset.Id, ledger.ConsumedRightAssetId);
        Assert.Equal(100, ledger.PreviousWeight);
        Assert.Equal(101, ledger.NewWeight);
        Assert.True(ledger.IsOwnerAction);
        Assert.Equal($"C# Master_{tid}", ledger.TagNameSnapshot);
        Assert.Equal("Gratis Tagging Contract Accepted", ledger.Reason);
        Assert.Equal(userB, ledger.OwnerId); // 承認したタグオーナーが実行者
    }

    [Fact]
    public async Task ScenarioB_GratisWithoutTip_ShouldMintAndBurnAssetByOwner()
    {
        // Arrange
        await using var scope = CreateTestScope();
        var (dbContext, service, tid) = scope;

        var userA = $"ua_{tid}";
        var userB = $"ub_{tid}";
        await dbContext.SeedUsersAsync(userA, userB);

        var targetItem = new Item { Content = $"Nice Project_{tid}", OwnerId = userA };
        var tag = new Tag { Name = $"Approved_{tid}", OwnerId = userB, CachedWeight = 100 };

        dbContext.Items!.Add(targetItem);
        dbContext.Tags!.Add(tag);
        await dbContext.SaveChangesAsync();

        var contract = new TaggingRequestEntity
        {
            ContractType = "Gratis",
            RequesterUserId = userA,
            TagOwnerUserId = userB,
            TargetItemId = targetItem.Id,
            RequestedTagId = tag.Id,
            ConsumedRightAssetId = null, // アセットなし
            Status = TradeStatus.Proposed,
            OwnerId = userA
        };
        dbContext.TaggingRequestEntities!.Add(contract);
        await dbContext.SaveChangesAsync();

        // Act
        // When: ユーザーBが承認を実行する
        var acceptResult = await service.AcceptContractAsync(contract.Id, userB);
        Assert.True(acceptResult is Success<string>, acceptResult switch { Failure f => f.ErrorMessage, _ => "Expected Success" });

        // Assert
        // Then: タグリレーションが作成される
        TagRelation? relation =
            await dbContext.TagRelations!.FirstOrDefaultAsync(tr => tr.ItemId == targetItem.Id && tr.TagId == tag.Id);
        Assert.NotNull(relation);

        // Then: ユーザーB（タグオーナー）が自らタグ発行のための RightAsset を新規発行し、即消費する
        TagWeightLedger? ledger =
            await dbContext.TagWeightLedgers!.FirstOrDefaultAsync(l => l.SourceId == relation.Id);
        Assert.NotNull(ledger);
        Assert.NotNull(ledger.ConsumedRightAssetId);

        RightAsset? mintedAsset = await dbContext.RightAssets!.IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Id == ledger.ConsumedRightAssetId);
        Assert.NotNull(mintedAsset);
        Assert.True(mintedAsset.IsBurned);
        Assert.Equal(userB, mintedAsset.OwnerId); // オーナーが発行したアセット
    }

    [Fact]
    public async Task ScenarioI_GratisRemove_ShouldRemoveRelationAndDecreaseCachedWeight()
    {
        // Arrange
        await using var scope = CreateTestScope();
        var (dbContext, service, tid) = scope;

        var userA = $"ua_{tid}";
        var userB = $"ub_{tid}";
        await dbContext.SeedUsersAsync(userA, userB);

        var targetItem = new Item { Content = $"TargetItem_{tid}", OwnerId = userA };
        var tag = new Tag { Name = $"RemovableTag_{tid}", OwnerId = userB, CachedWeight = 10 };

        dbContext.Items!.Add(targetItem);
        dbContext.Tags!.Add(tag);
        await dbContext.SaveChangesAsync();

        var relation = new TagRelation { ItemId = targetItem.Id, TagId = tag.Id, OwnerId = userB, Weight = 3 };
        dbContext.TagRelations!.Add(relation);

        var contract = new TaggingRequestEntity
        {
            ContractType = "Gratis",
            OwnerId = userA,
            RequesterUserId = userA,
            TagOwnerUserId = userB,
            TargetItemId = targetItem.Id,
            RequestedTagId = tag.Id,
            Status = TradeStatus.Proposed,
            Payload = new GratisPayload("Please remove this tag"),
            RequestType = TaggingRequestType.Remove
        };
        dbContext.TaggingRequestEntities!.Add(contract);
        await dbContext.SaveChangesAsync();

        // Act
        var acceptResult = await service.AcceptContractAsync(contract.Id, userB);
        Assert.True(acceptResult is Success<string>, acceptResult switch { Failure f => f.ErrorMessage, _ => "Expected Success" });

        // Assert
        // タグリレーションがDBから削除される
        var relationExists = await dbContext.TagRelations.AnyAsync(tr => tr.ItemId == targetItem.Id && tr.TagId == tag.Id);
        Assert.False(relationExists);

        // 契約ステータスが Executed になる
        TaggingRequestEntity? updatedContract = await dbContext.TaggingRequestEntities.FindAsync(contract.Id);
        Assert.Equal(TradeStatus.Executed, updatedContract!.Status);

        // CachedWeight がリレーションの重さの分だけ減少する
        Tag? updatedTag = await dbContext.Tags.FindAsync(tag.Id);
        Assert.Equal(7, updatedTag!.CachedWeight);
    }
}