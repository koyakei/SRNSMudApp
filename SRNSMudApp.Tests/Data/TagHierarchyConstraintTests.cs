using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Tests.TestSupport;

using Xunit;

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
}