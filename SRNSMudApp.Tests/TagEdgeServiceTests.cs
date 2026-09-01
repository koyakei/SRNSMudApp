using Microsoft.EntityFrameworkCore;

using Moq;

using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;
using SRNSMudApp.Tests.TestSupport;

namespace SRNSMudApp.Tests;

public class TagEdgeServiceTests : IAsyncLifetime
{
    private MsSqlTestDatabase _sharedDb = null!;

    public async Task InitializeAsync()
    {
        _sharedDb = await SharedMsSqlTestDatabase.GetInstanceAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private (ApplicationDbContext dbContext, TagEdgeService service, string tid) CreateScope()
    {
        var tid = Guid.NewGuid().ToString("N")[..8];
        var dbContext = new ApplicationDbContext(_sharedDb.Options);
        var mockDbFactory = new Mock<IDbContextFactory<ApplicationDbContext>>();
        mockDbFactory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new ApplicationDbContext(_sharedDb.Options));

        var service = new TagEdgeService(mockDbFactory.Object);
        return (dbContext, service, tid);
    }

    [Fact]
    public async Task CreateEdgeAsync_ShouldCreateEdge_WhenValid()
    {
        var (dbContext, service, tid) = CreateScope();
        await using (dbContext)
        {
            var userId = $"user_{tid}";
            await dbContext.SeedUsersAsync(userId);

            var tag1 = new Tag { Name = $"Tag1_{tid}", OwnerId = userId };
            var tag2 = new Tag { Name = $"Tag2_{tid}", OwnerId = userId };
            dbContext.Tags.AddRange(tag1, tag2);
            await dbContext.SaveChangesAsync();

            Result<TagEdge> result = await service.CreateEdgeAsync(tag1.Id, tag2.Id, userId);

            Assert.True(result is Success<TagEdge>);
            var edge = result switch { Success<TagEdge> s => s.Value, _ => throw new InvalidOperationException() };
            Assert.Equal(tag1.Id, edge.SourceTagId);
            Assert.Equal(tag2.Id, edge.TargetTagId);
            Assert.Equal(userId, edge.OwnerId);

            TagEdge? savedEdge = await dbContext.TagEdges.AsNoTracking().FirstOrDefaultAsync(e => e.Id == edge.Id);
            Assert.NotNull(savedEdge);
        }
    }

    [Fact]
    public async Task CreateEdgeAsync_ShouldRejectDuplicate_ForSameOwnerAndTags()
    {
        var (dbContext, service, tid) = CreateScope();
        await using (dbContext)
        {
            var userId = $"user_{tid}";
            await dbContext.SeedUsersAsync(userId);

            var tag1 = new Tag { Name = $"Tag1_{tid}", OwnerId = userId };
            var tag2 = new Tag { Name = $"Tag2_{tid}", OwnerId = userId };
            dbContext.Tags.AddRange(tag1, tag2);
            await dbContext.SaveChangesAsync();

            Result<TagEdge> first = await service.CreateEdgeAsync(tag1.Id, tag2.Id, userId);
            Assert.True(first is Success<TagEdge>);

            Result<TagEdge> second = await service.CreateEdgeAsync(tag1.Id, tag2.Id, userId);
            Assert.True(second is Failure f && f.ErrorMessage.Contains("既に存在します"));
        }
    }

    [Fact]
    public async Task CreateEdgeAsync_ShouldFail_WhenTagsDoNotExist()
    {
        var (dbContext, service, tid) = CreateScope();
        await using (dbContext)
        {
            var userId = $"user_{tid}";
            await dbContext.SeedUsersAsync(userId);

            Result<TagEdge> result = await service.CreateEdgeAsync(999999, 999998, userId);
            Assert.True(result is Failure f && f.ErrorMessage.Contains("見つかりません"));
        }
    }

