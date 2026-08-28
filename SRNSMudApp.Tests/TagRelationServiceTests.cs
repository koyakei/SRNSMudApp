using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;

namespace SRNSMudApp.Tests;

public class TagRelationServiceTests : IAsyncLifetime
{
    private MsSqlTestDatabase _sharedDb = null!;

    public async Task InitializeAsync()
    {
        _sharedDb = await SharedMsSqlTestDatabase.GetInstanceAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private (ApplicationDbContext context, TagRelationService service, string tid) CreateScope()
    {
        var tid = Guid.NewGuid().ToString("N")[..8];
        var context = new ApplicationDbContext(_sharedDb.Options);
        var service = new TagRelationService(context);
        return (context, service, tid);
    }

    [Fact]
    public async Task LinkTagToItemAsync_ShouldCreateRelationAndUpdateTagWeightAndCreateAuditLogs()
    {
        // Arrange
        var (context, service, tid) = CreateScope();
        await using (context)
        {
            var user = new ApplicationUser { Id = $"user_{tid}", UserName = $"test_{tid}" };
            var item = new Item { OwnerId = user.Id, Content = $"Test Content_{tid}" };
            var tag = new Tag { Name = $"TestTag_{tid}", OwnerId = user.Id, CachedWeight = 10 };

            context.Users.Add(user);
            context.Items.Add(item);
            context.Tags.Add(tag);
            await context.SaveChangesAsync();

            // Act
            Result<bool> result = await service.LinkTagToItemAsync(item.Id, tag.Id, user.Id, 5);

            // Assert
            Assert.True(result is Success<bool>);

            TagRelation? relation = await context.TagRelations!.FirstOrDefaultAsync(r => r.ItemId == item.Id && r.TagId == tag.Id);
            Assert.NotNull(relation);
            Assert.Equal(5, relation.Weight);

            Tag? updatedTag = await context.Tags!.FindAsync(tag.Id);
            Assert.NotNull(updatedTag);
            Assert.Equal(15, updatedTag.CachedWeight);

            TagWeightLedger? ledger = await context.TagWeightLedgers!.FirstOrDefaultAsync(l => l.SourceId == relation.Id);
            Assert.NotNull(ledger);
            Assert.Equal(5, ledger.Delta);
            Assert.Equal(10, ledger.PreviousWeight);
            Assert.Equal(15, ledger.NewWeight);
            Assert.True(ledger.IsOwnerAction);
            Assert.Equal(relation.Id, ledger.SourceId);

            RightAsset? asset = await context.RightAssets!.IgnoreQueryFilters()
                .FirstOrDefaultAsync(a => a.Id == ledger.ConsumedRightAssetId);
            Assert.NotNull(asset);
            Assert.True(asset.IsBurned);
        }
    }

    [Fact]
    public async Task AllocateWeightAsync_ShouldConsumeRightAssetAndAllocateToTagRelations()
    {
        // Arrange
        var (context, service, tid) = CreateScope();
        await using (context)
        {
            var user = new ApplicationUser { Id = $"user_{tid}", UserName = $"test_{tid}" };
            var itemA = new Item { OwnerId = user.Id, Content = $"Item A_{tid}" };
            var itemB = new Item { OwnerId = user.Id, Content = $"Item B_{tid}" };
            var tag = new Tag { Name = $"TestTag_{tid}", OwnerId = user.Id, CachedWeight = 0 };

            context.Users.Add(user);
            context.Items.AddRange(itemA, itemB);
            context.Tags.Add(tag);
            await context.SaveChangesAsync();

            var rightAsset = new RightAsset
            {
                OwnerId = user.Id,
                TargetTagId = tag.Id,
                Amount = 5,
                IsBurned = false
            };
            context.RightAssets.Add(rightAsset);
            await context.SaveChangesAsync();

            // Act 1: ItemA.TagRelation.weight を 3 操作する (RightAsset -3)
            Result<bool> result1 = await service.AllocateWeightAsync(rightAsset.Id, itemA.Id, tag.Id, user.Id, 3);

            // Assert 1
            Assert.True(result1 is Success<bool>);

            RightAsset? updatedAsset1 = await context.RightAssets.FindAsync(rightAsset.Id);
            Assert.NotNull(updatedAsset1);
            Assert.Equal(2, updatedAsset1.Amount);
            Assert.False(updatedAsset1.IsBurned);

            TagRelation? relationA =
                await context.TagRelations.FirstOrDefaultAsync(r => r.ItemId == itemA.Id && r.TagId == tag.Id);
            Assert.NotNull(relationA);
            Assert.Equal(3, relationA.Weight);

            // Act 2: ItemB.TagRelation.weight を 2 操作する (RightAsset -2)
            Result<bool> result2 = await service.AllocateWeightAsync(rightAsset.Id, itemB.Id, tag.Id, user.Id, 2);

            // Assert 2
            Assert.True(result2 is Success<bool>);

            RightAsset? updatedAsset2 = await context.RightAssets.FindAsync(rightAsset.Id);
            Assert.NotNull(updatedAsset2);
            Assert.Equal(0, updatedAsset2.Amount);
            Assert.True(updatedAsset2.IsBurned);

            TagRelation? relationB =
                await context.TagRelations.FirstOrDefaultAsync(r => r.ItemId == itemB.Id && r.TagId == tag.Id);
            Assert.NotNull(relationB);
            Assert.Equal(2, relationB.Weight);
        }
    }

    [Fact]
    public async Task
        AllocateWeightAsync_NegativeDelta_ShouldConsumeAbsoluteValueFromRightAssetAndDecreaseTagRelationWeight()
    {
        // Arrange
        var (context, service, tid) = CreateScope();
        await using (context)
        {
            var user = new ApplicationUser { Id = $"user_{tid}", UserName = $"test2_{tid}" };
            var itemA = new Item { OwnerId = user.Id, Content = $"Item A_{tid}" };
            var itemB = new Item { OwnerId = user.Id, Content = $"Item B_{tid}" };
            var tagX = new Tag { Name = $"TagX_{tid}", OwnerId = user.Id, CachedWeight = 1 };

            context.Users.Add(user);
            context.Items.AddRange(itemA, itemB);
            context.Tags.Add(tagX);
            await context.SaveChangesAsync();

            var existingRelationA = new TagRelation { ItemId = itemA.Id, TagId = tagX.Id, OwnerId = user.Id, Weight = 1 };
            var rightAsset = new RightAsset
            {
                OwnerId = user.Id,
                TargetTagId = tagX.Id,
                Amount = 5,
                IsBurned = false
            };

            context.TagRelations.Add(existingRelationA);
            context.RightAssets.Add(rightAsset);
            await context.SaveChangesAsync();

            // Act 1: ItemA の TagX を -3 操作する
            Result<bool> result1 = await service.AllocateWeightAsync(rightAsset.Id, itemA.Id, tagX.Id, user.Id, -3);

            // Assert 1
            Assert.True(result1 is Success<bool>);

            RightAsset? updatedAsset1 = await context.RightAssets.FindAsync(rightAsset.Id);
            Assert.NotNull(updatedAsset1);
            Assert.Equal(2, updatedAsset1.Amount);
            Assert.False(updatedAsset1.IsBurned);

            TagRelation? relationA =
                await context.TagRelations.FirstOrDefaultAsync(r => r.ItemId == itemA.Id && r.TagId == tagX.Id);
            Assert.NotNull(relationA);
            Assert.Equal(-2, relationA.Weight);

            // Act 2: ItemB に 2 操作する
            Result<bool> result2 = await service.AllocateWeightAsync(rightAsset.Id, itemB.Id, tagX.Id, user.Id, 2);

            // Assert 2
            Assert.True(result2 is Success<bool>);

            RightAsset? updatedAsset2 = await context.RightAssets.FindAsync(rightAsset.Id);
            Assert.NotNull(updatedAsset2);
            Assert.Equal(0, updatedAsset2.Amount);
            Assert.True(updatedAsset2.IsBurned);

            TagRelation? relationB =
                await context.TagRelations.FirstOrDefaultAsync(r => r.ItemId == itemB.Id && r.TagId == tagX.Id);
            Assert.NotNull(relationB);
            Assert.Equal(2, relationB.Weight);
        }
    }

    [Fact]
    public async Task AddTagToItemAsync_WithUntrackedItem_ShouldNotThrowPrimaryKeyException()
    {
        var testUserId = $"user_{Guid.NewGuid():N}";
        var itemContent = "Test Item " + Guid.NewGuid();
        var tagName = "Test Tag " + Guid.NewGuid();

        int itemId;
        int tagId;

        // 1. Arrange: 別のDbContextでデータを作成して保存する
        await using (var db = new ApplicationDbContext(_sharedDb.Options))
        {
            var user = new ApplicationUser
            {
                Id = testUserId,
                UserName = "testuser_" + testUserId,
                Email = $"testuser_{testUserId}@example.com"
            };
            db.Users.Add(user);

            var item = new Item { Content = itemContent, OwnerId = user.Id };
            db.Items.Add(item);

            var tag = new Tag { Name = tagName, OwnerId = user.Id };
            db.Tags.Add(tag);

            await db.SaveChangesAsync();

            itemId = item.Id;
            tagId = tag.Id;
        }

        // 2. Act: 新しいDbContextを持つTagRelationServiceで操作する
        await using (var db = new ApplicationDbContext(_sharedDb.Options))
        {
            var service = new TagRelationService(db);
            var result = await service.LinkTagToItemAsync(itemId, tagId, testUserId);
            Assert.True(result is Success<bool>);
        }

        // 3. Assert: 正しくTagRelationが作成されているか確認する
        await using (var db = new ApplicationDbContext(_sharedDb.Options))
        {
            var relations = await db.TagRelations
                .Where(tr => tr.ItemId == itemId && tr.TagId == tagId)
                .ToListAsync();

            Assert.Single(relations);
            Assert.Equal(testUserId, relations[0].OwnerId);
        }
    }
}