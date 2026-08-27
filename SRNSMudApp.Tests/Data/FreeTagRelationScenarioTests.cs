using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;

namespace SRNSMudApp.Tests.Data;

public class FreeTagRelationScenarioTests : IAsyncLifetime
{
    private MsSqlTestDatabase _sharedDb = null!;

    public async Task InitializeAsync()
    {
        _sharedDb = await SharedMsSqlTestDatabase.GetInstanceAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private ApplicationDbContext CreateDbContext() => new(_sharedDb.Options);

    [Fact]
    public async Task CreateFreeTagRelationAsync_ShouldCreateRelationAndLedgerWithoutAsset_WhenUserIsTagOwner()
    {
        // Arrange
        var tid = Guid.NewGuid().ToString("N")[..8];
        await using ApplicationDbContext context = CreateDbContext();

        var userA = new ApplicationUser { Id = $"userA_{tid}", UserName = $"testUserA_{tid}" };
        context.Users.Add(userA);

        var item = new Item { Content = $"Test Item_{tid}", OwnerId = userA.Id, Owner = userA };
        context.Items.Add(item);

        var tag = new Tag { Name = $"MyTag_{tid}", OwnerId = userA.Id, Owner = userA, CachedWeight = 100 };
        context.Tags.Add(tag);

        await context.SaveChangesAsync();

        // Act
        await context.CreateFreeTagRelationAsync(item.Id, tag.Id, userA.Id);

        // Assert
        // 1. リレーションの作成確認
        TagRelation? relation = await context.TagRelations!
            .FirstOrDefaultAsync(tr => tr.ItemId == item.Id && tr.TagId == tag.Id);

        Assert.NotNull(relation);
        Assert.Equal(1, relation.Weight);
        Assert.Equal(userA.Id, relation.OwnerId);

        // 2. 元帳への記帳確認
        TagWeightLedger? ledger = await context.TagWeightLedgers!
            .FirstOrDefaultAsync(l => l.TagId == tag.Id);

        Assert.NotNull(ledger);
        Assert.Equal(userA.Id, ledger.OwnerId);
        Assert.Equal(1, ledger.Delta);
        Assert.Equal("TagRelation", ledger.SourceType);
        Assert.Equal(relation.Id, ledger.SourceId);
        Assert.Equal(100, ledger.PreviousWeight);
        Assert.Equal(101, ledger.NewWeight);
        Assert.True(ledger.IsOwnerAction);
        Assert.Equal($"MyTag_{tid}", ledger.TagNameSnapshot);
        Assert.Equal("Owner Self-Tagging", ledger.Reason);

        // RightAsset が消費されていることの確認
        RightAsset? asset = await context.RightAssets!.IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Id == ledger.ConsumedRightAssetId);
        Assert.NotNull(asset);
        Assert.True(asset.IsBurned);
        Assert.Equal(userA.Id, asset.OwnerId);

        // 3. キャッシュの更新確認
        Tag? updatedTag = await context.Tags!.FindAsync(tag.Id);
        Assert.NotNull(updatedTag);
        Assert.Equal(101, updatedTag.CachedWeight);
    }

    [Fact]
    public async Task CreateFreeTagRelationAsync_ShouldThrowException_WhenUserIsNotTagOwner()
    {
        // Arrange
        var tid = Guid.NewGuid().ToString("N")[..8];
        await using ApplicationDbContext context = CreateDbContext();

        var ownerA = new ApplicationUser { Id = $"userA_{tid}", UserName = $"owner_{tid}" };
        var otherUserB = new ApplicationUser { Id = $"userB_{tid}", UserName = $"other_{tid}" };
        context.Users.AddRange(ownerA, otherUserB);

        var item = new Item { Content = $"Test Item_{tid}", OwnerId = ownerA.Id, Owner = ownerA };
        context.Items.Add(item);

        var tag = new Tag { Name = $"MyTag_{tid}", OwnerId = ownerA.Id, Owner = ownerA, CachedWeight = 100 };
        context.Tags.Add(tag);

        await context.SaveChangesAsync();

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            context.CreateFreeTagRelationAsync(item.Id, tag.Id, otherUserB.Id));
    }

    [Fact]
    public async Task CreateFreeTagRelationAsync_ShouldSucceed_WhenTagOwnerIsSystem()
    {
        // Arrange
        var tid = Guid.NewGuid().ToString("N")[..8];
        await using ApplicationDbContext context = CreateDbContext();

        var systemUser = new ApplicationUser { Id = $"system_{tid}", UserName = $"system_{tid}" };
        var userB = new ApplicationUser { Id = $"userB_{tid}", UserName = $"userB_{tid}" };
        context.Users.AddRange(systemUser, userB);

        var item = new Item { Content = $"User Item_{tid}", OwnerId = userB.Id, Owner = userB };
        context.Items.Add(item);

        var systemTag = new Tag { Name = $"SystemTag_{tid}", OwnerId = systemUser.Id, Owner = systemUser, CachedWeight = 50, IsSystem = true };
        context.Tags.Add(systemTag);

        await context.SaveChangesAsync();

        // Act - userB creates relation with system tag
        await context.CreateFreeTagRelationAsync(item.Id, systemTag.Id, userB.Id);

        // Assert
        TagRelation? relation = await context.TagRelations!
            .FirstOrDefaultAsync(tr => tr.ItemId == item.Id && tr.TagId == systemTag.Id);

        Assert.NotNull(relation);
        Assert.Equal(1, relation.Weight);
        Assert.Equal(userB.Id, relation.OwnerId);

        TagWeightLedger? ledger = await context.TagWeightLedgers!
            .FirstOrDefaultAsync(l => l.TagId == systemTag.Id && l.SourceId == relation.Id);

        Assert.NotNull(ledger);
        Assert.Equal(userB.Id, ledger.OwnerId);
        Assert.Equal(1, ledger.Delta);
        Assert.Equal(50, ledger.PreviousWeight);
        Assert.Equal(51, ledger.NewWeight);
        Assert.False(ledger.IsOwnerAction);
        Assert.Equal("System Classification Tagging", ledger.Reason);

        Tag? updatedTag = await context.Tags!.FindAsync(systemTag.Id);
        Assert.NotNull(updatedTag);
        Assert.Equal(51, updatedTag.CachedWeight);
    }
}