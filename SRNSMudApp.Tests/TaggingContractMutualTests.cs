using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;

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
            OwnerId = userA
        };
        dbContext.TaggingRequestEntities!.Add(contract);
        await dbContext.SaveChangesAsync();

        // Act
        var acceptResult = await service.AcceptContractAsync(contract.Id, userB);
        Assert.True(acceptResult is Success<string>, acceptResult switch { Failure f => f.ErrorMessage, _ => "Expected Success" });

        // Assert
        dbContext.ChangeTracker.Clear();
        var updatedAssetA = await dbContext.RightAssets.FindAsync(rightAssetA.Id);
        Assert.NotNull(updatedAssetA);
        Assert.True(updatedAssetA!.IsBurned);
        Assert.Equal(0, updatedAssetA.Amount);

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

    [Fact]
    public async Task MutualRemove_WhenTargetRelationExists_ShouldSwapAndSucceed()
    {
        // Arrange: 相互タグ付けで要求タグを削除し、対価タグを付与する
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

        // targetItemA に既に tagB が付与されている
        var relationA = new TagRelation { ItemId = targetItemA.Id, TagId = tagB.Id, OwnerId = userB, Weight = 5 };
        dbContext.TagRelations!.Add(relationA);

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
            ProposedWeight = 3,
            Payload = new MutualPayload(targetItemB.Id, tagA.Id),
            ConsumedRightAssetId = rightAssetA.Id,
            OwnerId = userA,
            RequestType = TaggingRequestType.Remove
        };
        dbContext.TaggingRequestEntities!.Add(contract);
        await dbContext.SaveChangesAsync();

        // Act
        var acceptResult = await service.AcceptContractAsync(contract.Id, userB);

        // Assert
        Assert.True(acceptResult is Success<string>, acceptResult switch { Failure f => f.ErrorMessage, _ => "Expected Success" });
        var acceptMessage = acceptResult switch { Success<string> s => s.Value, _ => null };
        Assert.Equal("相互タグ付けが完了しました。", acceptMessage);

        dbContext.ChangeTracker.Clear();
        // targetItemA から tagB が削除されていること
        var relationAExists = await dbContext.TagRelations.AnyAsync(tr => tr.ItemId == targetItemA.Id && tr.TagId == tagB.Id);
        Assert.False(relationAExists);

        // targetItemB に tagA が追加されていること
        var relationB = await dbContext.TagRelations.FirstOrDefaultAsync(tr => tr.ItemId == targetItemB.Id && tr.TagId == tagA.Id);
        Assert.NotNull(relationB);
        Assert.Equal(3, relationB.Weight);

        // キャッシュされた重みが更新されていること
        var updatedTagA = await dbContext.Tags.FindAsync(tagA.Id);
        var updatedTagB = await dbContext.Tags.FindAsync(tagB.Id);
        Assert.Equal(53, updatedTagA!.CachedWeight); // +3
        Assert.Equal(95, updatedTagB!.CachedWeight); // -5
    }

    [Fact]
    public async Task MutualRemove_WhenTagRelationDoesNotExist_ShouldSucceedWithEarlyExit()
    {
        // Arrange: targetItemA に tagB のリレーションが存在しない状態で Remove 相互契約
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
            ProposedWeight = 3,
            Payload = new MutualPayload(targetItemB.Id, tagA.Id),
            ConsumedRightAssetId = rightAssetA.Id,
            OwnerId = userA,
            RequestType = TaggingRequestType.Remove
        };
        dbContext.TaggingRequestEntities!.Add(contract);
        await dbContext.SaveChangesAsync();

        // Act: 承認を実行
        var acceptResult = await service.AcceptContractAsync(contract.Id, userB);

        // Assert: "削除対象が存在しません。" で早期正常終了し、対価タグの追加は行われないこと
        Assert.True(acceptResult is Success<string>, acceptResult switch { Failure f => f.ErrorMessage, _ => "Expected Success" });
        var acceptMessage = acceptResult switch { Success<string> s => s.Value, _ => null };
        Assert.Equal("削除対象が存在しません。", acceptMessage);

        dbContext.ChangeTracker.Clear();
        // targetItemB に tagA は付与されていないこと
        var relationBExists = await dbContext.TagRelations.AnyAsync(tr => tr.ItemId == targetItemB.Id && tr.TagId == tagA.Id);
        Assert.False(relationBExists);

        // キャッシュされた重みが変動していないこと
        var updatedTagA = await dbContext.Tags.FindAsync(tagA.Id);
        var updatedTagB = await dbContext.Tags.FindAsync(tagB.Id);
        Assert.Equal(50, updatedTagA!.CachedWeight);
        Assert.Equal(100, updatedTagB!.CachedWeight);

        // 契約ステータスが Executed に更新されていること
        var updatedContract = await dbContext.TaggingRequestEntities.FindAsync(contract.Id);
        Assert.Equal(TradeStatus.Executed, updatedContract!.Status);
    }

    [Fact]
    public async Task MutualTagging_WhenSurplusAssetAmount_ShouldDecreaseAmountAndKeepNotBurned()
    {
        // Arrange: 残高 5 のアセットを提供して相互タグ付けを実行
        await using var scope = CreateTestScope();
        var (dbContext, service, tid) = scope;

        var userA = $"ua_{tid}";
        var userB = $"ub_{tid}";
        await dbContext.SeedUsersAsync(userA, userB);

        var targetItemA = new Item { Content = $"TargetItemA_{tid}", OwnerId = userA };
        var targetItemB = new Item { Content = $"TargetItemB_{tid}", OwnerId = userB };
        var tagA = new Tag { Name = $"TagA_{tid}", OwnerId = userA, CachedWeight = 50 };
        var tagB = new Tag { Name = $"TagB_{tid}", OwnerId = userB, CachedWeight = 100 };
        dbContext.Items.AddRange(targetItemA, targetItemB);
        dbContext.Tags.AddRange(tagA, tagB);
        await dbContext.SaveChangesAsync();

        var rightAssetA = new RightAsset { Amount = 5, OwnerId = userA, TargetTagId = tagB.Id };
        dbContext.RightAssets.Add(rightAssetA);
        await dbContext.SaveChangesAsync();

        var contract = new TaggingRequestEntity
        {
            ContractType = "Mutual",
            RequesterUserId = userA,
            TagOwnerUserId = userB,
            TargetItemId = targetItemA.Id,
            RequestedTagId = tagB.Id,
            ProposedWeight = 2,
            Payload = new MutualPayload(targetItemB.Id, tagA.Id),
            ConsumedRightAssetId = rightAssetA.Id,
            OwnerId = userA
        };
        dbContext.TaggingRequestEntities.Add(contract);
        await dbContext.SaveChangesAsync();

        // Act
        var acceptResult = await service.AcceptContractAsync(contract.Id, userB);
        Assert.True(acceptResult is Success<string>, acceptResult switch { Failure f => f.ErrorMessage, _ => "Expected Success" });

        // Assert: 5 - 2 = 3 が手元に残り、IsBurned == false
        dbContext.ChangeTracker.Clear();
        var updatedAsset = await dbContext.RightAssets.FindAsync(rightAssetA.Id);
        Assert.NotNull(updatedAsset);
        Assert.False(updatedAsset!.IsBurned);
        Assert.Equal(3, updatedAsset.Amount);
    }

    [Fact]
    public async Task MutualTagging_WhenAssetBelongsToOtherUser_ShouldReturnFailure()
    {
        // Arrange
        await using var scope = CreateTestScope();
        var (dbContext, service, tid) = scope;

        var userA = $"ua_{tid}";
        var userB = $"ub_{tid}";
        var userC = $"uc_{tid}";
        await dbContext.SeedUsersAsync(userA, userB, userC);

        var targetItemA = new Item { Content = $"TargetItemA_{tid}", OwnerId = userA };
        var targetItemB = new Item { Content = $"TargetItemB_{tid}", OwnerId = userB };
        var tagA = new Tag { Name = $"TagA_{tid}", OwnerId = userA };
        var tagB = new Tag { Name = $"TagB_{tid}", OwnerId = userB };
        dbContext.Items.AddRange(targetItemA, targetItemB);
        dbContext.Tags.AddRange(tagA, tagB);
        await dbContext.SaveChangesAsync();

        // userC 所有のアセット
        var rightAsset = new RightAsset { Amount = 1, OwnerId = userC, TargetTagId = tagB.Id };
        dbContext.RightAssets.Add(rightAsset);
        await dbContext.SaveChangesAsync();

        var contract = new TaggingRequestEntity
        {
            ContractType = "Mutual",
            RequesterUserId = userA,
            TagOwnerUserId = userB,
            TargetItemId = targetItemA.Id,
            RequestedTagId = tagB.Id,
            Payload = new MutualPayload(targetItemB.Id, tagA.Id),
            ConsumedRightAssetId = rightAsset.Id,
            OwnerId = userA
        };
        dbContext.TaggingRequestEntities.Add(contract);
        await dbContext.SaveChangesAsync();

        // Act
        var acceptResult = await service.AcceptContractAsync(contract.Id, userB);

        // Assert
        Assert.True(acceptResult is Failure);
        var fail = acceptResult switch { Failure f => f, _ => throw new InvalidOperationException() };
        Assert.Equal("指定された RightAsset を所有していません。", fail.ErrorMessage);
    }

    [Fact]
    public async Task MutualTagging_WhenAssetIsAlreadyBurned_ShouldReturnFailure()
    {
        // Arrange
        await using var scope = CreateTestScope();
        var (dbContext, service, tid) = scope;

        var userA = $"ua_{tid}";
        var userB = $"ub_{tid}";
        await dbContext.SeedUsersAsync(userA, userB);

        var targetItemA = new Item { Content = $"TargetItemA_{tid}", OwnerId = userA };
        var targetItemB = new Item { Content = $"TargetItemB_{tid}", OwnerId = userB };
        var tagA = new Tag { Name = $"TagA_{tid}", OwnerId = userA };
        var tagB = new Tag { Name = $"TagB_{tid}", OwnerId = userB };
        dbContext.Items.AddRange(targetItemA, targetItemB);
        dbContext.Tags.AddRange(tagA, tagB);
        await dbContext.SaveChangesAsync();

        var rightAsset = new RightAsset { Amount = 1, OwnerId = userA, TargetTagId = tagB.Id, IsBurned = true };
        dbContext.RightAssets.Add(rightAsset);
        await dbContext.SaveChangesAsync();

        var contract = new TaggingRequestEntity
        {
            ContractType = "Mutual",
            RequesterUserId = userA,
            TagOwnerUserId = userB,
            TargetItemId = targetItemA.Id,
            RequestedTagId = tagB.Id,
            Payload = new MutualPayload(targetItemB.Id, tagA.Id),
            ConsumedRightAssetId = rightAsset.Id,
            OwnerId = userA
        };
        dbContext.TaggingRequestEntities.Add(contract);
        await dbContext.SaveChangesAsync();

        // Act
        var acceptResult = await service.AcceptContractAsync(contract.Id, userB);

        // Assert
        Assert.True(acceptResult is Failure);
        var fail = acceptResult switch { Failure f => f, _ => throw new InvalidOperationException() };
        Assert.Equal("指定された RightAsset は既に消費済みです。", fail.ErrorMessage);
    }

    [Fact]
    public async Task MutualTagging_WhenAssetTargetTagMismatch_ShouldReturnFailure()
    {
        // Arrange
        await using var scope = CreateTestScope();
        var (dbContext, service, tid) = scope;

        var userA = $"ua_{tid}";
        var userB = $"ub_{tid}";
        await dbContext.SeedUsersAsync(userA, userB);

        var targetItemA = new Item { Content = $"TargetItemA_{tid}", OwnerId = userA };
        var targetItemB = new Item { Content = $"TargetItemB_{tid}", OwnerId = userB };
        var tagA = new Tag { Name = $"TagA_{tid}", OwnerId = userA };
        var tagB = new Tag { Name = $"TagB_{tid}", OwnerId = userB };
        var otherTag = new Tag { Name = $"OtherTag_{tid}", OwnerId = userB };
        dbContext.Items.AddRange(targetItemA, targetItemB);
        dbContext.Tags.AddRange(tagA, tagB, otherTag);
        await dbContext.SaveChangesAsync();

        // otherTag 向けのアセット
        var rightAsset = new RightAsset { Amount = 1, OwnerId = userA, TargetTagId = otherTag.Id };
        dbContext.RightAssets.Add(rightAsset);
        await dbContext.SaveChangesAsync();

        var contract = new TaggingRequestEntity
        {
            ContractType = "Mutual",
            RequesterUserId = userA,
            TagOwnerUserId = userB,
            TargetItemId = targetItemA.Id,
            RequestedTagId = tagB.Id,
            Payload = new MutualPayload(targetItemB.Id, tagA.Id),
            ConsumedRightAssetId = rightAsset.Id,
            OwnerId = userA
        };
        dbContext.TaggingRequestEntities.Add(contract);
        await dbContext.SaveChangesAsync();

        // Act
        var acceptResult = await service.AcceptContractAsync(contract.Id, userB);

        // Assert
        Assert.True(acceptResult is Failure);
        var fail = acceptResult switch { Failure f => f, _ => throw new InvalidOperationException() };
        Assert.Equal("指定された RightAsset は対象タグの権利ではありません。", fail.ErrorMessage);
    }

    [Fact]
    public async Task MutualTagging_WhenAssetAmountIsInsufficient_ShouldReturnFailure()
    {
        // Arrange
        await using var scope = CreateTestScope();
        var (dbContext, service, tid) = scope;

        var userA = $"ua_{tid}";
        var userB = $"ub_{tid}";
        await dbContext.SeedUsersAsync(userA, userB);

        var targetItemA = new Item { Content = $"TargetItemA_{tid}", OwnerId = userA };
        var targetItemB = new Item { Content = $"TargetItemB_{tid}", OwnerId = userB };
        var tagA = new Tag { Name = $"TagA_{tid}", OwnerId = userA };
        var tagB = new Tag { Name = $"TagB_{tid}", OwnerId = userB };
        dbContext.Items.AddRange(targetItemA, targetItemB);
        dbContext.Tags.AddRange(tagA, tagB);
        await dbContext.SaveChangesAsync();

        // 残高 0 なのに相互契約を要求
        var rightAsset = new RightAsset { Amount = 0, OwnerId = userA, TargetTagId = tagB.Id };
        dbContext.RightAssets.Add(rightAsset);
        await dbContext.SaveChangesAsync();

        var contract = new TaggingRequestEntity
        {
            ContractType = "Mutual",
            RequesterUserId = userA,
            TagOwnerUserId = userB,
            TargetItemId = targetItemA.Id,
            RequestedTagId = tagB.Id,
            ProposedWeight = 2,
            Payload = new MutualPayload(targetItemB.Id, tagA.Id),
            ConsumedRightAssetId = rightAsset.Id,
            OwnerId = userA
        };
        dbContext.TaggingRequestEntities.Add(contract);
        await dbContext.SaveChangesAsync();

        // Act
        var acceptResult = await service.AcceptContractAsync(contract.Id, userB);

        // Assert
        Assert.True(acceptResult is Failure);
        var fail = acceptResult switch { Failure f => f, _ => throw new InvalidOperationException() };
        Assert.Equal("指定された RightAsset の残量が不足しています。", fail.ErrorMessage);
    }
}