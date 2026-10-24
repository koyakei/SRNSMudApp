using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;

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
            OwnerId = userA
        };
        dbContext.TaggingRequestEntities!.Add(contract);
        await dbContext.SaveChangesAsync();

        // Act
        var acceptResult = await service.AcceptContractAsync(contract.Id, userC, fulfillerAsset.Id);
        Assert.True(acceptResult is Success<string>, acceptResult switch { Failure f => f.ErrorMessage, _ => "Expected Success" });

        // Assert
        dbContext.ChangeTracker.Clear();
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
            OwnerId = userA
        };
        dbContext.TaggingRequestEntities!.Add(contract);
        await dbContext.SaveChangesAsync();

        // Act
        var acceptResult = await service.AcceptContractAsync(contract.Id, userC, fulfillerAsset.Id);
        Assert.True(acceptResult is Success<string>, acceptResult switch { Failure f => f.ErrorMessage, _ => "Expected Success" });

        // Assert
        dbContext.ChangeTracker.Clear();
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

    [Fact]
    public async Task Bounty_TagOwnerFulfillsWithoutAsset_ShouldAutoMintAndConsumeGoodwillAsset()
    {
        // Arrange: タグオーナー (userB) がアセット指定なしでバウンティを承認
        await using var scope = CreateTestScope();
        var (dbContext, service, tid) = scope;

        var userA = $"ua_{tid}";
        var userB = $"ub_{tid}";
        await dbContext.SeedUsersAsync(userA, userB);

        var targetItemA = new Item { Content = $"Item_{tid}", OwnerId = userA };
        var tagB = new Tag { Name = $"TagB_{tid}", OwnerId = userB, CachedWeight = 10 };

        dbContext.Items!.Add(targetItemA);
        dbContext.Tags!.Add(tagB);
        await dbContext.SaveChangesAsync();

        var contract = new TaggingRequestEntity
        {
            ContractType = "Bounty",
            RequesterUserId = userA,
            TagOwnerUserId = userB,
            TargetItemId = targetItemA.Id,
            RequestedTagId = tagB.Id,
            OwnerId = userA
        };
        dbContext.TaggingRequestEntities!.Add(contract);
        await dbContext.SaveChangesAsync();

        // Act: タグオーナー userB が fulfillerAssetId = null で承認
        var acceptResult = await service.AcceptContractAsync(contract.Id, userB, fulfillerAssetId: null);

        // Assert: 成功し、タグオーナーのアセットが自動Mint&即消費される
        Assert.True(acceptResult is Success<string>, acceptResult switch { Failure f => f.ErrorMessage, _ => "Expected Success" });

        dbContext.ChangeTracker.Clear();
        var relation = await dbContext.TagRelations.FirstOrDefaultAsync(tr => tr.ItemId == targetItemA.Id && tr.TagId == tagB.Id);
        Assert.NotNull(relation);

        var ledger = await dbContext.TagWeightLedgers.FirstOrDefaultAsync(l => l.SourceId == relation.Id);
        Assert.NotNull(ledger);
        Assert.True(ledger.IsOwnerAction);
        Assert.Equal(userB, ledger.OwnerId);
    }

    [Fact]
    public async Task Bounty_NonOwnerFulfillsWithoutAsset_ShouldFail()
    {
        // Arrange: 非オーナー (userC) がアセット指定なしで承認を試みる
        await using var scope = CreateTestScope();
        var (dbContext, service, tid) = scope;

        var userA = $"ua_{tid}";
        var userB = $"ub_{tid}";
        var userC = $"uc_{tid}";
        await dbContext.SeedUsersAsync(userA, userB, userC);

        var targetItemA = new Item { Content = $"Item_{tid}", OwnerId = userA };
        var tagB = new Tag { Name = $"TagB_{tid}", OwnerId = userB };

        dbContext.Items!.Add(targetItemA);
        dbContext.Tags!.Add(tagB);
        await dbContext.SaveChangesAsync();

        var contract = new TaggingRequestEntity
        {
            ContractType = "Bounty",
            RequesterUserId = userA,
            TagOwnerUserId = userB,
            TargetItemId = targetItemA.Id,
            RequestedTagId = tagB.Id,
            OwnerId = userA
        };
        dbContext.TaggingRequestEntities!.Add(contract);
        await dbContext.SaveChangesAsync();

        // Act
        var acceptResult = await service.AcceptContractAsync(contract.Id, userC, fulfillerAssetId: null);

        // Assert
        Assert.True(acceptResult is Failure fail && fail.ErrorMessage.Contains("バウンティを承認するには対象タグの RightAsset が必要です。"));
    }

    [Fact]
    public async Task Bounty_InvalidFulfillerAsset_ShouldFail()
    {
        await using var scope = CreateTestScope();
        var (dbContext, service, tid) = scope;

        var userA = $"ua_{tid}";
        var userB = $"ub_{tid}";
        var userC = $"uc_{tid}";
        var userD = $"ud_{tid}";
        await dbContext.SeedUsersAsync(userA, userB, userC, userD);

        var targetItemA = new Item { Content = $"Item_{tid}", OwnerId = userA };
        var tagB = new Tag { Name = $"TagB_{tid}", OwnerId = userB };
        var otherTag = new Tag { Name = $"OtherTag_{tid}", OwnerId = userB };

        dbContext.Items!.Add(targetItemA);
        dbContext.Tags!.AddRange(tagB, otherTag);
        await dbContext.SaveChangesAsync();

        // 他人所有のアセット
        var wrongOwnerAsset = new RightAsset { TargetTagId = tagB.Id, OwnerId = userD, Amount = 1, IsBurned = false };
        // 消費済みアセット
        var burnedAsset = new RightAsset { TargetTagId = tagB.Id, OwnerId = userC, Amount = 1, IsBurned = true };
        // 別タグのアセット
        var wrongTagAsset = new RightAsset { TargetTagId = otherTag.Id, OwnerId = userC, Amount = 1, IsBurned = false };

        dbContext.RightAssets!.AddRange(wrongOwnerAsset, burnedAsset, wrongTagAsset);
        await dbContext.SaveChangesAsync();

        var contract = new TaggingRequestEntity
        {
            ContractType = "Bounty",
            RequesterUserId = userA,
            TagOwnerUserId = userB,
            TargetItemId = targetItemA.Id,
            RequestedTagId = tagB.Id,
            OwnerId = userA
        };
        dbContext.TaggingRequestEntities!.Add(contract);
        await dbContext.SaveChangesAsync();

        // Case 1: 他人所有
        var res1 = await service.AcceptContractAsync(contract.Id, userC, wrongOwnerAsset.Id);
        Assert.True(res1 is Failure f1 && f1.ErrorMessage.Contains("提供されたアセットが無効または所有していません。"));

        // Case 2: Burn 済み
        var res2 = await service.AcceptContractAsync(contract.Id, userC, burnedAsset.Id);
        Assert.True(res2 is Failure f2 && f2.ErrorMessage.Contains("提供されたアセットが無効または所有していません。"));

        // Case 3: 別タグのアセット
        var res3 = await service.AcceptContractAsync(contract.Id, userC, wrongTagAsset.Id);
        Assert.True(res3 is Failure f3 && f3.ErrorMessage.Contains("提供されたアセットは対象タグの権利ではありません。"));
    }

    [Fact]
    public async Task Bounty_RewardInvalidState_ShouldRollbackAndFail()
    {
        await using var scope = CreateTestScope();
        var (dbContext, service, tid) = scope;

        var userA = $"ua_{tid}";
        var userB = $"ub_{tid}";
        var userC = $"uc_{tid}";
        await dbContext.SeedUsersAsync(userA, userB, userC);

        var targetItemA = new Item { Content = $"Item_{tid}", OwnerId = userA };
        var tagB = new Tag { Name = $"TagB_{tid}", OwnerId = userB };
        var tagC = new Tag { Name = $"RewardTag_{tid}", OwnerId = userA };

        dbContext.Items!.Add(targetItemA);
        dbContext.Tags!.AddRange(tagB, tagC);
        await dbContext.SaveChangesAsync();

        var rewardAsset = new RightAsset { Amount = 1, OwnerId = userA, TargetTagId = tagC.Id, IsBurned = false };
        var fulfillerAsset = new RightAsset { Amount = 1, OwnerId = userC, TargetTagId = tagB.Id, IsBurned = false };
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
            OwnerId = userA
        };
        dbContext.TaggingRequestEntities!.Add(contract);
        await dbContext.SaveChangesAsync();

        // 契約作成後に報酬アセットが Burn されて無効化
        rewardAsset.IsBurned = true;
        await dbContext.SaveChangesAsync();

        // Act
        var acceptResult = await service.AcceptContractAsync(contract.Id, userC, fulfillerAsset.Id);

        // Assert: 報酬アセット無効で失敗し、ロールバックされること
        Assert.True(acceptResult is Failure fail && fail.ErrorMessage.Contains("約束された報酬アセットが無効になっています。"));

        dbContext.ChangeTracker.Clear();
        // リレーションは作成されていないこと（ロールバック確認）
        var relationExists = await dbContext.TagRelations.AnyAsync(tr => tr.ItemId == targetItemA.Id && tr.TagId == tagB.Id);
        Assert.False(relationExists);
    }

    [Fact]
    public async Task BountyRemove_WhenRelationExists_ShouldRemoveRelationAndTransferReward()
    {
        await using var scope = CreateTestScope();
        var (dbContext, service, tid) = scope;

        var userA = $"ua_{tid}";
        var userB = $"ub_{tid}";
        var userC = $"uc_{tid}";
        await dbContext.SeedUsersAsync(userA, userB, userC);

        var targetItemA = new Item { Content = $"Item_{tid}", OwnerId = userA };
        var tagB = new Tag { Name = $"TagB_{tid}", OwnerId = userB, CachedWeight = 10 };
        var tagReward = new Tag { Name = $"RewardTag_{tid}", OwnerId = userA };

        dbContext.Items!.Add(targetItemA);
        dbContext.Tags!.AddRange(tagB, tagReward);
        await dbContext.SaveChangesAsync();

        // 削除対象の relation が存在
        var relation = new TagRelation { ItemId = targetItemA.Id, TagId = tagB.Id, OwnerId = userB, Weight = 4 };
        dbContext.TagRelations!.Add(relation);

        var rewardAsset = new RightAsset { Amount = 1, OwnerId = userA, TargetTagId = tagReward.Id, IsBurned = false };
        var fulfillerAsset = new RightAsset { Amount = 1, OwnerId = userC, TargetTagId = tagB.Id, IsBurned = false };
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
            OwnerId = userA,
            RequestType = TaggingRequestType.Remove
        };
        dbContext.TaggingRequestEntities!.Add(contract);
        await dbContext.SaveChangesAsync();

        // Act
        var acceptResult = await service.AcceptContractAsync(contract.Id, userC, fulfillerAsset.Id);

        // Assert
        Assert.True(acceptResult is Success<string>, acceptResult switch { Failure f => f.ErrorMessage, _ => "Expected Success" });

        dbContext.ChangeTracker.Clear();
        // リレーションが削除されていること
        var relationExists = await dbContext.TagRelations.AnyAsync(tr => tr.ItemId == targetItemA.Id && tr.TagId == tagB.Id);
        Assert.False(relationExists);

        // 報酬アセットの所有権が userC に移転していること
        var updatedReward = await dbContext.RightAssets.FindAsync(rewardAsset.Id);
        Assert.NotNull(updatedReward);
        Assert.Equal(userC, updatedReward.OwnerId);

        // CachedWeight が 4 減少していること
        var updatedTag = await dbContext.Tags.FindAsync(tagB.Id);
        Assert.Equal(6, updatedTag!.CachedWeight); // 10 - 4 = 6
    }

    [Fact]
    public async Task BountyRemove_WhenRelationDoesNotExist_ShouldEarlyExitWithSuccess()
    {
        await using var scope = CreateTestScope();
        var (dbContext, service, tid) = scope;

        var userA = $"ua_{tid}";
        var userB = $"ub_{tid}";
        var userC = $"uc_{tid}";
        await dbContext.SeedUsersAsync(userA, userB, userC);

        var targetItemA = new Item { Content = $"Item_{tid}", OwnerId = userA };
        var tagB = new Tag { Name = $"TagB_{tid}", OwnerId = userB, CachedWeight = 10 };
        var tagReward = new Tag { Name = $"RewardTag_{tid}", OwnerId = userA };

        dbContext.Items!.Add(targetItemA);
        dbContext.Tags!.AddRange(tagB, tagReward);
        await dbContext.SaveChangesAsync();

        var rewardAsset = new RightAsset { Amount = 1, OwnerId = userA, TargetTagId = tagReward.Id, IsBurned = false };
        var fulfillerAsset = new RightAsset { Amount = 1, OwnerId = userC, TargetTagId = tagB.Id, IsBurned = false };
        dbContext.RightAssets!.AddRange(rewardAsset, fulfillerAsset);
        await dbContext.SaveChangesAsync();

        // relation は存在しない
        var contract = new TaggingRequestEntity
        {
            ContractType = "Bounty",
            RequesterUserId = userA,
            TagOwnerUserId = userB,
            TargetItemId = targetItemA.Id,
            RequestedTagId = tagB.Id,
            Payload = new BountyPayload(rewardAsset.Id),
            OwnerId = userA,
            RequestType = TaggingRequestType.Remove
        };
        dbContext.TaggingRequestEntities!.Add(contract);
        await dbContext.SaveChangesAsync();

        // Act
        var acceptResult = await service.AcceptContractAsync(contract.Id, userC, fulfillerAsset.Id);

        // Assert: "削除対象が存在しません。" で早期正常終了
        Assert.True(acceptResult is Success<string>, acceptResult switch { Failure f => f.ErrorMessage, _ => "Expected Success" });
        var msg = acceptResult switch { Success<string> s => s.Value, _ => null };
        Assert.Equal("削除対象が存在しません。", msg);

        dbContext.ChangeTracker.Clear();
        // 早期終了のため報酬は移転されず userA のまま
        var updatedReward = await dbContext.RightAssets.FindAsync(rewardAsset.Id);
        Assert.NotNull(updatedReward);
        Assert.Equal(userA, updatedReward.OwnerId);

        // CachedWeight は不変
        var updatedTag = await dbContext.Tags.FindAsync(tagB.Id);
        Assert.Equal(10, updatedTag!.CachedWeight);
    }

    [Fact]
    public async Task Bounty_WhenFulfillerAssetHasSurplusAmount_ShouldDeductRequiredAndKeepSurplus()
    {
        // Arrange: 要求量 2 に対して残高 5 のアセットを指定してバウンティ承認
        await using var scope = CreateTestScope();
        var (dbContext, service, tid) = scope;

        var userA = $"ua_{tid}";
        var userB = $"ub_{tid}";
        var userC = $"uc_{tid}";
        await dbContext.SeedUsersAsync(userA, userB, userC);

        var targetItemA = new Item { Content = $"TargetItemA_{tid}", OwnerId = userA };
        var tagB = new Tag { Name = $"TagB_{tid}", OwnerId = userB, CachedWeight = 10 };
        var tagC = new Tag { Name = $"TagC_{tid}", OwnerId = userA };
        dbContext.Items.Add(targetItemA);
        dbContext.Tags.AddRange(tagB, tagC);
        await dbContext.SaveChangesAsync();

        var rewardAsset = new RightAsset { Amount = 1, OwnerId = userA, TargetTagId = tagC.Id, IsBurned = false };
        var fulfillerAsset = new RightAsset { Amount = 5, OwnerId = userC, TargetTagId = tagB.Id, IsBurned = false };
        dbContext.RightAssets.AddRange(rewardAsset, fulfillerAsset);
        await dbContext.SaveChangesAsync();

        var contract = new TaggingRequestEntity
        {
            ContractType = "Bounty",
            RequesterUserId = userA,
            TagOwnerUserId = userB,
            TargetItemId = targetItemA.Id,
            RequestedTagId = tagB.Id,
            ProposedWeight = 2,
            Payload = new BountyPayload(rewardAsset.Id),
            OwnerId = userA
        };
        dbContext.TaggingRequestEntities.Add(contract);
        await dbContext.SaveChangesAsync();

        // Act
        var acceptResult = await service.AcceptContractAsync(contract.Id, userC, fulfillerAsset.Id);

        // Assert: 承認成功し、5 - 2 = 3 が手元に残り、IsBurned == false
        Assert.True(acceptResult is Success<string>, acceptResult switch { Failure f => f.ErrorMessage, _ => "Expected Success" });

        dbContext.ChangeTracker.Clear();
        var updatedFulfillerAsset = await dbContext.RightAssets.FindAsync(fulfillerAsset.Id);
        Assert.NotNull(updatedFulfillerAsset);
        Assert.False(updatedFulfillerAsset!.IsBurned);
        Assert.Equal(3, updatedFulfillerAsset.Amount);
    }

    [Fact]
    public async Task Bounty_WhenFulfillerAssetHasInsufficientAmount_ShouldReturnFailure()
    {
        // Arrange: 要求量 2 に対して残高 1 のアセットを指定
        await using var scope = CreateTestScope();
        var (dbContext, service, tid) = scope;

        var userA = $"ua_{tid}";
        var userB = $"ub_{tid}";
        var userC = $"uc_{tid}";
        await dbContext.SeedUsersAsync(userA, userB, userC);

        var targetItemA = new Item { Content = $"TargetItemA_{tid}", OwnerId = userA };
        var tagB = new Tag { Name = $"TagB_{tid}", OwnerId = userB, CachedWeight = 10 };
        var tagC = new Tag { Name = $"TagC_{tid}", OwnerId = userA };
        dbContext.Items.Add(targetItemA);
        dbContext.Tags.AddRange(tagB, tagC);
        await dbContext.SaveChangesAsync();

        var rewardAsset = new RightAsset { Amount = 1, OwnerId = userA, TargetTagId = tagC.Id, IsBurned = false };
        var fulfillerAsset = new RightAsset { Amount = 1, OwnerId = userC, TargetTagId = tagB.Id, IsBurned = false };
        dbContext.RightAssets.AddRange(rewardAsset, fulfillerAsset);
        await dbContext.SaveChangesAsync();

        var contract = new TaggingRequestEntity
        {
            ContractType = "Bounty",
            RequesterUserId = userA,
            TagOwnerUserId = userB,
            TargetItemId = targetItemA.Id,
            RequestedTagId = tagB.Id,
            ProposedWeight = 2,
            Payload = new BountyPayload(rewardAsset.Id),
            OwnerId = userA
        };
        dbContext.TaggingRequestEntities.Add(contract);
        await dbContext.SaveChangesAsync();

        // Act
        var acceptResult = await service.AcceptContractAsync(contract.Id, userC, fulfillerAsset.Id);

        // Assert
        Assert.True(acceptResult is Failure);
        var fail = acceptResult switch { Failure f => f, _ => throw new InvalidOperationException() };
        Assert.Equal("提供されたアセットの残量が不足しています。", fail.ErrorMessage);
    }

    [Fact]
    public async Task Bounty_SelfFulfillment_ShouldBeRejected()
    {
        // Arrange: 依頼者自身によるバウンティ達成は拒否される
        await using var scope = CreateTestScope();
        var (dbContext, service, tid) = scope;

        var userA = $"ua_{tid}";
        var userB = $"ub_{tid}";
        await dbContext.SeedUsersAsync(userA, userB);

        var targetItemA = new Item { Content = $"TargetItemA_{tid}", OwnerId = userA };
        var tagB = new Tag { Name = $"TagB_{tid}", OwnerId = userB, CachedWeight = 10 };
        dbContext.Items.Add(targetItemA);
        dbContext.Tags.Add(tagB);
        await dbContext.SaveChangesAsync();

        var fulfillerAsset = new RightAsset { Amount = 1, OwnerId = userA, TargetTagId = tagB.Id, IsBurned = false };
        dbContext.RightAssets.Add(fulfillerAsset);
        await dbContext.SaveChangesAsync();

        var contract = new TaggingRequestEntity
        {
            ContractType = "Bounty",
            RequesterUserId = userA,
            TagOwnerUserId = userB,
            TargetItemId = targetItemA.Id,
            RequestedTagId = tagB.Id,
            Payload = new BountyPayload(0),
            OwnerId = userA
        };
        dbContext.TaggingRequestEntities.Add(contract);
        await dbContext.SaveChangesAsync();

        // Act: 依頼者自身が達成を試みる
        var acceptResult = await service.AcceptContractAsync(contract.Id, userA, fulfillerAsset.Id);

        // Assert
        Assert.True(acceptResult is Failure);
        var fail = acceptResult switch { Failure f => f, _ => throw new InvalidOperationException() };
        Assert.Equal("自身が作成したバウンティは達成できません。", fail.ErrorMessage);
    }
}