using Microsoft.EntityFrameworkCore;

using Moq;

using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services.Commands;

namespace SRNSMudApp.Tests.Services;

public class ContractCommandsTests : IAsyncLifetime
{
    private MsSqlTestDatabase _sharedDb = null!;

    public async Task InitializeAsync()
    {
        _sharedDb = await SharedMsSqlTestDatabase.GetInstanceAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private (ApplicationDbContext dbContext, IDbContextFactory<ApplicationDbContext> dbFactory, string tid) CreateScope()
    {
        var tid = Guid.NewGuid().ToString("N")[..8];
        var ctx = new ApplicationDbContext(_sharedDb.Options);
        var mockDbFactory = new Mock<IDbContextFactory<ApplicationDbContext>>();
        mockDbFactory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new ApplicationDbContext(_sharedDb.Options));
        return (ctx, mockDbFactory.Object, tid);
    }

    [Fact]
    public async Task CreatePublicOfferCommandHandler_ShouldPersistOffer()
    {
        // Arrange
        var (dbContext, dbFactory, tid) = CreateScope();
        await using (dbContext)
        {
            var userId = $"user_{tid}";
            await dbContext.SeedUsersAsync(userId);

            var tag = new Tag { Name = $"OfferTag_{tid}", OwnerId = userId };
            dbContext.Tags.Add(tag);
            await dbContext.SaveChangesAsync();

            var offer = new PublicTradeOffer
            {
                OwnerId = userId,
                OfferedTagId = tag.Id,
                RequiredAssetAmount = 2,
                IsActive = true
            };

            var handler = new CreatePublicOfferCommandHandler(dbFactory);

            // Act
            var result = await handler.HandleAsync(new CreatePublicOfferCommand(offer));

            // Assert
            Assert.True(result is Success<bool>);
            dbContext.ChangeTracker.Clear();
            var saved = await dbContext.PublicTradeOffers.FindAsync(offer.Id);
            Assert.NotNull(saved);
            Assert.Equal(userId, saved!.OwnerId);
            Assert.Equal(tag.Id, saved.OfferedTagId);
            Assert.Equal(2, saved.RequiredAssetAmount);
        }
    }

    [Fact]
    public async Task CreateBountyCommandHandler_ShouldPersistBounty()
    {
        // Arrange
        var (dbContext, dbFactory, tid) = CreateScope();
        await using (dbContext)
        {
            var userId = $"user_{tid}";
            await dbContext.SeedUsersAsync(userId);

            var item = new Item { Content = $"BountyItem_{tid}", OwnerId = userId };
            var tag = new Tag { Name = $"Tag_{tid}", OwnerId = userId };
            dbContext.Items.Add(item);
            dbContext.Tags.Add(tag);
            await dbContext.SaveChangesAsync();

            var asset = new RightAsset
            {
                TargetTagId = tag.Id,
                OwnerId = userId,
                Amount = 10,
                IsBurned = false,
                CreatedDate = DateTime.UtcNow
            };
            dbContext.RightAssets.Add(asset);
            await dbContext.SaveChangesAsync();

            var bounty = new TaggingRequestEntity
            {
                ContractType = "Bounty",
                RequesterUserId = userId,
                TagOwnerUserId = userId,
                OwnerId = userId,
                TargetItemId = item.Id,
                TargetId = item.TagTargetId,
                RequestedTagId = tag.Id,
                Payload = new BountyPayload(asset.Id)
            };

            var handler = new CreateBountyCommandHandler(dbFactory);

            // Act
            var result = await handler.HandleAsync(new CreateBountyCommand(bounty));

            // Assert
            Assert.True(result is Success<bool>);
            dbContext.ChangeTracker.Clear();
            var saved = await dbContext.TaggingRequestEntities.FindAsync(bounty.Id);
            Assert.NotNull(saved);
            Assert.Equal("Bounty", saved!.ContractType);
        }
    }

    [Fact]
    public async Task CreateTriggerContractCommandHandler_ShouldPersistTriggerContract()
    {
        // Arrange
        var (dbContext, dbFactory, tid) = CreateScope();
        await using (dbContext)
        {
            var userId = $"user_{tid}";
            await dbContext.SeedUsersAsync(userId);

            var item = new Item { Content = $"Item_{tid}", OwnerId = userId };
            var tag = new Tag { Name = $"Tag_{tid}", OwnerId = userId };
            dbContext.Items.Add(item);
            dbContext.Tags.Add(tag);
            await dbContext.SaveChangesAsync();

            var triggerContract = new TaggingRequestEntity
            {
                ContractType = "Trigger",
                RequesterUserId = userId,
                TagOwnerUserId = userId,
                OwnerId = userId,
                TargetItemId = item.Id,
                TargetId = item.TagTargetId,
                RequestedTagId = tag.Id,
                Payload = new PublicOfferPayload(200)
            };

            var handler = new CreateTriggerContractCommandHandler(dbFactory);

            // Act
            var result = await handler.HandleAsync(new CreateTriggerContractCommand(triggerContract));

            // Assert
            Assert.True(result is Success<bool>);
            dbContext.ChangeTracker.Clear();
            var saved = await dbContext.TaggingRequestEntities.FindAsync(triggerContract.Id);
            Assert.NotNull(saved);
            Assert.Equal("Trigger", saved!.ContractType);
        }
    }

