using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Tests.TestSupport;

using Xunit;

namespace SRNSMudApp.Tests.Data;

[Collection(MsSqlCollection.Name)]
public class FreeTagRelationScenarioTests : IAsyncLifetime
{
    private readonly MsSqlContainerFixture _fixture;
    private MsSqlTestDatabase _testDb = null!;

    public FreeTagRelationScenarioTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _testDb = await MsSqlTestDatabase.CreateAsync(_fixture.ConnectionString, nameof(FreeTagRelationScenarioTests));
    }

    public async Task DisposeAsync()
    {
        await _testDb.DisposeAsync();
    }

    private ApplicationDbContext CreateDbContext()
    {
        return new ApplicationDbContext(_testDb.Options);
    }

    [Fact]
    public async Task CreateFreeTagRelationAsync_ShouldCreateRelationAndLedgerWithoutAsset_WhenUserIsTagOwner()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();

        var userA = new ApplicationUser { Id = "userA", UserName = "testUserA" };
        context.Users.Add(userA);

        var item = new Item { Content = "Test Item", OwnerId = "userA", Owner = userA };
        context.Items.Add(item);

        var tag = new Tag { Name = "MyTag", OwnerId = "userA", Owner = userA, CachedWeight = 100 };
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
        Assert.Equal("userA", relation.OwnerId);

        // 2. 元帳への記帳確認
        TagWeightLedger? ledger = await context.TagWeightLedgers!
            .FirstOrDefaultAsync(l => l.TagId == tag.Id);

        Assert.NotNull(ledger);
        Assert.Equal("userA", ledger.OwnerId);
        Assert.Equal(1, ledger.Delta);
        Assert.Equal("TagRelation", ledger.SourceType);
        Assert.Equal(relation.Id, ledger.SourceId);
        // Assert.NotNull(ledger.ConsumedRightAssetId); // Now required
        Assert.Equal(100, ledger.PreviousWeight);
        Assert.Equal(101, ledger.NewWeight);
        Assert.True(ledger.IsOwnerAction);
        Assert.Equal("MyTag", ledger.TagNameSnapshot);
        Assert.Equal("Owner Self-Tagging", ledger.Reason);

        // RightAsset が消費されていることの確認
        RightAsset? asset = await context.RightAssets!.IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Id == ledger.ConsumedRightAssetId);
        Assert.NotNull(asset);
        Assert.True(asset.IsBurned);
        Assert.Equal("userA", asset.OwnerId);

        // 3. キャッシュの更新確認
        Tag? updatedTag = await context.Tags!.FindAsync(tag.Id);
        Assert.NotNull(updatedTag);
        Assert.Equal(101, updatedTag.CachedWeight);
    }

    [Fact]
    public async Task CreateFreeTagRelationAsync_ShouldThrowException_WhenUserIsNotTagOwner()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();

        var ownerA = new ApplicationUser { Id = "userA", UserName = "owner" };
        var otherUserB = new ApplicationUser { Id = "userB", UserName = "other" };
        context.Users.AddRange(ownerA, otherUserB);

        var item = new Item { Content = "Test Item", OwnerId = "userA", Owner = ownerA };
        context.Items.Add(item);

        var tag = new Tag { Name = "MyTag", OwnerId = "userA", Owner = ownerA, CachedWeight = 100 };
        context.Tags.Add(tag);

        await context.SaveChangesAsync();

        // Act & Assert
        // ユーザーB（タグのオーナーではない）が実行しようとすると例外が発生すること
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            context.CreateFreeTagRelationAsync(item.Id, tag.Id, otherUserB.Id));
    }

    [Fact]
    public async Task CreateFreeTagRelationAsync_ShouldSucceed_WhenTagOwnerIsSystem()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();

        var systemUser = new ApplicationUser { Id = "system", UserName = "system" };
        var userB = new ApplicationUser { Id = "userB", UserName = "userB" };
        context.Users.AddRange(systemUser, userB);

        var item = new Item { Content = "User Item", OwnerId = "userB", Owner = userB };
        context.Items.Add(item);

        var systemTag = new Tag { Name = "SystemTag", OwnerId = "system", Owner = systemUser, CachedWeight = 50, IsSystem = true };
        context.Tags.Add(systemTag);

        await context.SaveChangesAsync();

        // Act - userB creates relation with system tag
        await context.CreateFreeTagRelationAsync(item.Id, systemTag.Id, userB.Id);

        // Assert
        TagRelation? relation = await context.TagRelations!
            .FirstOrDefaultAsync(tr => tr.ItemId == item.Id && tr.TagId == systemTag.Id);

        Assert.NotNull(relation);
        Assert.Equal(1, relation.Weight);
        Assert.Equal("userB", relation.OwnerId);

        TagWeightLedger? ledger = await context.TagWeightLedgers!
            .FirstOrDefaultAsync(l => l.TagId == systemTag.Id && l.SourceId == relation.Id);

        Assert.NotNull(ledger);
        Assert.Equal("userB", ledger.OwnerId);
        Assert.Equal(1, ledger.Delta);
        Assert.Equal(50, ledger.PreviousWeight);
        Assert.Equal(51, ledger.NewWeight);
        Assert.True(ledger.IsOwnerAction);
        Assert.Equal("System Tag Tagging", ledger.Reason);

        Tag? updatedTag = await context.Tags!.FindAsync(systemTag.Id);
        Assert.NotNull(updatedTag);
        Assert.Equal(51, updatedTag.CachedWeight);
    }
}