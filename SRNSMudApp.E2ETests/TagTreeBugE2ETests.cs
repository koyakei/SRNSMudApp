#region

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

using SRNSMudApp.Data;

#endregion

namespace SRNSMudApp.E2ETests;

[TestFixture]
public class TagTreeBugE2ETests : PageTest
{
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _factory = new CustomWebApplicationFactory();
        _factory.EnsureServer();
        _serverAddress = _factory.ServerAddress;
    }

    [OneTimeTearDown]
    public void OneTimeTearDown() => _factory?.Dispose();

    private CustomWebApplicationFactory? _factory;
    private string? _serverAddress;

    [Test]
    public async Task TagTree_DisplaysCorrectly_WhenSingleRootNodeHasMultipleChildren()
    {
        using IServiceScope scope = _factory.AppServices.CreateScope();
        ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Ensure user exists
        if (!db.Users.Any(u => u.Id == "testuser"))
        {
            _ = db.Users.Add(new ApplicationUser { Id = "testuser", UserName = "testuser" });
            _ = await db.SaveChangesAsync();
        }

        // Clear existing tags
        db.Tags.RemoveRange(db.Tags);
        _ = await db.SaveChangesAsync();

        var rootTag = new Tag { Name = "BugRoot", OwnerId = "testuser", IsSystem = false };
        _ = db.Tags.Add(rootTag);
        _ = await db.SaveChangesAsync();

        var child1 = new Tag { Name = "Child1", ParentTagId = rootTag.Id, OwnerId = "testuser", IsSystem = false };
        var child2 = new Tag { Name = "Child2", ParentTagId = rootTag.Id, OwnerId = "testuser", IsSystem = false };
        var child3 = new Tag { Name = "Child3", ParentTagId = rootTag.Id, OwnerId = "testuser", IsSystem = false };
        db.Tags.AddRange(child1, child2, child3);
        _ = await db.SaveChangesAsync();

        _ = await Page.GotoAsync($"{_serverAddress}/tag-tree");
        _ = await Page.WaitForSelectorAsync("#jqtree-container .jqtree-tree");

        // Verify that the tree elements are visible
        ILocator rootNode = Page.Locator("text=BugRoot");
        await Expect(rootNode).ToBeVisibleAsync();

        ILocator child1Node = Page.Locator("text=Child1");
        await Expect(child1Node).ToBeVisibleAsync();
    }

    [Test]
    public async Task TagTree_DisplaysTags_WhenSearchFieldIsEmpty()
    {
        using IServiceScope scope = _factory.AppServices.CreateScope();
        ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Ensure user exists
        if (!db.Users.Any(u => u.Id == "testuser"))
        {
            _ = db.Users.Add(new ApplicationUser { Id = "testuser", UserName = "testuser" });
            _ = await db.SaveChangesAsync();
        }

        // Clear existing tags
        db.Tags.RemoveRange(db.Tags);
        _ = await db.SaveChangesAsync();

        var tags = new List<Tag>();
        for (var i = 0; i < 15; i++)
        {
            tags.Add(new Tag { Name = $"EmptySearchTag_{i}", OwnerId = "testuser", IsSystem = false });
        }

        db.Tags.AddRange(tags);
        _ = await db.SaveChangesAsync();

        _ = await Page.GotoAsync($"{_serverAddress}/tag-tree");
        _ = await Page.WaitForSelectorAsync("#jqtree-container .jqtree-tree");

        // Verify tags are visible without entering any search text
        for (var i = 0; i < 15; i++)
        {
            ILocator node = Page.GetByText($"EmptySearchTag_{i}", new PageGetByTextOptions { Exact = true });
            await Expect(node).ToBeVisibleAsync();
        }
    }
}