    [Fact]
    public async Task CreateBountyCommandHandler_WhenAssetNotOwned_ReturnsFailure()
    {
        // Arrange
        var (dbContext, dbFactory, tid) = CreateScope();
        await using (dbContext)
        {
            var ownerId = $"owner_{tid}";
            var attackerId = $"attacker_{tid}";
            await dbContext.SeedUsersAsync(ownerId, attackerId);

            var item = new Item { Content = $"BountyItem_{tid}", OwnerId = attackerId };
            var tag = new Tag { Name = $"Tag_{tid}", OwnerId = ownerId };
            dbContext.Items.Add(item);
            dbContext.Tags.Add(tag);
            await dbContext.SaveChangesAsync();

            var otherUserAsset = new RightAsset
            {
                TargetTagId = tag.Id,
                OwnerId = ownerId,
                Amount = 10,
                IsBurned = false,
                CreatedDate = DateTime.UtcNow
            };
            dbContext.RightAssets.Add(otherUserAsset);
            await dbContext.SaveChangesAsync();

            var bounty = new TaggingRequestEntity
            {
                ContractType = "Bounty",
                RequesterUserId = attackerId,
                TagOwnerUserId = ownerId,
                OwnerId = attackerId,
                TargetItemId = item.Id,
                TargetId = item.TagTargetId,
                RequestedTagId = tag.Id,
                Payload = new BountyPayload(otherUserAsset.Id)
            };

            var handler = new CreateBountyCommandHandler(dbFactory);

            // Act
            var result = await handler.HandleAsync(new CreateBountyCommand(bounty));

            // Assert
            Assert.True(result is Failure f && f.ErrorMessage.Contains("所有していないアセット"));
        }
    }

    [Fact]
    public async Task CreateBountyCommandHandler_WhenAssetAlreadyPledged_ReturnsFailure()
    {
        // Arrange
        var (dbContext, dbFactory, tid) = CreateScope();
        await using (dbContext)
        {
            var userId = $"user_{tid}";
            await dbContext.SeedUsersAsync(userId);

            var item = new Item { Content = $"BountyItem_{tid}", OwnerId = userId };
            var tag = new Tag { Name = $"Tag_{tid}", OwnerId = userId };
            dbContext.Items.Add(item);
            dbContext.Tags.Add(tag);
            await dbContext.SaveChangesAsync();

            var asset = new RightAsset
            {
                TargetTagId = tag.Id,
                OwnerId = userId,
                Amount = 10,
                IsBurned = false,
                CreatedDate = DateTime.UtcNow
            };
            dbContext.RightAssets.Add(asset);
            await dbContext.SaveChangesAsync();

            var existingBounty = new TaggingRequestEntity
            {
                ContractType = "Bounty",
                RequesterUserId = userId,
                TagOwnerUserId = userId,
                OwnerId = userId,
                TargetItemId = item.Id,
                TargetId = item.TagTargetId,
                RequestedTagId = tag.Id,
                Payload = new BountyPayload(asset.Id)
            };
            dbContext.TaggingRequestEntities.Add(existingBounty);
            await dbContext.SaveChangesAsync();

            var newBounty = new TaggingRequestEntity
            {
                ContractType = "Bounty",
                RequesterUserId = userId,
                TagOwnerUserId = userId,
                OwnerId = userId,
                TargetItemId = item.Id,
                TargetId = item.TagTargetId,
                RequestedTagId = tag.Id,
                Payload = new BountyPayload(asset.Id)
            };

            var handler = new CreateBountyCommandHandler(dbFactory);

            // Act
            var result = await handler.HandleAsync(new CreateBountyCommand(newBounty));

            // Assert
            Assert.True(result is Failure f && f.ErrorMessage.Contains("既に別のアクティブなバウンティに設定"));
        }
    }

    [Fact]
    public async Task CreatePublicOfferCommandHandler_WhenUserNotTagOwner_ReturnsFailure()
    {
        // Arrange
        var (dbContext, dbFactory, tid) = CreateScope();
        await using (dbContext)
        {
            var ownerId = $"owner_{tid}";
            var attackerId = $"attacker_{tid}";
            await dbContext.SeedUsersAsync(ownerId, attackerId);

            var tag = new Tag { Name = $"Tag_{tid}", OwnerId = ownerId };
            dbContext.Tags.Add(tag);
            await dbContext.SaveChangesAsync();

            var offer = new PublicTradeOffer
            {
                OwnerId = attackerId,
                OfferedTagId = tag.Id,
                RequiredAssetAmount = 5,
                IsActive = true
            };

            var handler = new CreatePublicOfferCommandHandler(dbFactory);

            // Act
            var result = await handler.HandleAsync(new CreatePublicOfferCommand(offer));

            // Assert
            Assert.True(result is Failure f && f.ErrorMessage.Contains("自身が所有していないタグ"));
        }
    }

    [Fact]
    public async Task CreatePublicOfferCommandHandler_WhenNegativeAmount_ReturnsFailure()
    {
        // Arrange
        var (dbContext, dbFactory, tid) = CreateScope();
        await using (dbContext)
        {
            var userId = $"user_{tid}";
            await dbContext.SeedUsersAsync(userId);

            var tag = new Tag { Name = $"Tag_{tid}", OwnerId = userId };
            dbContext.Tags.Add(tag);
            await dbContext.SaveChangesAsync();

            var offer = new PublicTradeOffer
            {
                OwnerId = userId,
                OfferedTagId = tag.Id,
                RequiredAssetAmount = -1,
                IsActive = true
            };

            var handler = new CreatePublicOfferCommandHandler(dbFactory);

            // Act
            var result = await handler.HandleAsync(new CreatePublicOfferCommand(offer));

            // Assert
            Assert.True(result is Failure f && f.ErrorMessage.Contains("0以上を指定してください"));
        }
    }
}