using Microsoft.EntityFrameworkCore;

using Moq;

using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;

namespace SRNSMudApp.Tests.Services;

public class ContractDataProviderTests : IAsyncLifetime
{
    private MsSqlTestDatabase _sharedDb = null!;

    public async Task InitializeAsync()
    {
        _sharedDb = await SharedMsSqlTestDatabase.GetInstanceAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private (ApplicationDbContext dbContext, ContractDataProvider provider, string tid) CreateScope()
    {
        var tid = Guid.NewGuid().ToString("N")[..8];
        var ctx = new ApplicationDbContext(_sharedDb.Options);
        var mockDbFactory = new Mock<IDbContextFactory<ApplicationDbContext>>();
        mockDbFactory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new ApplicationDbContext(_sharedDb.Options));
        var provider = new ContractDataProvider(mockDbFactory.Object);
        return (ctx, provider, tid);
    }

    [Fact]
    public async Task DeactivatePublicOfferAsync_WhenOwner_ShouldDeactivateAndReturnTrue()
    {
        // Arrange
        var (dbContext, provider, tid) = CreateScope();
        await using (dbContext)
        {
            var ownerId = $"owner_{tid}";
            await dbContext.SeedUsersAsync(ownerId);

            var tag = new Tag { Name = $"OfferTag_{tid}", OwnerId = ownerId };
            dbContext.Tags.Add(tag);
            await dbContext.SaveChangesAsync();

            var offer = new PublicTradeOffer
            {
                OwnerId = ownerId,
                OfferedTagId = tag.Id,
                RequiredAssetAmount = 1,
                IsActive = true
            };
            dbContext.PublicTradeOffers.Add(offer);
            await dbContext.SaveChangesAsync();

            // Act
            var result = await provider.DeactivatePublicOfferAsync(offer.Id, ownerId);

            // Assert
            Assert.True(result);
            dbContext.ChangeTracker.Clear();
            var updated = await dbContext.PublicTradeOffers.FindAsync(offer.Id);
            Assert.NotNull(updated);
            Assert.False(updated!.IsActive);
        }
    }

    [Fact]
    public async Task DeactivatePublicOfferAsync_WhenNotOwner_ShouldReturnFalseAndRemainActive()
    {
        // Arrange
        var (dbContext, provider, tid) = CreateScope();
        await using (dbContext)
        {
            var ownerId = $"owner_{tid}";
            var otherUserId = $"other_{tid}";
            await dbContext.SeedUsersAsync(ownerId, otherUserId);

            var tag = new Tag { Name = $"OfferTag_{tid}", OwnerId = ownerId };
            dbContext.Tags.Add(tag);
            await dbContext.SaveChangesAsync();

            var offer = new PublicTradeOffer
            {
                OwnerId = ownerId,
                OfferedTagId = tag.Id,
                RequiredAssetAmount = 1,
                IsActive = true
            };
            dbContext.PublicTradeOffers.Add(offer);
            await dbContext.SaveChangesAsync();

            // Act
            var result = await provider.DeactivatePublicOfferAsync(offer.Id, otherUserId);

            // Assert
            Assert.False(result);
            dbContext.ChangeTracker.Clear();
            var updated = await dbContext.PublicTradeOffers.FindAsync(offer.Id);
            Assert.NotNull(updated);
            Assert.True(updated!.IsActive);
        }
    }

    [Fact]
    public async Task DeactivatePublicOfferAsync_WhenOfferNotFound_ShouldReturnFalse()
    {
        // Arrange
        var (dbContext, provider, _) = CreateScope();
        await using (dbContext)
        {
            // Act
            var result = await provider.DeactivatePublicOfferAsync(999999, "any_user");

            // Assert
            Assert.False(result);
        }
    }

    [Fact]
    public async Task GetValidRightAssetsAsync_ShouldFilterByTagAndMinAmount_AndExcludeBurnedOrOtherUsers()
    {
        // Arrange
        var (dbContext, provider, tid) = CreateScope();
        await using (dbContext)
        {
            var userId = $"user_{tid}";
            var otherUserId = $"other_{tid}";
            await dbContext.SeedUsersAsync(userId, otherUserId);

            var tag1 = new Tag { Name = $"Tag1_{tid}", OwnerId = userId };
            var tag2 = new Tag { Name = $"Tag2_{tid}", OwnerId = userId };
            dbContext.Tags.AddRange(tag1, tag2);
            await dbContext.SaveChangesAsync();

            var asset1 = new RightAsset { OwnerId = userId, TargetTagId = tag1.Id, Amount = 10, IsBurned = false };
            var asset2LowAmount = new RightAsset { OwnerId = userId, TargetTagId = tag1.Id, Amount = 2, IsBurned = false };
            var asset3Burned = new RightAsset { OwnerId = userId, TargetTagId = tag1.Id, Amount = 10, IsBurned = true };
            var asset4Tag2 = new RightAsset { OwnerId = userId, TargetTagId = tag2.Id, Amount = 10, IsBurned = false };
            var asset5OtherUser = new RightAsset { OwnerId = otherUserId, TargetTagId = tag1.Id, Amount = 10, IsBurned = false };
            dbContext.RightAssets.AddRange(asset1, asset2LowAmount, asset3Burned, asset4Tag2, asset5OtherUser);
            await dbContext.SaveChangesAsync();

            // Act 1: フィルタなし（ユーザーの非Burn全件）
            var allMyAssets = await provider.GetValidRightAssetsAsync(userId);
            Assert.Equal(3, allMyAssets.Count); // asset1, asset2LowAmount, asset4Tag2

            // Act 2: tag1 のみ
            var tag1Assets = await provider.GetValidRightAssetsAsync(userId, targetTagId: tag1.Id);
            Assert.Equal(2, tag1Assets.Count); // asset1, asset2LowAmount

            // Act 3: tag1 かつ minAmount = 5
            var filteredAssets = await provider.GetValidRightAssetsAsync(userId, targetTagId: tag1.Id, minAmount: 5);
            Assert.Single(filteredAssets);
            Assert.Equal(asset1.Id, filteredAssets[0].Id);
        }
    }

