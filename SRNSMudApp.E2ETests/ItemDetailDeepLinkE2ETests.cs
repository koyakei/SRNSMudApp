using System.Text.RegularExpressions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

using SRNSMudApp.Data;

namespace SRNSMudApp.E2ETests;

[TestFixture]
public class ItemDetailDeepLinkE2ETests : PageTest
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

    private async Task<(Item targetItem, TaggingRequestEntity request)> SetupTestDataAsync()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var email = $"testuser_deeplink_{uniqueId}@example.com";

        // Register user
        await Page.Context.ClearCookiesAsync();
        var userName = email.Contains('@') ? email.Split('@')[0] : email;
        await Page.GotoAsync($"{_serverAddress}/auth/callback?provider=Google&code=mock-{userName}");
        await Page.WaitForURLAsync(new Regex(@"^" + Regex.Escape(_serverAddress) + @"/?$"),
            new PageWaitForURLOptions { Timeout = 10000 });
        await Expect(Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Logout" }))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });

        using IServiceScope scope = _factory!.AppServices.CreateScope();
        ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        ApplicationUser user = await db.Users.FirstAsync(u => u.Email == email);

        var tag = new Tag { Name = $"Tag {uniqueId}", Content = "Test", OwnerId = user.Id, CachedWeight = 0 };
        db.Tags.Add(tag);

        var item = new Item { Content = $"Item {uniqueId}", OwnerId = user.Id };
        db.Items.Add(item);

        await db.SaveChangesAsync();

        var request = new GratisTaggingContract
        {
            OwnerId = user.Id,
            RequesterUserId = user.Id,
            TagOwnerUserId = user.Id,
            TargetItemId = item.Id,
            RequestedTagId = tag.Id,
            RequestType = TaggingRequestType.Add,
            Status = TradeStatus.Proposed
        };
        db.TaggingRequestEntities!.Add(request);
        await db.SaveChangesAsync();

        return (item, request);
    }

    [Test]
    public async Task DeepLink_StateToUrl_UpdatesUrlOnInteraction()
    {
        (Item item, TaggingRequestEntity request) = await SetupTestDataAsync();

        // Navigate to ItemDetail
        await Page.GotoAsync($"{_serverAddress}/ItemDetail/{item.Id}");
        await Task.Delay(1000);

        // Click the "関連リクエスト (Related Requests)" tab
        await Page.GetByRole(AriaRole.Tab, new PageGetByRoleOptions { Name = "関連リクエスト (Related Requests)" })
            .ClickAsync();

        // Wait for URL to update with ?tab=requests
        await Expect(Page).ToHaveURLAsync(new Regex(@"tab=requests"));

        // Wait for the request list to load
        await Task.Delay(1000);

        // Click the request row specifically
        await Page.Locator("td").Filter(new LocatorFilterOptions { HasText = request.Owner!.UserName! }).First
            .ClickAsync();

        // Wait for URL to update with &requestId=...
        await Expect(Page).ToHaveURLAsync(new Regex($@"requestId={request.Id}"));
    }

    [Test]
    public async Task DeepLink_UrlToState_RestoresStateFromUrl()
    {
        (Item item, TaggingRequestEntity request) = await SetupTestDataAsync();

        // Navigate to ItemDetail with query parameters
        await Page.GotoAsync($"{_serverAddress}/ItemDetail/{item.Id}?tab=requests&requestId={request.Id}");
        await Task.Delay(2000);

        // Verify the active tab is "関連リクエスト" (mud-tab-active class)
        ILocator tab = Page.GetByRole(AriaRole.Tab, new PageGetByRoleOptions { Name = "関連リクエスト (Related Requests)" });
        await Expect(tab).ToHaveClassAsync(new Regex("mud-tab-active"));

        // Verify the specific request row is selected (mud-table-row-selected class)
        // Find the row containing the request data
        ILocator row = Page.Locator("tr").Filter(new LocatorFilterOptions { HasText = request.Owner!.UserName! });
        await Expect(row).ToHaveClassAsync(new Regex("mud-table-row-selected"));
    }
}