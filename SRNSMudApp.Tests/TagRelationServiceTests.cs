using Microsoft.EntityFrameworkCore;
using SRNSMudApp.Data;
using SRNSMudApp.Services;

namespace SRNSMudApp.Tests.Services;

public class TagRelationServiceTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly TagRelationService _service;

    public TagRelationServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        
        _context = new ApplicationDbContext(options);
        _service = new TagRelationService(_context);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task LinkTagToItemAsync_ShouldCreateRelationAndUpdateTagWeightAndCreateAuditLogs()
    {
        // Arrange
        var user = new ApplicationUser { Id = "user1", UserName = "test" };
        var item = new Item { Id = 1, OwnerId = "user1", Content = "Test Content" };
        var tag = new Tag { Id = 1, Name = "TestTag", OwnerId = "user1", CachedWeight = 10 };
        
        _context.Users.Add(user);
        _context.Items.Add(item);
        _context.Tags.Add(tag);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.LinkTagToItemAsync(1, 1, "user1", 5);

        // Assert
        Assert.Equal(TaggingResult.Success, result);

        var relation = await _context.TagRelations!.FirstOrDefaultAsync(r => r.ItemId == 1 && r.TagId == 1);
        Assert.NotNull(relation);
        Assert.Equal(5, relation.Weight);

        var updatedTag = await _context.Tags!.FindAsync(1);
        Assert.NotNull(updatedTag);
        Assert.Equal(15, updatedTag.CachedWeight); // 10 + 5

        var ledger = await _context.TagWeightLedgers!.FirstOrDefaultAsync();
        Assert.NotNull(ledger);
        Assert.Equal(5, ledger.Delta);
        Assert.Equal(10, ledger.PreviousWeight);
        Assert.Equal(15, ledger.NewWeight);
        Assert.True(ledger.IsOwnerAction);
        Assert.Equal(relation.Id, ledger.SourceId);
        // Assert.NotNull(ledger.ConsumedRightAssetId);

        var asset = await _context.RightAssets!.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.Id == ledger.ConsumedRightAssetId);
        Assert.NotNull(asset);
        Assert.True(asset.IsBurned);
    }



    [Fact]
    public async Task AllocateWeightAsync_ShouldConsumeRightAssetAndAllocateToTagRelations()
    {
        // Arrange
        // RightAsset weight 5 は ItemA.TagRelation.weight を 3 操作すると RightAsset weight 2 に
        // ItemB.TagRelation.weight を 2 操作すると RightAsset weight 0になる
        var user = new ApplicationUser { Id = "user1", UserName = "test" };
        var itemA = new Item { Id = 1, OwnerId = "user1", Content = "Item A" };
        var itemB = new Item { Id = 2, OwnerId = "user1", Content = "Item B" };
        var tag = new Tag { Id = 1, Name = "TestTag", OwnerId = "user1", CachedWeight = 0 };
        
        var rightAsset = new RightAsset
        {
            Id = 1,
            OwnerId = "user1",
            TargetTagId = 1,
            Amount = 5,
            IsBurned = false
        };

        _context.Users.Add(user);
        _context.Items.AddRange(itemA, itemB);
        _context.Tags.Add(tag);
        _context.RightAssets.Add(rightAsset);
        await _context.SaveChangesAsync();

        // Act 1: ItemA.TagRelation.weight を 3 操作する (RightAsset -3)
        var result1 = await _service.AllocateWeightAsync(rightAsset.Id, itemA.Id, tag.Id, "user1", 3);
        
        // Assert 1
        Assert.Equal(TaggingResult.Success, result1);
        
        var updatedAsset1 = await _context.RightAssets.FindAsync(1);
        Assert.NotNull(updatedAsset1);
        Assert.Equal(2, updatedAsset1.Amount); // RightAsset weight 2 に
        Assert.False(updatedAsset1.IsBurned);

        var relationA = await _context.TagRelations.FirstOrDefaultAsync(r => r.ItemId == itemA.Id && r.TagId == tag.Id);
        Assert.NotNull(relationA);
        Assert.Equal(3, relationA.Weight);

        // Act 2: ItemB.TagRelation.weight を 2 操作する (RightAsset -2)
        var result2 = await _service.AllocateWeightAsync(rightAsset.Id, itemB.Id, tag.Id, "user1", 2);

        // Assert 2
        Assert.Equal(TaggingResult.Success, result2);

        var updatedAsset2 = await _context.RightAssets.FindAsync(1);
        Assert.NotNull(updatedAsset2);
        Assert.Equal(0, updatedAsset2.Amount); // RightAsset weight 0 になる
        Assert.True(updatedAsset2.IsBurned); // 0になったのでBurnされること

        var relationB = await _context.TagRelations.FirstOrDefaultAsync(r => r.ItemId == itemB.Id && r.TagId == tag.Id);
        Assert.NotNull(relationB);
        Assert.Equal(2, relationB.Weight);
    }

    [Fact]
    public async Task AllocateWeightAsync_NegativeDelta_ShouldConsumeAbsoluteValueFromRightAssetAndDecreaseTagRelationWeight()
    {
        // Arrange
        // ItemAに TagX が weight 1 でタグ付けされている
        // RightAsset (Weight 5) を用いて、ItemA の TagX を -3 操作する。
        // 結果: ItemAのTagRelationのWeightが -2になり、RightAssetのWeightは残りの 2 になる。
        // そのまま同じ RightAsset を用いて、ItemB に 2 操作する。
        // 結果: ItemBのTagRelationのWeightが 2 になり、RightAssetのWeightは 0 になり、完全にBurnされる。
        var user = new ApplicationUser { Id = "user2", UserName = "test2" };
        var itemA = new Item { Id = 3, OwnerId = "user2", Content = "Item A" };
        var itemB = new Item { Id = 4, OwnerId = "user2", Content = "Item B" };
        var tagX = new Tag { Id = 2, Name = "TagX", OwnerId = "user2", CachedWeight = 1 };
        
        var existingRelationA = new TagRelation
        {
            ItemId = 3,
            TagId = 2,
            OwnerId = "user2",
            Weight = 1
        };

        var rightAsset = new RightAsset
        {
            Id = 2,
            OwnerId = "user2",
            TargetTagId = 2,
            Amount = 5,
            IsBurned = false
        };

        _context.Users.Add(user);
        _context.Items.AddRange(itemA, itemB);
        _context.Tags.Add(tagX);
        _context.TagRelations.Add(existingRelationA);
        _context.RightAssets.Add(rightAsset);
        await _context.SaveChangesAsync();

        // Act 1: ItemA の TagX を -3 操作する
        var result1 = await _service.AllocateWeightAsync(rightAsset.Id, itemA.Id, tagX.Id, "user2", -3);
        
        // Assert 1
        Assert.Equal(TaggingResult.Success, result1);
        
        var updatedAsset1 = await _context.RightAssets.FindAsync(2);
        Assert.NotNull(updatedAsset1);
        Assert.Equal(2, updatedAsset1.Amount); // 5 - |-3| = 2
        Assert.False(updatedAsset1.IsBurned);

        var relationA = await _context.TagRelations.FirstOrDefaultAsync(r => r.ItemId == itemA.Id && r.TagId == tagX.Id);
        Assert.NotNull(relationA);
        Assert.Equal(-2, relationA.Weight); // 1 + (-3) = -2

        // Act 2: ItemB に 2 操作する
        var result2 = await _service.AllocateWeightAsync(rightAsset.Id, itemB.Id, tagX.Id, "user2", 2);

        // Assert 2
        Assert.Equal(TaggingResult.Success, result2);

        var updatedAsset2 = await _context.RightAssets.FindAsync(2);
        Assert.NotNull(updatedAsset2);
        Assert.Equal(0, updatedAsset2.Amount); // 2 - |2| = 0
        Assert.True(updatedAsset2.IsBurned); // 0になったのでBurnされること

        var relationB = await _context.TagRelations.FirstOrDefaultAsync(r => r.ItemId == itemB.Id && r.TagId == tagX.Id);
        Assert.NotNull(relationB);
        Assert.Equal(2, relationB.Weight); // 0 + 2 = 2
    }
}
