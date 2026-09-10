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
public class TagDiagramQuotedItemE2ETests : PageTest
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
    public async Task TagDiagram_ShowsQuotedItemsTags_WhenItemIdIsProvided()
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

        var tag1 = new Tag { Name = "TagQuotedSource", OwnerId = user.Id };
        var tag2 = new Tag { Name = "TagQuotedTarget", OwnerId = user.Id };
        var tag3 = new Tag { Name = "TagQuotedUnrelated", OwnerId = user.Id };

        db.Tags.AddRange(tag1, tag2, tag3);
        await db.SaveChangesAsync();

        var tt1 = new TaggableTarget { OwnerId = user.Id };
        var tt2 = new TaggableTarget { OwnerId = user.Id };
        db.TaggableTargets.AddRange(tt1, tt2);
        await db.SaveChangesAsync();

        var itemB = new Item { Content = "This is the quoted item", OwnerId = user.Id, TagTargetId = tt2.Id };
        db.Items.Add(itemB);
        await db.SaveChangesAsync();

        db.TagRelations.Add(new TagRelation { TagId = tag2.Id, ItemId = itemB.Id, OwnerId = user.Id, Weight = 1 });
        await db.SaveChangesAsync();

        var itemA = new Item
        {
            Content = "This item quotes the other item, but has no link in text.",
            OwnerId = user.Id,
            TagTargetId = tt1.Id,
            QuotedItemId = itemB.Id // Quoting Item B
        };
        db.Items.Add(itemA);
        await db.SaveChangesAsync();

        db.TagRelations.Add(new TagRelation { TagId = tag1.Id, ItemId = itemA.Id, OwnerId = user.Id, Weight = 1 });

        // Add edges so they count as connected
        var edge1 = new TagEdge { SourceTagId = tag1.Id, TargetTagId = tag3.Id, OwnerId = user.Id };
        db.TagEdges.Add(edge1);
        await db.SaveChangesAsync();

        await Page.GotoAsync($"{_serverAddress}/auth/callback?provider=Google&code=mock-google-test");
        await Page.WaitForURLAsync(new Regex(@"^" + Regex.Escape(_serverAddress) + @"/?$"), new PageWaitForURLOptions { Timeout = 10000 });

        await Page.GotoAsync($"{_serverAddress}/tag-diagram?itemId={itemA.Id}");

        await Expect(Page.Locator(".tag-node").First).ToBeVisibleAsync();

        var tagSource = Page.Locator("text=TagQuotedSource").First;
        var tagTarget = Page.Locator("text=TagQuotedTarget").First;
        var tagUnrelated = Page.Locator("text=TagQuotedUnrelated").First;

        await Expect(tagSource).ToBeVisibleAsync();

        // This should pass now because we added QuotedItemId to the extracted context tags!
        await Expect(tagTarget).ToBeVisibleAsync();

        await Expect(tagUnrelated).Not.ToBeVisibleAsync();
    }
}