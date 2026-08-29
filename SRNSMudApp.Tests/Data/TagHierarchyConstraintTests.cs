using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;

namespace SRNSMudApp.Tests.Data;

public class TagHierarchyConstraintTests : IAsyncLifetime
{
    private MsSqlTestDatabase _sharedDb = null!;

    public async Task InitializeAsync()
    {
        _sharedDb = await SharedMsSqlTestDatabase.GetInstanceAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task RootTag_CanBeCreated_WithUniversalName()
    {
        await using var context = new ApplicationDbContext(_sharedDb.Options);

        var rootTag = await context.Tags.FirstOrDefaultAsync(t => t.Name == Tag.RootTagName);
        Assert.NotNull(rootTag);
        Assert.Equal(HierarchyId.GetRoot(), rootTag.Node);
    }

    [Fact]
    public async Task NonRootTag_CannotBeCreated_WithGetRootNode()
    {
        var tid = Guid.NewGuid().ToString("N")[..8];
        await using var context = new ApplicationDbContext(_sharedDb.Options);

        var invalidTag = new Tag
        {
            Name = $"InvalidRootTag_{tid}",
            OwnerId = "system_root",
            Node = HierarchyId.GetRoot(),
            CreatedDate = DateTime.UtcNow,
            UpdatedDate = DateTime.UtcNow
        };

        context.Tags.Add(invalidTag);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        Assert.Contains(Tag.RootTagName, exception.Message);
    }

    [Fact]
    public async Task ChildTag_CanBeCreated_UnderRootTag()
    {
        var tid = Guid.NewGuid().ToString("N")[..8];
        await using var context = new ApplicationDbContext(_sharedDb.Options);

        var rootTag = await context.Tags.FirstAsync(t => t.Name == Tag.RootTagName);

        var childTag = new Tag
        {
            Name = $"ValidChildTag_{tid}",
            OwnerId = "system_root",
            ParentTagId = rootTag.Id,
            Node = rootTag.Node.GetDescendant(null, null),
            CreatedDate = DateTime.UtcNow,
            UpdatedDate = DateTime.UtcNow
        };

        context.Tags.Add(childTag);
        await context.SaveChangesAsync();

        var savedTag = await context.Tags.FirstOrDefaultAsync(t => t.Name == $"ValidChildTag_{tid}");
        Assert.NotNull(savedTag);
        Assert.True(savedTag.Node.IsDescendantOf(rootTag.Node));
        Assert.NotEqual(HierarchyId.GetRoot(), savedTag.Node);
    }

    [Fact]
    public async Task SaveChangesAsync_AddsMultipleTagsWithoutNode_AssignsUniqueNodes()
    {
        var tid = Guid.NewGuid().ToString("N")[..8];
        var testUser = $"testuser_{tid}";

        await using var context = new ApplicationDbContext(_sharedDb.Options);
        await context.SeedUsersAsync(testUser);

        var rootTag = await context.Tags.FirstAsync(t => t.Name == Tag.RootTagName);

        // Node を指定せずに複数のタグを一度に追加
        var tag1 = new Tag { Name = $"AutoNode1_{tid}", OwnerId = testUser };
        var tag2 = new Tag { Name = $"AutoNode2_{tid}", OwnerId = testUser };
        var tag3 = new Tag { Name = $"AutoNode3_{tid}", OwnerId = testUser };

        context.Tags.AddRange(tag1, tag2, tag3);
        await context.SaveChangesAsync();

        var savedTag1 = await context.Tags.SingleAsync(t => t.Id == tag1.Id);
        var savedTag2 = await context.Tags.SingleAsync(t => t.Id == tag2.Id);
        var savedTag3 = await context.Tags.SingleAsync(t => t.Id == tag3.Id);

        // ParentTagId が rootTag.Id に自動設定されていること
        Assert.Equal(rootTag.Id, savedTag1.ParentTagId);
        Assert.Equal(rootTag.Id, savedTag2.ParentTagId);
        Assert.Equal(rootTag.Id, savedTag3.ParentTagId);

        // Node が自動採番され、すべて一意かつ rootTag の子孫であること
        Assert.NotNull(savedTag1.Node);
        Assert.NotNull(savedTag2.Node);
        Assert.NotNull(savedTag3.Node);

        Assert.NotEqual(savedTag1.Node, savedTag2.Node);
        Assert.NotEqual(savedTag2.Node, savedTag3.Node);
        Assert.NotEqual(savedTag1.Node, savedTag3.Node);

        Assert.True(savedTag1.Node.IsDescendantOf(rootTag.Node));
        Assert.True(savedTag2.Node.IsDescendantOf(rootTag.Node));
        Assert.True(savedTag3.Node.IsDescendantOf(rootTag.Node));
    }
}