#region

using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Tests.TestSupport;

using Xunit;

#endregion

namespace SRNSMudApp.Tests.Data;

public class TagWeightLimitTests : IAsyncLifetime
{
    private MsSqlTestDatabase _sharedDb = null!;

    public async Task InitializeAsync()
    {
        _sharedDb = await SharedMsSqlTestDatabase.GetInstanceAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private ApplicationDbContext CreateDbContext() => new(_sharedDb.Options);

    [Fact]
    public async Task SaveChangesAsync_LimitsWeightTo1_ForGoodAndBadSystemTags_TagRelation()
    {
        var tid = Guid.NewGuid().ToString("N")[..8];
        await using ApplicationDbContext context = CreateDbContext();

        var userId = $"user_{tid}";
        var owner = new ApplicationUser { Id = userId, UserName = $"testuser_{tid}" };
        _ = context.Users.Add(owner);

        var item = new Item { Content = $"Test Item_{tid}", OwnerId = userId, Owner = owner };
        _ = context.Items.Add(item);

        var goodTag = new Tag { Name = "good", IsSystem = true, OwnerId = userId, Owner = owner };
        var badTag = new Tag { Name = "bad", IsSystem = true, OwnerId = userId, Owner = owner };
        var normalTag = new Tag { Name = "normal", IsSystem = false, OwnerId = userId, Owner = owner };
        var otherSystemTag = new Tag { Name = "other", IsSystem = true, OwnerId = userId, Owner = owner };

        context.Tags.AddRange(goodTag, badTag, normalTag, otherSystemTag);
        _ = await context.SaveChangesAsync();

        // Act
        var goodRelation = new TagRelation
        {
            ItemId = item.Id,
            TagId = goodTag.Id,
            OwnerId = userId,
            Owner = owner,
            Weight = 5
        };
        var badRelation = new TagRelation
        {
            ItemId = item.Id,
            TagId = badTag.Id,
            OwnerId = userId,
            Owner = owner,
            Weight = 10
        };
        var normalRelation = new TagRelation
        {
            ItemId = item.Id,
            TagId = normalTag.Id,
            OwnerId = userId,
            Owner = owner,
            Weight = 3
        };
        var otherRelation = new TagRelation
        {
            ItemId = item.Id,
            TagId = otherSystemTag.Id,
            OwnerId = userId,
            Owner = owner,
            Weight = 7
        };

        context.TagRelations.AddRange(goodRelation, badRelation, normalRelation, otherRelation);
        _ = await context.SaveChangesAsync();

        // Assert
        Assert.Equal(1, goodRelation.Weight);
        Assert.Equal(1, badRelation.Weight);
        Assert.Equal(3, normalRelation.Weight);
        Assert.Equal(7, otherRelation.Weight);
    }

    [Fact]
    public async Task SaveChangesAsync_LimitsWeightTo1_ForGoodAndBadSystemTags_TagRelationToTag()
    {
        var tid = Guid.NewGuid().ToString("N")[..8];
        await using ApplicationDbContext context = CreateDbContext();

        var userId = $"user_{tid}";
        var owner = new ApplicationUser { Id = userId, UserName = $"testuser_{tid}" };
        _ = context.Users.Add(owner);

        var targetTag = new Tag { Name = "target", IsSystem = false, OwnerId = userId, Owner = owner };
        var goodTag = new Tag { Name = "good", IsSystem = true, OwnerId = userId, Owner = owner };
        var badTag = new Tag { Name = "bad", IsSystem = true, OwnerId = userId, Owner = owner };
        var normalTag = new Tag { Name = "normal", IsSystem = false, OwnerId = userId, Owner = owner };

        context.Tags.AddRange(targetTag, goodTag, badTag, normalTag);
        _ = await context.SaveChangesAsync();

        // Act
        var goodRelation = new TagRelationToTag
        {
            TargetTagId = targetTag.Id,
            TagId = goodTag.Id,
            OwnerId = userId,
            Owner = owner,
            Weight = 5
        };
        var badRelation = new TagRelationToTag
        {
            TargetTagId = targetTag.Id,
            TagId = badTag.Id,
            OwnerId = userId,
            Owner = owner,
            Weight = 10
        };
        var normalRelation = new TagRelationToTag
        {
            TargetTagId = targetTag.Id,
            TagId = normalTag.Id,
            OwnerId = userId,
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