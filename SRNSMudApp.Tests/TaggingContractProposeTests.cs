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
    public async Task ProposeGratisContractAsync_WhenTagOwnerAutoAcceptIsEnabled_ShouldExecuteImmediately()
    {
        // Arrange
        await using var scope = CreateTestScope();
        var (dbContext, service, tid) = scope;

        var requesterId = $"req_{tid}";
        var tagOwnerId = $"owner_{tid}";
        await dbContext.SeedUsersAsync(requesterId, tagOwnerId);

        var targetItem = new Item { Content = $"TargetItem_{tid}", OwnerId = requesterId };
        var tag = new Tag { Name = $"AutoAcceptTag_{tid}", OwnerId = tagOwnerId, AutoAcceptIncomingTaggingRequests = true, CachedWeight = 10 };
        dbContext.Items.Add(targetItem);
        dbContext.Tags.Add(tag);
        await dbContext.SaveChangesAsync();

        // Act
        var result = await service.ProposeGratisContractAsync(
            requesterId,
            tagOwnerId,
            targetItem.Id,
            tag.Id,
            requestType: TaggingRequestType.Add,
            proposedWeight: 2,
            message: "please accept automatically");

        // Assert
        Assert.True(result is Success<TaggingRequestEntity>);
        var contract = result switch
        {
            Success<TaggingRequestEntity> s => s.Value,
            _ => throw new InvalidOperationException("Expected Success")
        };

        dbContext.ChangeTracker.Clear();
        TaggingRequestEntity? saved = await dbContext.TaggingRequestEntities
            .FirstOrDefaultAsync(c => c.Id == contract.Id);
        Assert.NotNull(saved);
        Assert.Equal(TradeStatus.Executed, saved!.Status);
        Assert.NotNull(await dbContext.TagRelations.FirstOrDefaultAsync(tr => tr.ItemId == targetItem.Id && tr.TagId == tag.Id));
        Assert.Equal(12, (await dbContext.Tags.FindAsync(tag.Id))!.CachedWeight);
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

    [Fact]
    public async Task ProposeGratisContractAsync_WhenTagDelegatedToGroup_AndRequesterIsMember_ShouldExecuteImmediately()
    {
        // Arrange
        await using var scope = CreateTestScope();
        var (dbContext, service, tid) = scope;

        var requesterId = $"req_{tid}";
        var tagOwnerId = $"owner_{tid}";
        await dbContext.SeedUsersAsync(requesterId, tagOwnerId);

        var group = new UserGroup { Name = $"DelegatedGroup_{tid}", OwnerId = tagOwnerId };
        var member = new UserGroupMember { UserGroup = group, UserId = requesterId, OwnerId = tagOwnerId };
        group.Members.Add(member);
        dbContext.UserGroups.Add(group);
        await dbContext.SaveChangesAsync();

        var targetItem = new Item { Content = $"TargetItem_{tid}", OwnerId = requesterId };
        var tag = new Tag
        {
            Name = $"DelegatedTag_{tid}",
            OwnerId = tagOwnerId,
            CachedWeight = 10
        };
        tag.AutoApproveUserGroups.Add(new TagAutoApproveUserGroup
        {
            Tag = tag,
            UserGroupId = group.Id,
            OwnerId = tagOwnerId
        });
        dbContext.Items.Add(targetItem);
        dbContext.Tags.Add(tag);
        await dbContext.SaveChangesAsync();

        // Act
        var result = await service.ProposeGratisContractAsync(
            requesterId,
            tagOwnerId,
            targetItem.Id,
            tag.Id,
            requestType: TaggingRequestType.Add,
            proposedWeight: 1,
            message: "delegate test");

        // Assert
        Assert.True(result is Success<TaggingRequestEntity>);
        var contract = result switch
        {
            Success<TaggingRequestEntity> s => s.Value,
            _ => throw new InvalidOperationException("Expected Success")
        };

        dbContext.ChangeTracker.Clear();
        TaggingRequestEntity? saved = await dbContext.TaggingRequestEntities
            .FirstOrDefaultAsync(c => c.Id == contract.Id);
        Assert.NotNull(saved);
        Assert.Equal(TradeStatus.Executed, saved!.Status);
        Assert.NotNull(await dbContext.TagRelations.FirstOrDefaultAsync(tr => tr.ItemId == targetItem.Id && tr.TagId == tag.Id));
    }

    [Fact]
    public async Task ProposeGratisContractAsync_WhenTagDelegatedToGroup_AndRequesterIsNotMember_ShouldRemainProposed()
    {
        // Arrange
        await using var scope = CreateTestScope();
        var (dbContext, service, tid) = scope;

        var requesterId = $"req_{tid}";
        var tagOwnerId = $"owner_{tid}";
        var otherUserId = $"other_{tid}";
        await dbContext.SeedUsersAsync(requesterId, tagOwnerId, otherUserId);

        var group = new UserGroup { Name = $"DelegatedGroup_{tid}", OwnerId = tagOwnerId };
        var member = new UserGroupMember { UserGroup = group, UserId = otherUserId, OwnerId = tagOwnerId };
        group.Members.Add(member);
        dbContext.UserGroups.Add(group);
        await dbContext.SaveChangesAsync();

        var targetItem = new Item { Content = $"TargetItem_{tid}", OwnerId = requesterId };
        var tag = new Tag
        {
            Name = $"DelegatedTag_{tid}",
            OwnerId = tagOwnerId,
            CachedWeight = 10
        };
        tag.AutoApproveUserGroups.Add(new TagAutoApproveUserGroup
        {
            Tag = tag,
            UserGroupId = group.Id,
            OwnerId = tagOwnerId
        });
        dbContext.Items.Add(targetItem);
        dbContext.Tags.Add(tag);
        await dbContext.SaveChangesAsync();

        // Act
        var result = await service.ProposeGratisContractAsync(
            requesterId,
            tagOwnerId,
            targetItem.Id,
            tag.Id,
            requestType: TaggingRequestType.Add,
            proposedWeight: 1,
            message: "not member test");

        // Assert
        Assert.True(result is Success<TaggingRequestEntity>);
        var contract = result switch
        {
            Success<TaggingRequestEntity> s => s.Value,
            _ => throw new InvalidOperationException("Expected Success")
        };

        dbContext.ChangeTracker.Clear();
        TaggingRequestEntity? saved = await dbContext.TaggingRequestEntities
            .FirstOrDefaultAsync(c => c.Id == contract.Id);
        Assert.NotNull(saved);
        Assert.Equal(TradeStatus.Proposed, saved!.Status);
        Assert.Null(await dbContext.TagRelations.FirstOrDefaultAsync(tr => tr.ItemId == targetItem.Id && tr.TagId == tag.Id));
    }

    [Fact]
    public async Task ProposeGratisContractAsync_WhenMultipleGroupsAllowed_AndRequesterIsInAnyGroup_ShouldExecuteImmediately()
    {
        // Arrange
        await using var scope = CreateTestScope();
        var (dbContext, service, tid) = scope;

        var requesterId = $"req_{tid}";
        var tagOwnerId = $"owner_{tid}";
        var otherUserId = $"other_{tid}";
        await dbContext.SeedUsersAsync(requesterId, tagOwnerId, otherUserId);

        var groupA = new UserGroup { Name = $"GroupA_{tid}", OwnerId = tagOwnerId };
        var memberA = new UserGroupMember { UserGroup = groupA, UserId = otherUserId, OwnerId = tagOwnerId };
        groupA.Members.Add(memberA);

        var groupB = new UserGroup { Name = $"GroupB_{tid}", OwnerId = tagOwnerId };
        var memberB = new UserGroupMember { UserGroup = groupB, UserId = requesterId, OwnerId = tagOwnerId };
        groupB.Members.Add(memberB);

        dbContext.UserGroups.AddRange(groupA, groupB);
        await dbContext.SaveChangesAsync();

        var targetItem = new Item { Content = $"TargetItem_{tid}", OwnerId = requesterId };
        var tag = new Tag
        {
            Name = $"MultiGroupTag_{tid}",
            OwnerId = tagOwnerId,
            CachedWeight = 10
        };
        tag.AutoApproveUserGroups.Add(new TagAutoApproveUserGroup { Tag = tag, UserGroupId = groupA.Id, OwnerId = tagOwnerId });
        tag.AutoApproveUserGroups.Add(new TagAutoApproveUserGroup { Tag = tag, UserGroupId = groupB.Id, OwnerId = tagOwnerId });

        dbContext.Items.Add(targetItem);
        dbContext.Tags.Add(tag);
        await dbContext.SaveChangesAsync();

        // Act
        var result = await service.ProposeGratisContractAsync(
            requesterId,
            tagOwnerId,
            targetItem.Id,
            tag.Id,
            requestType: TaggingRequestType.Add,
            proposedWeight: 1,
            message: "multi group test");

        // Assert
        Assert.True(result is Success<TaggingRequestEntity>);
        var contract = result switch
        {
            Success<TaggingRequestEntity> s => s.Value,
            _ => throw new InvalidOperationException("Expected Success")
        };

        dbContext.ChangeTracker.Clear();
        TaggingRequestEntity? saved = await dbContext.TaggingRequestEntities
            .FirstOrDefaultAsync(c => c.Id == contract.Id);
        Assert.NotNull(saved);
        Assert.Equal(TradeStatus.Executed, saved!.Status);
        Assert.NotNull(await dbContext.TagRelations.FirstOrDefaultAsync(tr => tr.ItemId == targetItem.Id && tr.TagId == tag.Id));
    }
}