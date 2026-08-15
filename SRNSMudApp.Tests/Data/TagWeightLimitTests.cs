#region

using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;

#endregion

namespace SRNSMudApp.Tests.Data;

public class TagWeightLimitTests
{
    private static ApplicationDbContext CreateDbContext()
    {
        DbContextOptions<ApplicationDbContext> options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()) // Unique DB for each test
            .Options;

        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task SaveChangesAsync_LimitsWeightTo1_ForGoodAndBadSystemTags_TagRelation()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();

        var owner = new ApplicationUser { Id = "user1", UserName = "testuser" };
        _ = context.Users.Add(owner);

        var item = new Item { Content = "Test Item", OwnerId = "user1", Owner = owner };
        _ = context.Items.Add(item);

        var goodTag = new Tag { Name = "good", IsSystem = true, OwnerId = "user1", Owner = owner };
        var badTag = new Tag { Name = "bad", IsSystem = true, OwnerId = "user1", Owner = owner };
        var normalTag = new Tag { Name = "normal", IsSystem = false, OwnerId = "user1", Owner = owner };
        var otherSystemTag = new Tag { Name = "other", IsSystem = true, OwnerId = "user1", Owner = owner };

        context.Tags.AddRange(goodTag, badTag, normalTag, otherSystemTag);
        _ = await context.SaveChangesAsync(); // Save base entities first to get IDs

        // Act
        var goodRelation = new TagRelation
        {
            ItemId = item.Id,
            TagId = goodTag.Id,
            OwnerId = "user1",
            Owner = owner,
            Weight = 5
        };
        var badRelation = new TagRelation
        {
            ItemId = item.Id,
            TagId = badTag.Id,
            OwnerId = "user1",
            Owner = owner,
            Weight = 10
        };
        var normalRelation = new TagRelation
        {
            ItemId = item.Id,
            TagId = normalTag.Id,
            OwnerId = "user1",
            Owner = owner,
            Weight = 3
        };
        var otherRelation = new TagRelation
        {
            ItemId = item.Id,
            TagId = otherSystemTag.Id,
            OwnerId = "user1",
            Owner = owner,
            Weight = 7
        };

        context.TagRelations.AddRange(goodRelation, badRelation, normalRelation, otherRelation);
        _ = await context.SaveChangesAsync(); // This should trigger the weight limit enforcement

        // Assert
        Assert.Equal(1, goodRelation.Weight);
        Assert.Equal(1, badRelation.Weight);
        Assert.Equal(3, normalRelation.Weight);
        Assert.Equal(7, otherRelation.Weight);
    }

    [Fact]
    public async Task SaveChangesAsync_LimitsWeightTo1_ForGoodAndBadSystemTags_TagRelationToTag()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();

        var owner = new ApplicationUser { Id = "user1", UserName = "testuser" };
        _ = context.Users.Add(owner);

        var targetTag = new Tag { Name = "target", IsSystem = false, OwnerId = "user1", Owner = owner };
        var goodTag = new Tag { Name = "good", IsSystem = true, OwnerId = "user1", Owner = owner };
        var badTag = new Tag { Name = "bad", IsSystem = true, OwnerId = "user1", Owner = owner };
        var normalTag = new Tag { Name = "normal", IsSystem = false, OwnerId = "user1", Owner = owner };

        context.Tags.AddRange(targetTag, goodTag, badTag, normalTag);
        _ = await context.SaveChangesAsync();

        // Act
        var goodRelation = new TagRelationToTag
        {
            TargetTagId = targetTag.Id,
            TagId = goodTag.Id,
            OwnerId = "user1",
            Owner = owner,
            Weight = 5
        };
        var badRelation = new TagRelationToTag
        {
            TargetTagId = targetTag.Id,
            TagId = badTag.Id,
            OwnerId = "user1",
            Owner = owner,
            Weight = 10
        };
        var normalRelation = new TagRelationToTag
        {
            TargetTagId = targetTag.Id,
            TagId = normalTag.Id,
            OwnerId = "user1",
            Owner = owner,
            Weight = 3
        };

        context.TagRelationToTags.AddRange(goodRelation, badRelation, normalRelation);
        _ = await context.SaveChangesAsync();

        // Assert
        Assert.Equal(1, goodRelation.Weight);
        Assert.Equal(1, badRelation.Weight);
        Assert.Equal(3, normalRelation.Weight);
    }
}