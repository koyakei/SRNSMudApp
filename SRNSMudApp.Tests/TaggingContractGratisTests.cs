using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;

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
        dbContext.ChangeTracker.Clear();
        TaggingRequestEntity? updatedContract = await dbContext.TaggingRequestEntities.FindAsync(contract.Id);
        Assert.Equal(TradeStatus.Executed, updatedContract!.Status);

        // CachedWeight がリレーションの重さの分だけ減少する
        Tag? updatedTag = await dbContext.Tags.FindAsync(tag.Id);
        Assert.Equal(7, updatedTag!.CachedWeight);
    }

    [Fact]
    public async Task GratisRemove_WhenTagRelationDoesNotExist_ShouldSucceedWithEarlyExit()
    {
        // Arrange: 削除対象の TagRelation が存在しない状態で Remove 契約を作成
        await using var scope = CreateTestScope();
        var (dbContext, service, tid) = scope;

        var userA = $"ua_{tid}";
        var userB = $"ub_{tid}";
        await dbContext.SeedUsersAsync(userA, userB);

        var targetItem = new Item { Content = $"TargetItem_{tid}", OwnerId = userA };
        var tag = new Tag { Name = $"Tag_{tid}", OwnerId = userB, CachedWeight = 10 };

        dbContext.Items!.Add(targetItem);
        dbContext.Tags!.Add(tag);
        await dbContext.SaveChangesAsync();

        var contract = new TaggingRequestEntity
        {
            ContractType = "Gratis",
            OwnerId = userA,
            RequesterUserId = userA,
            TagOwnerUserId = userB,
            TargetItemId = targetItem.Id,
            RequestedTagId = tag.Id,
            Payload = new GratisPayload("Please remove non-existent tag"),
            RequestType = TaggingRequestType.Remove
        };
        dbContext.TaggingRequestEntities!.Add(contract);
        await dbContext.SaveChangesAsync();

        // Act: 承認を実行
        var acceptResult = await service.AcceptContractAsync(contract.Id, userB);

        // Assert: "削除対象が存在しません。" で早期正常終了し、Executed になること
        Assert.True(acceptResult is Success<string>, acceptResult switch { Failure f => f.ErrorMessage, _ => "Expected Success" });
        var successMessage = acceptResult switch { Success<string> s => s.Value, _ => null };
        Assert.Equal("削除対象が存在しません。", successMessage);

        dbContext.ChangeTracker.Clear();
        TaggingRequestEntity? updatedContract = await dbContext.TaggingRequestEntities.FindAsync(contract.Id);
        Assert.Equal(TradeStatus.Executed, updatedContract!.Status);

        Tag? updatedTag = await dbContext.Tags.FindAsync(tag.Id);
        Assert.Equal(10, updatedTag!.CachedWeight); // 減算されないこと
    }

    [Fact]
    public async Task GratisDecreaseWeight_Item_WhenWeightRemainsPositive_UpdatesRelationWeight()
    {
        await using var scope = CreateTestScope();
        var (dbContext, service, tid) = scope;

        var userA = $"ua_{tid}";
        var userB = $"ub_{tid}";
        await dbContext.SeedUsersAsync(userA, userB);

        var targetItem = new Item { Content = $"TargetItem_{tid}", OwnerId = userA };
        var tag = new Tag { Name = $"Tag_{tid}", OwnerId = userB, CachedWeight = 10 };

        dbContext.Items!.Add(targetItem);
        dbContext.Tags!.Add(tag);
        await dbContext.SaveChangesAsync();

        var relation = new TagRelation { ItemId = targetItem.Id, TagId = tag.Id, OwnerId = userB, Weight = 5 };
        dbContext.TagRelations!.Add(relation);

        var contract = new TaggingRequestEntity
        {
            ContractType = "Gratis",
            OwnerId = userA,
            RequesterUserId = userA,
            TagOwnerUserId = userB,
            TargetItemId = targetItem.Id,
            RequestedTagId = tag.Id,
            ProposedWeight = 2,
            Payload = new GratisPayload("Please decrease weight"),
            RequestType = TaggingRequestType.DecreaseWeight
        };
        dbContext.TaggingRequestEntities!.Add(contract);
        await dbContext.SaveChangesAsync();

        // Act
        var acceptResult = await service.AcceptContractAsync(contract.Id, userB);

        // Assert
        Assert.True(acceptResult is Success<string>, acceptResult switch { Failure f => f.ErrorMessage, _ => "Expected Success" });

        dbContext.ChangeTracker.Clear();
        var updatedRelation = await dbContext.TagRelations.FirstOrDefaultAsync(tr => tr.ItemId == targetItem.Id && tr.TagId == tag.Id);
        Assert.NotNull(updatedRelation);
        Assert.Equal(3, updatedRelation.Weight); // 5 - 2 = 3

        var updatedTag = await dbContext.Tags.FindAsync(tag.Id);
        Assert.Equal(8, updatedTag!.CachedWeight); // 10 - 2 = 8

        var ledger = await dbContext.TagWeightLedgers.FirstOrDefaultAsync(l => l.SourceId == relation.Id && l.Delta == -2);
        Assert.NotNull(ledger);
        Assert.Equal(10, ledger.PreviousWeight);
        Assert.Equal(8, ledger.NewWeight);
    }

    [Fact]
    public async Task GratisDecreaseWeight_Item_WhenWeightReachesZeroOrLess_RemovesRelation()
    {
        await using var scope = CreateTestScope();
        var (dbContext, service, tid) = scope;

        var userA = $"ua_{tid}";
        var userB = $"ub_{tid}";
        await dbContext.SeedUsersAsync(userA, userB);

        var targetItem = new Item { Content = $"TargetItem_{tid}", OwnerId = userA };
        var tag = new Tag { Name = $"Tag_{tid}", OwnerId = userB, CachedWeight = 10 };

        dbContext.Items!.Add(targetItem);
        dbContext.Tags!.Add(tag);
        await dbContext.SaveChangesAsync();

        var relation = new TagRelation { ItemId = targetItem.Id, TagId = tag.Id, OwnerId = userB, Weight = 3 };
        dbContext.TagRelations!.Add(relation);

        // ProposedWeight が 4 (残量 3 を超えるため <= 0 になり削除)
        var contract = new TaggingRequestEntity
        {
            ContractType = "Gratis",
            OwnerId = userA,
            RequesterUserId = userA,
            TagOwnerUserId = userB,
            TargetItemId = targetItem.Id,
            RequestedTagId = tag.Id,
            ProposedWeight = 4,
            Payload = new GratisPayload("Please decrease weight"),
            RequestType = TaggingRequestType.DecreaseWeight
        };
        dbContext.TaggingRequestEntities!.Add(contract);
        await dbContext.SaveChangesAsync();

        // Act
        var acceptResult = await service.AcceptContractAsync(contract.Id, userB);

        // Assert
        Assert.True(acceptResult is Success<string>, acceptResult switch { Failure f => f.ErrorMessage, _ => "Expected Success" });

        dbContext.ChangeTracker.Clear();
        var relationExists = await dbContext.TagRelations.AnyAsync(tr => tr.ItemId == targetItem.Id && tr.TagId == tag.Id);
        Assert.False(relationExists);

        var updatedTag = await dbContext.Tags.FindAsync(tag.Id);
        Assert.Equal(6, updatedTag!.CachedWeight); // 10 - 4 = 6
    }

    [Fact]
    public async Task GratisDecreaseWeight_TagEdge_WhenWeightRemainsPositive_DecreasesAttachmentWeight()
    {
        await using var scope = CreateTestScope();
        var (dbContext, service, tid) = scope;

        var userA = $"ua_{tid}";
        var userB = $"ub_{tid}";
        await dbContext.SeedUsersAsync(userA, userB);

        var tagA = new Tag { Name = $"Source_{tid}", OwnerId = userA };
        var tagB = new Tag { Name = $"Target_{tid}", OwnerId = userA };
        var tagMeaning = new Tag { Name = $"Meaning_{tid}", OwnerId = userB, CachedWeight = 20 };
        dbContext.Tags.AddRange(tagA, tagB, tagMeaning);
        await dbContext.SaveChangesAsync();

        var edge = new TagEdge { SourceTagId = tagA.Id, TargetTagId = tagB.Id, OwnerId = userA };
        dbContext.TagEdges.Add(edge);

        var initialAsset = new RightAsset
        {
            TargetTagId = tagMeaning.Id,
            OwnerId = userB,
            Amount = 1,
            IsBurned = true
        };
        dbContext.RightAssets.Add(initialAsset);
        await dbContext.SaveChangesAsync();

        var attachment = new TagEdgeTagAttachment
        {
            TagEdgeId = edge.Id,
            TagId = tagMeaning.Id,
            Weight = 5,
            OwnerId = userA,
            ConsumedRightAssetId = initialAsset.Id
        };
        dbContext.TagEdgeTagAttachments.Add(attachment);
        await dbContext.SaveChangesAsync();

        var proposeResult = await service.ProposeGratisEdgeContractAsync(
            requesterUserId: userA,
            tagOwnerUserId: userB,
            tagEdgeId: edge.Id,
            requestedTagId: tagMeaning.Id,
            requestType: TaggingRequestType.DecreaseWeight,
            proposedWeight: 2,
            message: "Decrease edge tag weight"
        );
        Assert.True(proposeResult is Success<TaggingRequestEntity>);
        var contract = proposeResult switch { Success<TaggingRequestEntity> s => s.Value, _ => throw new InvalidOperationException() };

        // Act
        var acceptResult = await service.AcceptContractAsync(contract.Id, userB);

        // Assert
        Assert.True(acceptResult is Success<string>, acceptResult switch { Failure f => f.ErrorMessage, _ => "Expected Success" });

        dbContext.ChangeTracker.Clear();
        var updatedAttachment = await dbContext.TagEdgeTagAttachments.FirstOrDefaultAsync(a => a.TagEdgeId == edge.Id && a.TagId == tagMeaning.Id);
        Assert.NotNull(updatedAttachment);
        Assert.Equal(3, updatedAttachment.Weight); // 5 - 2 = 3

        var updatedTag = await dbContext.Tags.FindAsync(tagMeaning.Id);
        Assert.Equal(18, updatedTag!.CachedWeight); // 20 - 2 = 18

        var ledger = await dbContext.TagWeightLedgers.FirstOrDefaultAsync(l => l.TagId == tagMeaning.Id && l.SourceType == "TagEdgeTagAttachmentUpdate");
        Assert.NotNull(ledger);
        Assert.Equal(-2, ledger.Delta);
    }

    [Fact]
    public async Task GratisRemove_TagEdge_RemovesAttachment()
    {
        await using var scope = CreateTestScope();
        var (dbContext, service, tid) = scope;

        var userA = $"ua_{tid}";
        var userB = $"ub_{tid}";
        await dbContext.SeedUsersAsync(userA, userB);

        var tagA = new Tag { Name = $"Source_{tid}", OwnerId = userA };
        var tagB = new Tag { Name = $"Target_{tid}", OwnerId = userA };
        var tagMeaning = new Tag { Name = $"Meaning_{tid}", OwnerId = userB, CachedWeight = 20 };
        dbContext.Tags.AddRange(tagA, tagB, tagMeaning);
        await dbContext.SaveChangesAsync();

        var edge = new TagEdge { SourceTagId = tagA.Id, TargetTagId = tagB.Id, OwnerId = userA };
        dbContext.TagEdges.Add(edge);

        var initialAsset = new RightAsset
        {
            TargetTagId = tagMeaning.Id,
            OwnerId = userB,
            Amount = 1,
            IsBurned = true
        };
        dbContext.RightAssets.Add(initialAsset);
        await dbContext.SaveChangesAsync();

        var attachment = new TagEdgeTagAttachment
        {
            TagEdgeId = edge.Id,
            TagId = tagMeaning.Id,
            Weight = 3,
            OwnerId = userA,
            ConsumedRightAssetId = initialAsset.Id
        };
        dbContext.TagEdgeTagAttachments.Add(attachment);
        await dbContext.SaveChangesAsync();

        var proposeResult = await service.ProposeGratisEdgeContractAsync(
            requesterUserId: userA,
            tagOwnerUserId: userB,
            tagEdgeId: edge.Id,
            requestedTagId: tagMeaning.Id,
            requestType: TaggingRequestType.Remove,
            proposedWeight: 3,
            message: "Remove edge tag"
        );
        Assert.True(proposeResult is Success<TaggingRequestEntity>);
        var contract = proposeResult switch { Success<TaggingRequestEntity> s => s.Value, _ => throw new InvalidOperationException() };

        // Act
        var acceptResult = await service.AcceptContractAsync(contract.Id, userB);

        // Assert
        Assert.True(acceptResult is Success<string>, acceptResult switch { Failure f => f.ErrorMessage, _ => "Expected Success" });

        dbContext.ChangeTracker.Clear();
        var attachmentExists = await dbContext.TagEdgeTagAttachments.AnyAsync(a => a.TagEdgeId == edge.Id && a.TagId == tagMeaning.Id);
        Assert.False(attachmentExists);

        var updatedTag = await dbContext.Tags.FindAsync(tagMeaning.Id);
        Assert.Equal(17, updatedTag!.CachedWeight); // 20 - 3 = 17

        var ledger = await dbContext.TagWeightLedgers.FirstOrDefaultAsync(l => l.TagId == tagMeaning.Id && l.SourceType == "TagEdgeTagAttachmentDelete");
        Assert.NotNull(ledger);
        Assert.Equal(-3, ledger.Delta);
    }

    [Fact]
    public async Task GratisRemove_TagEdge_WhenAttachmentDoesNotExist_ReturnsFailure()
    {
        await using var scope = CreateTestScope();
        var (dbContext, service, tid) = scope;

        var userA = $"ua_{tid}";
        var userB = $"ub_{tid}";
        await dbContext.SeedUsersAsync(userA, userB);

        var tagA = new Tag { Name = $"Source_{tid}", OwnerId = userA };
        var tagB = new Tag { Name = $"Target_{tid}", OwnerId = userA };
        var tagMeaning = new Tag { Name = $"Meaning_{tid}", OwnerId = userB, CachedWeight = 20 };
        dbContext.Tags.AddRange(tagA, tagB, tagMeaning);
        await dbContext.SaveChangesAsync();

        var edge = new TagEdge { SourceTagId = tagA.Id, TargetTagId = tagB.Id, OwnerId = userA };
        dbContext.TagEdges.Add(edge);
        await dbContext.SaveChangesAsync();

        // 紐付けアタッチメントが存在しない状態で Remove 契約を作成
        var proposeResult = await service.ProposeGratisEdgeContractAsync(
            requesterUserId: userA,
            tagOwnerUserId: userB,
            tagEdgeId: edge.Id,
            requestedTagId: tagMeaning.Id,
            requestType: TaggingRequestType.Remove,
            proposedWeight: 1,
            message: "Remove non-existent edge tag"
        );
        Assert.True(proposeResult is Success<TaggingRequestEntity>);
        var contract = proposeResult switch { Success<TaggingRequestEntity> s => s.Value, _ => throw new InvalidOperationException() };

        // Act
        var acceptResult = await service.AcceptContractAsync(contract.Id, userB);

        // Assert: エッジの紐付けが存在しない場合は Failure(TagEdgeAttachmentNotFound)
        Assert.True(acceptResult is Failure fail && fail.ErrorMessage == ContractMessages.TagEdgeAttachmentNotFound);
    }
}