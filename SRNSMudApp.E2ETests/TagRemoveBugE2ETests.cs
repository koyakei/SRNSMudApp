using System.Text.RegularExpressions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

using SRNSMudApp.Data;

namespace SRNSMudApp.E2ETests;

[TestFixture]
public class TagRemoveBugE2ETests : PageTest
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
    public async Task ApprovingRemoveRequest_ShouldRemoveTagging()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var tagOwnerEmail = $"tagowner_{uniqueId}@example.com";
        var itemOwnerEmail = $"itemowner_{uniqueId}@example.com";
        var tagName = $"BugTag_{uniqueId}";
        var itemContent = $"BugItem_{uniqueId}";

        // 1. User1 (TagOwner) logs in and gets created in DB
        await Page.Context.ClearCookiesAsync();
        var userName1 = tagOwnerEmail.Split('@')[0];
        await Page.GotoAsync($"{_serverAddress}/auth/callback?provider=Google&code=mock-{userName1}");
        await Page.WaitForURLAsync(new Regex(@"^" + Regex.Escape(_serverAddress) + @"/?$"));

        // 2. User2 (ItemOwner) logs in and gets created in DB
        await Page.Context.ClearCookiesAsync();
        var userName2 = itemOwnerEmail.Split('@')[0];
        await Page.GotoAsync($"{_serverAddress}/auth/callback?provider=Google&code=mock-{userName2}");
        await Page.WaitForURLAsync(new Regex(@"^" + Regex.Escape(_serverAddress) + @"/?$"));

        // 3. Setup Data in DB
        int tagId, itemId;
        using (IServiceScope scope = _factory!.AppServices.CreateScope())
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            ApplicationUser tagOwner = await db.Users.FirstAsync(u => u.Email == tagOwnerEmail);
            ApplicationUser itemOwner = await db.Users.FirstAsync(u => u.Email == itemOwnerEmail);

            var tag = new Tag { Name = tagName, Content = "Test", OwnerId = tagOwner.Id, CachedWeight = 1 };
            db.Tags.Add(tag);

            var item = new Item { Content = itemContent, OwnerId = itemOwner.Id };
            db.Items.Add(item);
            await db.SaveChangesAsync();

            tagId = tag.Id;
            itemId = item.Id;

            // User1 (TagOwner) adds the tag to User2's Item
            var relation = new TagRelation
            {
                ItemId = item.Id,
                TagId = tag.Id,
                OwnerId = tagOwner.Id, // Owned by TagOwner
                Weight = 1
            };
            db.TagRelations.Add(relation);

            // User2 (ItemOwner) creates a Remove request
            var contract = new GratisTaggingContract
            {
                OwnerId = itemOwner.Id,
                RequesterUserId = itemOwner.Id,
                TagOwnerUserId = tagOwner.Id,
                TargetItemId = item.Id,
                RequestedTagId = tag.Id,
                Status = TradeStatus.Proposed,
                RequesterMessage = "Please remove this tag",
                RequestType = TaggingRequestType.Remove
            };
            db.TaggingRequestEntities.Add(contract);

            await db.SaveChangesAsync();
        }

        // 4. User1 (TagOwner) logs in to approve the request
        await Page.Context.ClearCookiesAsync();
        await Page.GotoAsync($"{_serverAddress}/auth/callback?provider=Google&code=mock-{userName1}");
        await Page.WaitForURLAsync(new Regex(@"^" + Regex.Escape(_serverAddress) + @"/?$"));

        // Navigate to TagDetail page to see requests
        await Page.GotoAsync($"{_serverAddress}/TagDetail/{tagId}");
        await Task.Delay(2000);

        // Click on "リクエスト" tab
        await Page.Locator(".mud-tab").Filter(new LocatorFilterOptions { HasText = "リクエスト" }).ClickAsync();
        await Task.Delay(1000);

        // Find the "承認" (Approve) button and click it
        ILocator approveButton = Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "承認" }).First;
        await Expect(approveButton).ToBeVisibleAsync();
        await approveButton.ClickAsync();

        // Wait for success snackbar
        await Expect(Page.Locator("text=リクエストを承認しました。")).ToBeVisibleAsync();
        await Task.Delay(1000);

        // 5. Verify the tag relation is removed from DB
        using (IServiceScope scope = _factory!.AppServices.CreateScope())
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var relationExists = await db.TagRelations.AnyAsync(tr => tr.ItemId == itemId && tr.TagId == tagId);

            // This assertion should fail if the bug exists
            Assert.That(relationExists, Is.False, "The tag relation should have been removed after approval.");
        }
    }
}