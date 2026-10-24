using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;

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

    [Fact]
    public async Task Trigger_SurplusAssetAmount_ShouldConsumeAssetAndAttachTag()
    {
        // Arrange: 要求量 5 に対して残高 10 のアセットを指定してトリガー
        await using var scope = CreateTestScope();
        var (dbContext, service, tid) = scope;

        var userA = $"ua_{tid}";
        var userB = $"ub_{tid}";
        await dbContext.SeedUsersAsync(userA, userB);

        var targetItemA = new Item { Content = $"TargetItemA_{tid}", OwnerId = userA };
        var tagB = new Tag { Name = $"TagB_{tid}", OwnerId = userB, CachedWeight = 50 };

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
            RequiredAssetAmount = 5, // 5 を要求
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
            OwnerId = userA
        };
        dbContext.TaggingRequestEntities!.Add(contract);
        await dbContext.SaveChangesAsync();

        // Act
        var acceptResult = await service.AcceptContractAsync(contract.Id, userA);

        // Assert: 承認成功し、提供アセットが消費されること
        Assert.True(acceptResult is Success<string>, acceptResult switch { Failure f => f.ErrorMessage, _ => "Expected Success" });

        dbContext.ChangeTracker.Clear();
        var updatedAsset = await dbContext.RightAssets.FindAsync(rightAsset.Id);
        Assert.NotNull(updatedAsset);
        Assert.False(updatedAsset!.IsBurned);
        Assert.Equal(5, updatedAsset.Amount); // 10 - 5 = 5 (超過分が手元に残る)

        var relation = await dbContext.TagRelations.FirstOrDefaultAsync(tr => tr.ItemId == targetItemA.Id && tr.TagId == tagB.Id);
        Assert.NotNull(relation);
    }

    [Fact]
    public async Task Trigger_WhenAssetAmountExactlyMatchesRequired_ShouldBurnAssetAndAttachTag()
    {
        // Arrange: 要求量 5 に対して残高 5 のアセットを指定してトリガー
        await using var scope = CreateTestScope();
        var (dbContext, service, tid) = scope;

        var userA = $"ua_{tid}";
        var userB = $"ub_{tid}";
        await dbContext.SeedUsersAsync(userA, userB);

        var targetItemA = new Item { Content = $"TargetItemA_{tid}", OwnerId = userA };
        var tagB = new Tag { Name = $"TagB_{tid}", OwnerId = userB, CachedWeight = 50 };

        dbContext.Items.Add(targetItemA);
        dbContext.Tags.Add(tagB);
        await dbContext.SaveChangesAsync();

        var rightAsset = new RightAsset { Amount = 5, OwnerId = userA, TargetTagId = tagB.Id };
        dbContext.RightAssets.Add(rightAsset);
        await dbContext.SaveChangesAsync();

        var publicOffer = new PublicTradeOffer
        {
            OwnerId = userB,
            OfferedTagId = tagB.Id,
            RequiredAssetAmount = 5,
            IsActive = true
        };
        dbContext.PublicTradeOffers.Add(publicOffer);
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
            OwnerId = userA
        };
        dbContext.TaggingRequestEntities.Add(contract);
        await dbContext.SaveChangesAsync();

        // Act
        var acceptResult = await service.AcceptContractAsync(contract.Id, userA);

        // Assert: 全量消費され Amount=0, IsBurned=true
        Assert.True(acceptResult is Success<string>, acceptResult switch { Failure f => f.ErrorMessage, _ => "Expected Success" });

        dbContext.ChangeTracker.Clear();
        var updatedAsset = await dbContext.RightAssets.FindAsync(rightAsset.Id);
        Assert.NotNull(updatedAsset);
        Assert.True(updatedAsset!.IsBurned);
        Assert.Equal(0, updatedAsset.Amount);
    }

    [Fact]
    public async Task Trigger_WhenAssetBelongsToOtherUser_ShouldReturnFailure()
    {
        // Arrange: 他人所有のアセットを指定
        await using var scope = CreateTestScope();
        var (dbContext, service, tid) = scope;

        var userA = $"ua_{tid}";
        var userB = $"ub_{tid}";
        var userC = $"uc_{tid}";
        await dbContext.SeedUsersAsync(userA, userB, userC);

        var targetItemA = new Item { Content = $"TargetItemA_{tid}", OwnerId = userA };
        var tagB = new Tag { Name = $"TagB_{tid}", OwnerId = userB };
        dbContext.Items.Add(targetItemA);
        dbContext.Tags.Add(tagB);
        await dbContext.SaveChangesAsync();

        // userC 所有のアセット
        var rightAsset = new RightAsset { Amount = 5, OwnerId = userC, TargetTagId = tagB.Id };
        dbContext.RightAssets.Add(rightAsset);
        await dbContext.SaveChangesAsync();

        var publicOffer = new PublicTradeOffer
        {
            OwnerId = userB,
            OfferedTagId = tagB.Id,
            RequiredAssetAmount = 5,
            IsActive = true
        };
        dbContext.PublicTradeOffers.Add(publicOffer);
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
            OwnerId = userA
        };
        dbContext.TaggingRequestEntities.Add(contract);
        await dbContext.SaveChangesAsync();

        // Act
        var acceptResult = await service.AcceptContractAsync(contract.Id, userA);

        // Assert
        Assert.True(acceptResult is Failure);
        var fail = acceptResult switch { Failure f => f, _ => throw new InvalidOperationException() };
        Assert.Equal("提供された RightAsset を所有していません。", fail.ErrorMessage);
    }

    [Fact]
    public async Task Trigger_WhenAssetIsAlreadyBurned_ShouldReturnFailure()
    {
        // Arrange: 既に Burn 済みのアセットを指定
        await using var scope = CreateTestScope();
        var (dbContext, service, tid) = scope;

        var userA = $"ua_{tid}";
        var userB = $"ub_{tid}";
        await dbContext.SeedUsersAsync(userA, userB);

        var targetItemA = new Item { Content = $"TargetItemA_{tid}", OwnerId = userA };
        var tagB = new Tag { Name = $"TagB_{tid}", OwnerId = userB };
        dbContext.Items.Add(targetItemA);
        dbContext.Tags.Add(tagB);
        await dbContext.SaveChangesAsync();

        var rightAsset = new RightAsset { Amount = 5, OwnerId = userA, TargetTagId = tagB.Id, IsBurned = true };
        dbContext.RightAssets.Add(rightAsset);
        await dbContext.SaveChangesAsync();

        var publicOffer = new PublicTradeOffer
        {
            OwnerId = userB,
            OfferedTagId = tagB.Id,
            RequiredAssetAmount = 5,
            IsActive = true
        };
        dbContext.PublicTradeOffers.Add(publicOffer);
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
            OwnerId = userA
        };
        dbContext.TaggingRequestEntities.Add(contract);
        await dbContext.SaveChangesAsync();

        // Act
        var acceptResult = await service.AcceptContractAsync(contract.Id, userA);

        // Assert
        Assert.True(acceptResult is Failure);
        var fail = acceptResult switch { Failure f => f, _ => throw new InvalidOperationException() };
        Assert.Equal("提供された RightAsset は既に消費済みです。", fail.ErrorMessage);
    }

    [Fact]
    public async Task Trigger_WhenOfferDoesNotExist_ShouldReturnFailure()
    {
        // Arrange: 存在しないオファー ID を指定
        await using var scope = CreateTestScope();
        var (dbContext, service, tid) = scope;

        var userA = $"ua_{tid}";
        var userB = $"ub_{tid}";
        await dbContext.SeedUsersAsync(userA, userB);

        var targetItemA = new Item { Content = $"TargetItemA_{tid}", OwnerId = userA };
        var tagB = new Tag { Name = $"TagB_{tid}", OwnerId = userB };
        dbContext.Items.Add(targetItemA);
        dbContext.Tags.Add(tagB);
        await dbContext.SaveChangesAsync();

        var contract = new TaggingRequestEntity
        {
            ContractType = "Trigger",
            RequesterUserId = userA,
            TagOwnerUserId = userB,
            TargetItemId = targetItemA.Id,
            RequestedTagId = tagB.Id,
            Payload = new PublicOfferPayload(999999),
            OwnerId = userA
        };
        dbContext.TaggingRequestEntities.Add(contract);
        await dbContext.SaveChangesAsync();

        // Act
        var acceptResult = await service.AcceptContractAsync(contract.Id, userA);

        // Assert
        Assert.True(acceptResult is Failure);
        var fail = acceptResult switch { Failure f => f, _ => throw new InvalidOperationException() };
        Assert.Equal("公開オファーが見つかりません。", fail.ErrorMessage);
    }

    [Fact]
    public async Task TriggerRemove_WhenRequiredAssetAmountIsZero_ShouldMintOwnerAssetAndRemove()
    {
        // Arrange: 無料オファー (RequiredAssetAmount == 0) での Remove トリガー実行
        await using var scope = CreateTestScope();
        var (dbContext, service, tid) = scope;

        var userA = $"ua_{tid}";
        var userB = $"ub_{tid}";
        await dbContext.SeedUsersAsync(userA, userB);

        var targetItemA = new Item { Content = $"TargetItemA_{tid}", OwnerId = userA };
        var tagB = new Tag { Name = $"TagB_{tid}", OwnerId = userB, CachedWeight = 50 };
        dbContext.Items.Add(targetItemA);
        dbContext.Tags.Add(tagB);
        await dbContext.SaveChangesAsync();

        var relation = new TagRelation { ItemId = targetItemA.Id, TagId = tagB.Id, Weight = 10, OwnerId = userA };
        dbContext.TagRelations.Add(relation);
        await dbContext.SaveChangesAsync();

        var publicOffer = new PublicTradeOffer
        {
            OwnerId = userB,
            OfferedTagId = tagB.Id,
            RequiredAssetAmount = 0, // 無料キャンペーン
            IsActive = true
        };
        dbContext.PublicTradeOffers.Add(publicOffer);
        await dbContext.SaveChangesAsync();

        var contract = new TaggingRequestEntity
        {
            ContractType = "Trigger",
            RequesterUserId = userA,
            TagOwnerUserId = userB,
            TargetItemId = targetItemA.Id,
            RequestedTagId = tagB.Id,
            RequestType = TaggingRequestType.Remove,
            Payload = new PublicOfferPayload(publicOffer.Id),
            OwnerId = userA
        };
        dbContext.TaggingRequestEntities.Add(contract);
        await dbContext.SaveChangesAsync();

        // Act
        var acceptResult = await service.AcceptContractAsync(contract.Id, userA);

        // Assert
        Assert.True(acceptResult is Success<string>, acceptResult switch { Failure f => f.ErrorMessage, _ => "Expected Success" });

        dbContext.ChangeTracker.Clear();
        // リレーションが削除されていること
        var deletedRelation = await dbContext.TagRelations.FirstOrDefaultAsync(tr => tr.ItemId == targetItemA.Id && tr.TagId == tagB.Id);
        Assert.Null(deletedRelation);

        // オーナーにより Goodwill アセットが自動 Mint され Burn されていること
        var mintedAsset = await dbContext.RightAssets.FirstOrDefaultAsync(a => a.OwnerId == userB && a.TargetTagId == tagB.Id && a.IsBurned);
        Assert.NotNull(mintedAsset);
    }

    [Fact]
    public async Task TriggerRemove_WhenRelationExists_ShouldRemoveRelationAndDecreaseWeight()
    {
        // Arrange: 削除対象リレーションが存在する状態で Remove トリガーを実行
        await using var scope = CreateTestScope();
        var (dbContext, service, tid) = scope;

        var userA = $"ua_{tid}";
        var userB = $"ub_{tid}";
        await dbContext.SeedUsersAsync(userA, userB);

        var targetItemA = new Item { Content = $"TargetItemA_{tid}", OwnerId = userA };
        var tagB = new Tag { Name = $"TagB_{tid}", OwnerId = userB, CachedWeight = 50 };

        dbContext.Items!.Add(targetItemA);
        dbContext.Tags!.Add(tagB);
        await dbContext.SaveChangesAsync();

        var relation = new TagRelation { ItemId = targetItemA.Id, TagId = tagB.Id, OwnerId = userB, Weight = 5 };
        dbContext.TagRelations!.Add(relation);

        var publicOffer = new PublicTradeOffer
        {
            OwnerId = userB,
            OfferedTagId = tagB.Id,
            RequiredAssetAmount = 0, // 無料キャンペーン
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
            Payload = new PublicOfferPayload(publicOffer.Id),
            OwnerId = userA,
            RequestType = TaggingRequestType.Remove
        };
        dbContext.TaggingRequestEntities!.Add(contract);
        await dbContext.SaveChangesAsync();

        // Act
        var acceptResult = await service.AcceptContractAsync(contract.Id, userA);

        // Assert
        Assert.True(acceptResult is Success<string>, acceptResult switch { Failure f => f.ErrorMessage, _ => "Expected Success" });

        dbContext.ChangeTracker.Clear();
        var relationExists = await dbContext.TagRelations.AnyAsync(tr => tr.ItemId == targetItemA.Id && tr.TagId == tagB.Id);
        Assert.False(relationExists);

        var updatedTag = await dbContext.Tags.FindAsync(tagB.Id);
        Assert.Equal(45, updatedTag!.CachedWeight); // 50 - 5 = 45

        var ledger = await dbContext.TagWeightLedgers.FirstOrDefaultAsync(l => l.TagId == tagB.Id && l.Reason == "Public Offer Triggered (Remove)");
        Assert.NotNull(ledger);
        Assert.Equal(-5, ledger.Delta);
    }

    [Fact]
    public async Task TriggerRemove_WhenRelationDoesNotExist_ShouldEarlyExitWithSuccess()
    {
        // Arrange: 削除対象リレーションが存在しない状態で Remove トリガーを実行
        await using var scope = CreateTestScope();
        var (dbContext, service, tid) = scope;

        var userA = $"ua_{tid}";
        var userB = $"ub_{tid}";
        await dbContext.SeedUsersAsync(userA, userB);

        var targetItemA = new Item { Content = $"TargetItemA_{tid}", OwnerId = userA };
        var tagB = new Tag { Name = $"TagB_{tid}", OwnerId = userB, CachedWeight = 50 };

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
            Payload = new PublicOfferPayload(publicOffer.Id),
            OwnerId = userA,
            RequestType = TaggingRequestType.Remove
        };
        dbContext.TaggingRequestEntities!.Add(contract);
        await dbContext.SaveChangesAsync();

        // Act
        var acceptResult = await service.AcceptContractAsync(contract.Id, userA);

        // Assert: "削除対象が存在しません。" で早期正常終了
        Assert.True(acceptResult is Success<string>, acceptResult switch { Failure f => f.ErrorMessage, _ => "Expected Success" });
        var msg = acceptResult switch { Success<string> s => s.Value, _ => null };
        Assert.Equal("削除対象が存在しません。", msg);

        dbContext.ChangeTracker.Clear();
        var updatedTag = await dbContext.Tags.FindAsync(tagB.Id);
        Assert.Equal(50, updatedTag!.CachedWeight); // 減算されないこと
    }
}