using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;

namespace SRNSMudApp.Tests;

public class TaggingContractProposeTests : TaggingContractTestBase
{
    [Fact]
    public async Task ProposeGratisContractAsync_ShouldCreateGratisContractInProposedStatus()
    {
        // Arrange
        await using var scope = CreateTestScope();
        var (dbContext, service, tid) = scope;

        var requesterId = $"req_{tid}";
        var tagOwnerId = $"owner_{tid}";
        await dbContext.SeedUsersAsync(requesterId, tagOwnerId);

        var message = "Please give me this tag!";
        var targetItem = new Item { Content = $"TargetItem_{tid}", OwnerId = requesterId };
        var tag = new Tag { Name = $"Tag_{tid}", OwnerId = tagOwnerId };
        dbContext.Items.Add(targetItem);
        dbContext.Tags.Add(tag);
        await dbContext.SaveChangesAsync();

        // Act
        var result = await service.ProposeGratisContractAsync(
            requesterId, tagOwnerId, targetItem.Id, tag.Id, message: message);
        Assert.True(result is Success<TaggingRequestEntity>);
        var contract = result switch
        {
            Success<TaggingRequestEntity> s => s.Value,
            _ => throw new InvalidOperationException("Expected Success")
        };

        // Assert
        Assert.NotNull(contract);
        Assert.Equal(requesterId, contract.RequesterUserId);
        Assert.Equal(requesterId, contract.OwnerId);
        Assert.Equal(tagOwnerId, contract.TagOwnerUserId);
        Assert.Equal(targetItem.Id, contract.TargetItemId);
        Assert.Equal(tag.Id, contract.RequestedTagId);
        Assert.Equal(TradeStatus.Proposed, contract.Status);
        Assert.True(contract.Payload is GratisPayload p && p.RequesterMessage == message);

        TaggingRequestEntity? saved = await dbContext.TaggingRequestEntities
            .FirstOrDefaultAsync(c => c.Id == contract.Id);
        Assert.NotNull(saved);
    }

    [Fact]
    public async Task ProposeMutualContractAsync_ShouldCreateMutualContractInProposedStatus()
    {
        // Arrange
        await using var scope = CreateTestScope();
        var (dbContext, service, tid) = scope;

        var requesterId = $"req_{tid}";
        var tagOwnerId = $"owner_{tid}";
        await dbContext.SeedUsersAsync(requesterId, tagOwnerId);

        var targetItem1 = new Item { Content = $"TargetItem1_{tid}", OwnerId = requesterId };
        var targetItem2 = new Item { Content = $"TargetItem2_{tid}", OwnerId = tagOwnerId };
        var tag1 = new Tag { Name = $"Tag1_{tid}", OwnerId = tagOwnerId };
        var tag2 = new Tag { Name = $"Tag2_{tid}", OwnerId = requesterId };
        dbContext.Items.AddRange(targetItem1, targetItem2);
        dbContext.Tags.AddRange(tag1, tag2);
        await dbContext.SaveChangesAsync();

        var rightAsset = new RightAsset { Amount = 1, OwnerId = requesterId, TargetTagId = tag1.Id };
        dbContext.RightAssets.Add(rightAsset);
        await dbContext.SaveChangesAsync();

        // Act
        var result = await service.ProposeMutualContractAsync(
            requesterId, tagOwnerId, targetItem1.Id, tag1.Id, targetItem2.Id, tag2.Id, rightAsset.Id);
        Assert.True(result is Success<TaggingRequestEntity>);
        var contract = result switch
        {
            Success<TaggingRequestEntity> s => s.Value,
            _ => throw new InvalidOperationException("Expected Success")
        };

        // Assert
        Assert.NotNull(contract);
        Assert.Equal(requesterId, contract.RequesterUserId);
        Assert.Equal(tagOwnerId, contract.TagOwnerUserId);
        Assert.Equal(targetItem1.Id, contract.TargetItemId);
        Assert.Equal(tag1.Id, contract.RequestedTagId);
        Assert.True(contract.Payload is MutualPayload pm && pm.OfferedTargetItemId == targetItem2.Id);
        Assert.True(contract.Payload is MutualPayload pm2 && pm2.OfferedTagId == tag2.Id);
        Assert.Equal(rightAsset.Id, contract.ConsumedRightAssetId);
        Assert.Equal(TradeStatus.Proposed, contract.Status);

        TaggingRequestEntity? saved = await dbContext.TaggingRequestEntities
            .FirstOrDefaultAsync(c => c.Id == contract.Id);
        Assert.NotNull(saved);
    }
}