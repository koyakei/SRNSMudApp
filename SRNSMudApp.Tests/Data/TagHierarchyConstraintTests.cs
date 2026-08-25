using Microsoft.EntityFrameworkCore;
using SRNSMudApp.Data;
using SRNSMudApp.Tests.TestSupport;
using Xunit;

namespace SRNSMudApp.Tests.Data;

public class TagHierarchyConstraintTests : IClassFixture<MsSqlContainerFixture>
{
    private readonly MsSqlContainerFixture _fixture;

    public TagHierarchyConstraintTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RootTag_CanBeCreated_WithUniversalName()
    {
        await using var db = await MsSqlTestDatabase.CreateAsync(_fixture.ConnectionString);
        await using var context = new ApplicationDbContext(db.Options);

        var rootTag = await context.Tags.FirstOrDefaultAsync(t => t.Name == Tag.RootTagName);
        Assert.NotNull(rootTag);
        Assert.Equal(HierarchyId.GetRoot(), rootTag.Node);
    }

    [Fact]
    public async Task NonRootTag_CannotBeCreated_WithGetRootNode()
    {
        await using var db = await MsSqlTestDatabase.CreateAsync(_fixture.ConnectionString);
        await using var context = new ApplicationDbContext(db.Options);

        var invalidTag = new Tag
        {
            Name = "InvalidRootTag",
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
        await using var db = await MsSqlTestDatabase.CreateAsync(_fixture.ConnectionString);
        await using var context = new ApplicationDbContext(db.Options);

        var rootTag = await context.Tags.FirstAsync(t => t.Name == Tag.RootTagName);

        var childTag = new Tag
        {
            Name = "ValidChildTag",
            OwnerId = "system_root",
            ParentTagId = rootTag.Id,
            Node = rootTag.Node.GetDescendant(null, null),
            CreatedDate = DateTime.UtcNow,
            UpdatedDate = DateTime.UtcNow
        };

        context.Tags.Add(childTag);
        await context.SaveChangesAsync();

        var savedTag = await context.Tags.FirstOrDefaultAsync(t => t.Name == "ValidChildTag");
        Assert.NotNull(savedTag);
        Assert.True(savedTag.Node.IsDescendantOf(rootTag.Node));
        Assert.NotEqual(HierarchyId.GetRoot(), savedTag.Node);
    }
}