    [Fact]
    public async Task DeleteEdgeAsync_ShouldDeleteEdgeAndCascadeAttachments()
    {
        var (dbContext, service, tid) = CreateScope();
        await using (dbContext)
        {
            var userId = $"user_{tid}";
            await dbContext.SeedUsersAsync(userId);

            var tag1 = new Tag { Name = $"Tag1_{tid}", OwnerId = userId };
            var tag2 = new Tag { Name = $"Tag2_{tid}", OwnerId = userId };
            var tagMeaning = new Tag { Name = $"Meaning_{tid}", OwnerId = userId };
            dbContext.Tags.AddRange(tag1, tag2, tagMeaning);
            await dbContext.SaveChangesAsync();

            var edge = new TagEdge { SourceTagId = tag1.Id, TargetTagId = tag2.Id, OwnerId = userId };
            dbContext.TagEdges.Add(edge);
            await dbContext.SaveChangesAsync();

            var asset = new RightAsset
            {
                OwnerId = userId,
                TargetTagId = tagMeaning.Id,
                Amount = 1,
                IsBurned = false
            };
            dbContext.RightAssets.Add(asset);
            await dbContext.SaveChangesAsync();

            var attachResult = await service.AttachTagToEdgeAsync(edge.Id, tagMeaning.Id, asset.Id, userId, 1);
            Assert.True(attachResult is Success<TagEdgeTagAttachment>);
            var attachmentId = attachResult switch { Success<TagEdgeTagAttachment> s => s.Value.Id, _ => throw new InvalidOperationException() };

            // Delete edge
            Result<bool> deleteResult = await service.DeleteEdgeAsync(edge.Id, userId);
            Assert.True(deleteResult is Success<bool>);

            Assert.Null(await dbContext.TagEdges.AsNoTracking().FirstOrDefaultAsync(e => e.Id == edge.Id));
            Assert.Null(await dbContext.TagEdgeTagAttachments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == attachmentId));
        }
    }

    [Fact]
    public async Task DeleteEdgeAsync_ShouldFail_WhenNotFoundOrNotOwner()
    {
        var (dbContext, service, tid) = CreateScope();
        await using (dbContext)
        {
            var ownerId = $"user1_{tid}";
            var otherId = $"user2_{tid}";
            await dbContext.SeedUsersAsync(ownerId, otherId);

            var tag1 = new Tag { Name = $"Tag1_{tid}", OwnerId = ownerId };
            var tag2 = new Tag { Name = $"Tag2_{tid}", OwnerId = ownerId };
            dbContext.Tags.AddRange(tag1, tag2);
            await dbContext.SaveChangesAsync();

            var edge = new TagEdge { SourceTagId = tag1.Id, TargetTagId = tag2.Id, OwnerId = ownerId };
            dbContext.TagEdges.Add(edge);
            await dbContext.SaveChangesAsync();

            // Not found
            Result<bool> notFound = await service.DeleteEdgeAsync(999999, ownerId);
            Assert.True(notFound is Failure);

            // Not owner
            Result<bool> notOwner = await service.DeleteEdgeAsync(edge.Id, otherId);
            Assert.True(notOwner is Failure f && f.ErrorMessage.Contains("権限がありません"));
        }
    }

    [Fact]
    public async Task AttachTagToEdgeAsync_ValidationFailures()
    {
        var (dbContext, service, tid) = CreateScope();
        await using (dbContext)
        {
            var userId = $"user_{tid}";
            var otherUserId = $"other_{tid}";
            await dbContext.SeedUsersAsync(userId, otherUserId);

            var tag1 = new Tag { Name = $"Tag1_{tid}", OwnerId = userId };
            var tag2 = new Tag { Name = $"Tag2_{tid}", OwnerId = userId };
            var tag3 = new Tag { Name = $"Tag3_{tid}", OwnerId = userId };
            var tag4 = new Tag { Name = $"Tag4_{tid}", OwnerId = userId };
            dbContext.Tags.AddRange(tag1, tag2, tag3, tag4);
            await dbContext.SaveChangesAsync();

            var edge = new TagEdge { SourceTagId = tag1.Id, TargetTagId = tag2.Id, OwnerId = userId };
            dbContext.TagEdges.Add(edge);
            await dbContext.SaveChangesAsync();

            var validAsset = new RightAsset { OwnerId = userId, TargetTagId = tag3.Id, Amount = 1 };
            var otherUserAsset = new RightAsset { OwnerId = otherUserId, TargetTagId = tag3.Id, Amount = 1 };
            var burnedAsset = new RightAsset { OwnerId = userId, TargetTagId = tag3.Id, Amount = 1, IsBurned = true };
            var wrongTagAsset = new RightAsset { OwnerId = userId, TargetTagId = tag4.Id, Amount = 1 };
            var zeroAmountAsset = new RightAsset { OwnerId = userId, TargetTagId = tag3.Id, Amount = 0 };
            dbContext.RightAssets.AddRange(validAsset, otherUserAsset, burnedAsset, wrongTagAsset, zeroAmountAsset);
            await dbContext.SaveChangesAsync();

            // 1. weight <= 0
            var resWeight = await service.AttachTagToEdgeAsync(edge.Id, tag3.Id, validAsset.Id, userId, 0);
            Assert.True(resWeight is Failure fWeight && fWeight.ErrorMessage.Contains("1 以上"));

            // 2. Edge not found
            var resEdge = await service.AttachTagToEdgeAsync(999999, tag3.Id, validAsset.Id, userId);
            Assert.True(resEdge is Failure fEdge && fEdge.ErrorMessage.Contains("Edge が見つかりません"));

            // 3. Tag not found
            var resTag = await service.AttachTagToEdgeAsync(edge.Id, 999999, validAsset.Id, userId);
            Assert.True(resTag is Failure fTag && fTag.ErrorMessage.Contains("タグが見つかりません"));

            // 4. Asset not found
            var resAssetNotFound = await service.AttachTagToEdgeAsync(edge.Id, tag3.Id, 999999, userId);
            Assert.True(resAssetNotFound is Failure fAsset && fAsset.ErrorMessage.Contains("RightAsset が見つかりません"));

            // 5. Other user asset
            var resOtherAsset = await service.AttachTagToEdgeAsync(edge.Id, tag3.Id, otherUserAsset.Id, userId);
            Assert.True(resOtherAsset is Failure fOther && fOther.ErrorMessage.Contains("所有していません"));

            // 6. Burned asset
            var resBurned = await service.AttachTagToEdgeAsync(edge.Id, tag3.Id, burnedAsset.Id, userId);
            Assert.True(resBurned is Failure fBurned && fBurned.ErrorMessage.Contains("既に消費済み"));

            // 7. Wrong target tag
            var resWrongTag = await service.AttachTagToEdgeAsync(edge.Id, tag3.Id, wrongTagAsset.Id, userId);
            Assert.True(resWrongTag is Failure fWrong && fWrong.ErrorMessage.Contains("対象タグの権利ではありません"));

            // 8. Amount <= 0
            var resZeroAmount = await service.AttachTagToEdgeAsync(edge.Id, tag3.Id, zeroAmountAsset.Id, userId);
            Assert.True(resZeroAmount is Failure fZero && fZero.ErrorMessage.Contains("残量が不足"));
        }
    }

    [Fact]
    public async Task AttachTagToEdgeAsync_Success_ShouldBurnAssetWhenAmountReachesZero_AndAddLedger()
    {
        var (dbContext, service, tid) = CreateScope();
        await using (dbContext)
        {
            var userId = $"user_{tid}";
            await dbContext.SeedUsersAsync(userId);

            var tag1 = new Tag { Name = $"Tag1_{tid}", OwnerId = userId };
            var tag2 = new Tag { Name = $"Tag2_{tid}", OwnerId = userId };
            var tagMeaning = new Tag { Name = $"Meaning_{tid}", OwnerId = userId, CachedWeight = 5 };
            dbContext.Tags.AddRange(tag1, tag2, tagMeaning);
            await dbContext.SaveChangesAsync();

            var edge = new TagEdge { SourceTagId = tag1.Id, TargetTagId = tag2.Id, OwnerId = userId };
            dbContext.TagEdges.Add(edge);

            var asset = new RightAsset
            {
                OwnerId = userId,
                TargetTagId = tagMeaning.Id,
                Amount = 1,
                IsBurned = false
            };
            dbContext.RightAssets.Add(asset);
            await dbContext.SaveChangesAsync();

            // Act
            Result<TagEdgeTagAttachment> result = await service.AttachTagToEdgeAsync(
                edge.Id, tagMeaning.Id, asset.Id, userId, weight: 3);

            // Assert
            Assert.True(result is Success<TagEdgeTagAttachment>);
            var attachment = result switch { Success<TagEdgeTagAttachment> s => s.Value, _ => throw new InvalidOperationException() };
            Assert.Equal(edge.Id, attachment.TagEdgeId);
            Assert.Equal(tagMeaning.Id, attachment.TagId);
            Assert.Equal(3, attachment.Weight);
            Assert.Equal(asset.Id, attachment.ConsumedRightAssetId);

            // Verify RightAsset is burned
            RightAsset? updatedAsset = await dbContext.RightAssets.AsNoTracking().FirstOrDefaultAsync(a => a.Id == asset.Id);
            Assert.NotNull(updatedAsset);
            Assert.Equal(0, updatedAsset.Amount);
            Assert.True(updatedAsset.IsBurned);
            Assert.Contains("BurnedAt", updatedAsset.BurnStatusJson);

            // Verify Tag CachedWeight
            Tag? updatedTag = await dbContext.Tags.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tagMeaning.Id);
            Assert.NotNull(updatedTag);
            Assert.Equal(8, updatedTag.CachedWeight);

            // Verify TagWeightLedger
            TagWeightLedger? ledger = await dbContext.TagWeightLedgers.AsNoTracking()
                .FirstOrDefaultAsync(l => l.SourceType == "TagEdgeTagAttachmentInsert" && l.TagId == tagMeaning.Id);
            Assert.NotNull(ledger);
            Assert.Equal(tagMeaning.Id, ledger.TagId);
            Assert.Equal(tagMeaning.Name, ledger.TagNameSnapshot);
            Assert.Equal(asset.Id, ledger.ConsumedRightAssetId);
            Assert.Equal(3, ledger.Delta);
            Assert.Equal(5, ledger.PreviousWeight);
            Assert.Equal(8, ledger.NewWeight);
            Assert.True(ledger.IsOwnerAction);

            // Verify duplicate attach rejected
            var duplicateResult = await service.AttachTagToEdgeAsync(edge.Id, tagMeaning.Id, asset.Id, userId, 1);
            Assert.True(duplicateResult is Failure fDup && fDup.ErrorMessage.Contains("既に Edge に紐付けられています"));
        }
    }

    [Fact]
    public async Task DetachTagFromEdgeAsync_Success_ShouldDecrementWeight_CreateLedger_AndNotRestoreAsset()
    {
        var (dbContext, service, tid) = CreateScope();
        await using (dbContext)
        {
            var userId = $"user_{tid}";
            var otherUserId = $"other_{tid}";
            await dbContext.SeedUsersAsync(userId, otherUserId);

            var tag1 = new Tag { Name = $"Tag1_{tid}", OwnerId = userId };
            var tag2 = new Tag { Name = $"Tag2_{tid}", OwnerId = userId };
            var tagMeaning = new Tag { Name = $"Meaning_{tid}", OwnerId = userId, CachedWeight = 10 };
            dbContext.Tags.AddRange(tag1, tag2, tagMeaning);
            await dbContext.SaveChangesAsync();

            var edge = new TagEdge { SourceTagId = tag1.Id, TargetTagId = tag2.Id, OwnerId = userId };
            dbContext.TagEdges.Add(edge);

            var asset = new RightAsset
            {
                OwnerId = userId,
                TargetTagId = tagMeaning.Id,
                Amount = 1,
                IsBurned = false
            };
            dbContext.RightAssets.Add(asset);
            await dbContext.SaveChangesAsync();

            var attachResult = await service.AttachTagToEdgeAsync(edge.Id, tagMeaning.Id, asset.Id, userId, weight: 2);
            Assert.True(attachResult is Success<TagEdgeTagAttachment>);
            var attachmentId = attachResult switch { Success<TagEdgeTagAttachment> s => s.Value.Id, _ => throw new InvalidOperationException() };

            // Attempt detach by another user -> fail
            Result<bool> failDetach = await service.DetachTagFromEdgeAsync(attachmentId, otherUserId);
            Assert.True(failDetach is Failure fFail && fFail.ErrorMessage.Contains("権限がありません"));

            // Detach by owner -> success
            Result<bool> successDetach = await service.DetachTagFromEdgeAsync(attachmentId, userId);
            Assert.True(successDetach is Success<bool>);

            // Attachment deleted
            Assert.Null(await dbContext.TagEdgeTagAttachments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == attachmentId));

            // Tag CachedWeight decreased
            Tag? updatedTag = await dbContext.Tags.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tagMeaning.Id);
            Assert.NotNull(updatedTag);
            Assert.Equal(10, updatedTag.CachedWeight); // 10 -> +2 (attach) -> -2 (detach) = 10

            // TagWeightLedger for delete created
            TagWeightLedger? deleteLedger = await dbContext.TagWeightLedgers.AsNoTracking()
                .FirstOrDefaultAsync(l => l.SourceType == "TagEdgeTagAttachmentDelete" && l.TagId == tagMeaning.Id);
            Assert.NotNull(deleteLedger);
            Assert.Equal(-2, deleteLedger.Delta);
            Assert.Equal(12, deleteLedger.PreviousWeight);
            Assert.Equal(10, deleteLedger.NewWeight);

            // RightAsset is NOT restored
            RightAsset? checkAsset = await dbContext.RightAssets.AsNoTracking().FirstOrDefaultAsync(a => a.Id == asset.Id);
            Assert.NotNull(checkAsset);
            Assert.Equal(0, checkAsset.Amount);
            Assert.True(checkAsset.IsBurned);
        }
    }

    [Fact]
    public async Task GetEdgesForTagAsync_And_GetAttachmentsForEdgeAsync_ShouldReturnPopulatedData()
    {
        var (dbContext, service, tid) = CreateScope();
        await using (dbContext)
        {
            var userId = $"user_{tid}";
            await dbContext.SeedUsersAsync(userId);

            var tagA = new Tag { Name = $"TagA_{tid}", OwnerId = userId };
            var tagB = new Tag { Name = $"TagB_{tid}", OwnerId = userId };
            var tagC = new Tag { Name = $"TagC_{tid}", OwnerId = userId };
            var tagMeaning = new Tag { Name = $"Meaning_{tid}", OwnerId = userId };
            dbContext.Tags.AddRange(tagA, tagB, tagC, tagMeaning);
            await dbContext.SaveChangesAsync();

            // edge1: A -> B
            var edge1 = new TagEdge { SourceTagId = tagA.Id, TargetTagId = tagB.Id, OwnerId = userId };
            // edge2: C -> A
            var edge2 = new TagEdge { SourceTagId = tagC.Id, TargetTagId = tagA.Id, OwnerId = userId };
            dbContext.TagEdges.AddRange(edge1, edge2);
            await dbContext.SaveChangesAsync();

            var asset = new RightAsset { OwnerId = userId, TargetTagId = tagMeaning.Id, Amount = 2 };
            dbContext.RightAssets.Add(asset);
            await dbContext.SaveChangesAsync();

            await service.AttachTagToEdgeAsync(edge1.Id, tagMeaning.Id, asset.Id, userId, 1);

            // Test GetEdgesForTagAsync for tagA (both as source and target)
            IReadOnlyList<TagEdge> edgesForA = await service.GetEdgesForTagAsync(tagA.Id);
            Assert.Equal(2, edgesForA.Count);
            Assert.All(edgesForA, e =>
            {
                Assert.NotNull(e.SourceTag);
                Assert.NotNull(e.TargetTag);
            });

            // Test GetAttachmentsForEdgeAsync
            IReadOnlyList<TagEdgeTagAttachment> attachments = await service.GetAttachmentsForEdgeAsync(edge1.Id);
            Assert.Single(attachments);
            Assert.NotNull(attachments[0].Tag);
            Assert.Equal(tagMeaning.Name, attachments[0].Tag.Name);
        }
    }
}