using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

using SRNSMudApp.Data;

namespace SRNSMudApp.E2ETests;

[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class TagDiagramContextE2ETests : PageTest
{
    private CustomWebApplicationFactory _factory = null!;
    private string _serverAddress = "";

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _factory = SharedTestServerFixture.Factory;
        _serverAddress = SharedTestServerFixture.ServerAddress;
    }

    [Test]
    public async Task TagDiagram_ShowsLinkedItemsAndTags_WhenItemIdIsProvided()
    {
        var userEmail = "mock-google-test@example.com";
        var dbFactory = _factory.AppServices.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();

        await using var db = await dbFactory.CreateDbContextAsync();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == userEmail);
        if (user == null)
        {
            user = new ApplicationUser { UserName = userEmail, Email = userEmail };
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        var tag1 = new Tag { Name = "TagA", OwnerId = user.Id };
        var tag2 = new Tag { Name = "TagB", OwnerId = user.Id };
        var tag3 = new Tag { Name = "TagC", OwnerId = user.Id };
        var tag4 = new Tag { Name = "TagUnrelated", OwnerId = user.Id };

        db.Tags.AddRange(tag1, tag2, tag3, tag4);
        await db.SaveChangesAsync();

        var tt1 = new TaggableTarget { OwnerId = user.Id };
        var tt2 = new TaggableTarget { OwnerId = user.Id };
        db.TaggableTargets.AddRange(tt1, tt2);
        await db.SaveChangesAsync();

        var item2 = new Item { Content = "Target item that is internally linked", OwnerId = user.Id, TagTargetId = tt2.Id };
        db.Items.Add(item2);
        await db.SaveChangesAsync();

        db.TagRelations.Add(new TagRelation { TagId = tag3.Id, ItemId = item2.Id, OwnerId = user.Id, Weight = 1 });
        await db.SaveChangesAsync();

        var item1 = new Item
        {
            Content = $"This is my item. Link to tag /TagDetail/{tag2.Id} and item /ItemDetail/{item2.Id} here.",
            OwnerId = user.Id,
            TagTargetId = tt1.Id
        };
        db.Items.Add(item1);
        await db.SaveChangesAsync();

        db.TagRelations.Add(new TagRelation { TagId = tag1.Id, ItemId = item1.Id, OwnerId = user.Id, Weight = 1 });

        var edge1 = new TagEdge { SourceTagId = tag1.Id, TargetTagId = tag2.Id, OwnerId = user.Id };
        var edge2 = new TagEdge { SourceTagId = tag2.Id, TargetTagId = tag3.Id, OwnerId = user.Id };
        var edge3 = new TagEdge { SourceTagId = tag3.Id, TargetTagId = tag4.Id, OwnerId = user.Id };
        db.TagEdges.AddRange(edge1, edge2, edge3);
        await db.SaveChangesAsync();

        await Page.GotoAsync($"{_serverAddress}/auth/callback?provider=Google&code=mock-google-test");
        await Page.WaitForURLAsync(new Regex(@"^" + Regex.Escape(_serverAddress) + @"/?$"), new PageWaitForURLOptions { Timeout = 10000 });

        await Page.GotoAsync($"{_serverAddress}/tag-diagram?itemId={item1.Id}");

        await Expect(Page.Locator(".tag-node").First).ToBeVisibleAsync();

        var tagA = Page.Locator("text=TagA").First;
        var tagB = Page.Locator("text=TagB").First;
        var tagC = Page.Locator("text=TagC").First;
        var tagUnrelated = Page.Locator("text=TagUnrelated").First;

        await Expect(tagA).ToBeVisibleAsync();
        await Expect(tagB).ToBeVisibleAsync();
        await Expect(tagC).ToBeVisibleAsync();
        await Expect(tagUnrelated).Not.ToBeVisibleAsync();
    }
}