    [Fact]
    public async Task GetContractsAsync_ShouldSeparateIncomingAndOutgoingContracts()
    {
        // Arrange
        var (dbContext, provider, tid) = CreateScope();
        await using (dbContext)
        {
            var userA = $"userA_{tid}";
            var userB = $"userB_{tid}";
            await dbContext.SeedUsersAsync(userA, userB);

            var itemA = new Item { Content = $"ItemA_{tid}", OwnerId = userA };
            var itemB = new Item { Content = $"ItemB_{tid}", OwnerId = userB };
            var tagA = new Tag { Name = $"TagA_{tid}", OwnerId = userA };
            var tagB = new Tag { Name = $"TagB_{tid}", OwnerId = userB };
            dbContext.Items.AddRange(itemA, itemB);
            dbContext.Tags.AddRange(tagA, tagB);
            await dbContext.SaveChangesAsync();

            // Contract 1: Requester = userA, TagOwner = userB, Proposed -> userA から見て Outgoing, userB から見て Incoming
            var c1 = new TaggingRequestEntity
            {
                ContractType = "Gratis",
                RequesterUserId = userA,
                TagOwnerUserId = userB,
                OwnerId = userA,
                TargetItemId = itemA.Id,
                TargetId = itemA.TagTargetId,
                RequestedTagId = tagB.Id,
                Payload = new GratisPayload("c1")
            };

            // Contract 2: Requester = userB, TagOwner = userA, Proposed -> userA から見て Incoming, userB から見て Outgoing
            var c2 = new TaggingRequestEntity
            {
                ContractType = "Gratis",
                RequesterUserId = userB,
                TagOwnerUserId = userA,
                OwnerId = userB,
                TargetItemId = itemB.Id,
                TargetId = itemB.TagTargetId,
                RequestedTagId = tagA.Id,
                Payload = new GratisPayload("c2")
            };

            // Contract 3: Executed の契約 -> Incoming には含まれない（Status == Proposed のみ）
            var c3 = new TaggingRequestEntity
            {
                ContractType = "Gratis",
                RequesterUserId = userB,
                TagOwnerUserId = userA,
                OwnerId = userB,
                TargetItemId = itemB.Id,
                TargetId = itemB.TagTargetId,
                RequestedTagId = tagA.Id,
                Payload = new GratisPayload("c3")
            };
            c3.Execute();

            dbContext.TaggingRequestEntities.AddRange(c1, c2, c3);
            await dbContext.SaveChangesAsync();

            // Act
            var userAData = await provider.GetContractsAsync(userA);

            // Assert
            // userA の Incoming: c2 のみ (c3 は Executed なので除外)
            Assert.Single(userAData.IncomingContracts);
            Assert.Equal(c2.Id, userAData.IncomingContracts[0].Id);

            // userA の Outgoing: c1 のみ
            Assert.Single(userAData.OutgoingContracts);
            Assert.Equal(c1.Id, userAData.OutgoingContracts[0].Id);
        }
    }

    [Fact]
    public async Task GetActiveBountiesAsync_ShouldReturnProposedBountiesWithAssets()
    {
        // Arrange
        var (dbContext, provider, tid) = CreateScope();
        await using (dbContext)
        {
            var ownerId = $"bounty_owner_{tid}";
            await dbContext.SeedUsersAsync(ownerId);

            var item = new Item { Content = $"BountyItem_{tid}", OwnerId = ownerId };
            var requestedTag = new Tag { Name = $"ReqTag_{tid}", OwnerId = ownerId };
            var rewardTag = new Tag { Name = $"RewardTag_{tid}", OwnerId = ownerId };
            dbContext.Items.Add(item);
            dbContext.Tags.AddRange(requestedTag, rewardTag);
            await dbContext.SaveChangesAsync();

            var rewardAsset = new RightAsset { OwnerId = ownerId, TargetTagId = rewardTag.Id, Amount = 5 };
            dbContext.RightAssets.Add(rewardAsset);
            await dbContext.SaveChangesAsync();

            var proposedBounty = new TaggingRequestEntity
            {
                ContractType = "Bounty",
                RequesterUserId = ownerId,
                TagOwnerUserId = ownerId,
                OwnerId = ownerId,
                TargetItemId = item.Id,
                TargetId = item.TagTargetId,
                RequestedTagId = requestedTag.Id,
                Payload = new BountyPayload(rewardAsset.Id)
            };

            var executedBounty = new TaggingRequestEntity
            {
                ContractType = "Bounty",
                RequesterUserId = ownerId,
                TagOwnerUserId = ownerId,
                OwnerId = ownerId,
                TargetItemId = item.Id,
                TargetId = item.TagTargetId,
                RequestedTagId = requestedTag.Id,
                Payload = new BountyPayload(rewardAsset.Id)
            };
            executedBounty.Execute();

            dbContext.TaggingRequestEntities.AddRange(proposedBounty, executedBounty);
            await dbContext.SaveChangesAsync();

            // Act
            var data = await provider.GetActiveBountiesAsync();

            // Assert
            Assert.Contains(data.Bounties, b => b.Id == proposedBounty.Id);
            Assert.DoesNotContain(data.Bounties, b => b.Id == executedBounty.Id);
            Assert.True(data.RewardAssets.ContainsKey(rewardAsset.Id));
            Assert.Equal(5, data.RewardAssets[rewardAsset.Id].Amount);
        }
    }
}