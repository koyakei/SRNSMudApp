using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Tests.TestSupport;

namespace SRNSMudApp.Tests;

public class TaggableTargetContractTests : TaggingContractTestBase
{
    [Fact]
    public async Task Item_ShouldAutomaticallyReceiveTaggableTarget_OnSave()
    {
        await using var scope = CreateTestScope();
        var (dbContext, _, tid) = scope;

        var user = $"user_{tid}";
        await dbContext.SeedUsersAsync(user);

        var item = new Item { Content = $"Test content_{tid}", OwnerId = user };
        dbContext.Items.Add(item);
        await dbContext.SaveChangesAsync();

        Assert.True(item.TagTargetId > 0);

        TaggableTarget? target = await dbContext.TaggableTargets.AsNoTracking().FirstOrDefaultAsync(t => t.Id == item.TagTargetId);
        Assert.NotNull(target);
        Assert.Equal("Item", target.TargetType);
        Assert.Equal(user, target.OwnerId);
    }

    [Fact]
    public async Task TagEdge_ShouldAutomaticallyReceiveTaggableTarget_OnSave()
    {
        await using var scope = CreateTestScope();
        var (dbContext, _, tid) = scope;

        var user = $"user_{tid}";
        await dbContext.SeedUsersAsync(user);

        var tagA = new Tag { Name = $"TagA_{tid}", OwnerId = user };
        var tagB = new Tag { Name = $"TagB_{tid}", OwnerId = user };
        dbContext.Tags.AddRange(tagA, tagB);
        await dbContext.SaveChangesAsync();

        var edge = new TagEdge { SourceTagId = tagA.Id, TargetTagId = tagB.Id, OwnerId = user };
        dbContext.TagEdges.Add(edge);
        await dbContext.SaveChangesAsync();

        Assert.True(edge.TagTargetId > 0);

        TaggableTarget? target = await dbContext.TaggableTargets.AsNoTracking().FirstOrDefaultAsync(t => t.Id == edge.TagTargetId);
        Assert.NotNull(target);
        Assert.Equal("TagEdge", target.TargetType);
        Assert.Equal(user, target.OwnerId);
    }

    [Fact]
    public async Task TagEdge_GratisContract_Accept_ShouldCreateTagAttachment_BurnAsset_AndRecordLedger()
    {
        await using var scope = CreateTestScope();
        var (dbContext, service, tid) = scope;

        var userA = $"ua_{tid}";
        var userB = $"ub_{tid}";
        await dbContext.SeedUsersAsync(userA, userB);

        var tagA = new Tag { Name = $"Source_{tid}", OwnerId = userA };
        var tagB = new Tag { Name = $"Target_{tid}", OwnerId = userA };
        var tagMeaning = new Tag { Name = $"Meaning_{tid}", OwnerId = userB, CachedWeight = 50 };
        dbContext.Tags.AddRange(tagA, tagB, tagMeaning);
        await dbContext.SaveChangesAsync();

        var edge = new TagEdge { SourceTagId = tagA.Id, TargetTagId = tagB.Id, OwnerId = userA };
        dbContext.TagEdges.Add(edge);
        await dbContext.SaveChangesAsync();

        // userA proposes Gratis contract on edge to userB (tag owner of tagMeaning)
        var proposeResult = await service.ProposeGratisEdgeContractAsync(
            requesterUserId: userA,
            tagOwnerUserId: userB,
            tagEdgeId: edge.Id,
            requestedTagId: tagMeaning.Id,
            requestType: TaggingRequestType.Add,
            proposedWeight: 3,
            message: "Annotate edge with your tag"
        );

        Assert.True(proposeResult is Success<TaggingRequestEntity>);
        var contract = proposeResult switch { Success<TaggingRequestEntity> s => s.Value, _ => null! };
        Assert.Equal(edge.TagTargetId, contract.TargetId);

        // userB accepts the contract (userB issues/burns RightAsset)
        var acceptResult = await service.AcceptContractAsync(contract.Id, userB);
        Assert.True(acceptResult is Success<string>, acceptResult switch { Failure f => f.ErrorMessage, _ => "" });

        // Verify TagEdgeTagAttachment was created
        TagEdgeTagAttachment? attachment = await dbContext.TagEdgeTagAttachments
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.TagEdgeId == edge.Id && a.TagId == tagMeaning.Id);
        Assert.NotNull(attachment);
        Assert.Equal(3, attachment.Weight);
        Assert.Equal(userA, attachment.OwnerId);
        Assert.True(attachment.ConsumedRightAssetId > 0);

        // Verify Tag CachedWeight
        Tag? updatedTag = await dbContext.Tags.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tagMeaning.Id);
        Assert.NotNull(updatedTag);
        Assert.Equal(53, updatedTag.CachedWeight);

        // Verify TagWeightLedger
        TagWeightLedger? ledger = await dbContext.TagWeightLedgers
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.TagId == tagMeaning.Id && l.SourceType == "TagEdgeTagAttachmentInsert");
        Assert.NotNull(ledger);
        Assert.Equal(3, ledger.Delta);
        Assert.Equal(50, ledger.PreviousWeight);
        Assert.Equal(53, ledger.NewWeight);
        Assert.Equal(attachment.ConsumedRightAssetId, ledger.ConsumedRightAssetId);

        // Verify requests by edge ID query
        var edgeRequests = await service.GetRequestsByEdgeIdAsync(edge.Id);
        Assert.Single(edgeRequests);
        Assert.Equal(contract.Id, edgeRequests[0].Id);
    }

    [Fact]
    public async Task CascadeDelete_TagEdge_ShouldDeleteTaggableTarget()
    {
        await using var scope = CreateTestScope();
        var (dbContext, _, tid) = scope;

        var user = $"user_{tid}";
        await dbContext.SeedUsersAsync(user);

        var tagA = new Tag { Name = $"Source_{tid}", OwnerId = user };
        var tagB = new Tag { Name = $"Target_{tid}", OwnerId = user };
        dbContext.Tags.AddRange(tagA, tagB);
        await dbContext.SaveChangesAsync();

        var edge = new TagEdge { SourceTagId = tagA.Id, TargetTagId = tagB.Id, OwnerId = user };
        dbContext.TagEdges.Add(edge);
        await dbContext.SaveChangesAsync();

        var targetId = edge.TagTargetId;
        Assert.NotNull(await dbContext.TaggableTargets.FindAsync(targetId));

        // Delete the edge
        dbContext.TagEdges.Remove(edge);
        await dbContext.SaveChangesAsync();

        // Verify edge is gone
        Assert.Null(await dbContext.TagEdges.FindAsync(edge.Id));

        // Verify TaggableTarget is deleted by cascade
        Assert.Null(await dbContext.TaggableTargets.FindAsync(targetId));
    }
}