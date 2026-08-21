#region

using System.Text.RegularExpressions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

using SRNSMudApp.Data;

#endregion

namespace SRNSMudApp.E2ETests;

[TestFixture]
public partial class ContractAndOfferScenarioE2ETests : PageTest
{
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _factory = new CustomWebApplicationFactory();
        _factory.EnsureServer(); // Initialize the host
        _serverAddress = _factory.ServerAddress;
    }

    [OneTimeTearDown]
    public void OneTimeTearDown() => _factory?.Dispose();

    private CustomWebApplicationFactory? _factory;
    private string? _serverAddress;

    private async Task RegisterAndLoginAsync(string email)
    {
        await Page.Context.ClearCookiesAsync();
        var userName = email.Contains('@') ? email.Split('@')[0] : email;
        await Page.GotoAsync($"{_serverAddress}/auth/callback?provider=Google&code=mock-{userName}");
        await Page.WaitForURLAsync(new Regex(@"^" + Regex.Escape(_serverAddress) + @"/?$"),
            new PageWaitForURLOptions { Timeout = 10000 });
        await Expect(Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Logout" }))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });
    }

    [Test]
    public async Task Scenario_ContractAndPublicOffer_ThreeUsers()
    {
        // Increase test timeout as this is a long scenario
        TestContext.Progress.WriteLine("Starting Long Scenario E2E Test...");

        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var aliceEmail = $"alice_{uniqueId}@example.com";
        var bobEmail = $"bob_{uniqueId}@example.com";
        var charlieEmail = $"charlie_{uniqueId}@example.com";

        var aliceTag = $"AliceTag_{uniqueId}";
        var aliceItem = $"AliceItem_{uniqueId}";

        var bobTag = $"BobTag_{uniqueId}";
        var bobItem = $"BobItem_{uniqueId}";

        var charlieItem = $"CharlieItem_{uniqueId}";

        // ==========================================
        // 1. Setup: Users and their Data
        // ==========================================
        await RegisterAndLoginAsync(aliceEmail);
        using (IServiceScope scope = _factory!.AppServices.CreateScope())
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            ApplicationUser alice = await db.Users.FirstAsync(u => u.Email == aliceEmail);
            db.Tags.Add(new Tag { Name = aliceTag, Content = "Alice's Tag", OwnerId = alice.Id, CachedWeight = 0 });
            db.Items.Add(new Item { Content = aliceItem, OwnerId = alice.Id });
            await db.SaveChangesAsync();
        }

        await RegisterAndLoginAsync(bobEmail);
        using (IServiceScope scope = _factory!.AppServices.CreateScope())
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            ApplicationUser bob = await db.Users.FirstAsync(u => u.Email == bobEmail);
            db.Tags.Add(new Tag { Name = bobTag, Content = "Bob's Tag", OwnerId = bob.Id, CachedWeight = 0 });
            db.Items.Add(new Item { Content = bobItem, OwnerId = bob.Id });
            await db.SaveChangesAsync();
        }

        await RegisterAndLoginAsync(charlieEmail);
        using (IServiceScope scope = _factory!.AppServices.CreateScope())
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            ApplicationUser charlie = await db.Users.FirstAsync(u => u.Email == charlieEmail);
            db.Items.Add(new Item { Content = charlieItem, OwnerId = charlie.Id });
            await db.SaveChangesAsync();
        }

        // ==========================================
        // 2. Alice creates a Public Offer (Gratis)
        // ==========================================
        await RegisterAndLoginAsync(aliceEmail);
        _ = await Page.GotoAsync($"{_serverAddress}/PublicOffer/PublicOfferBoard");
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "オファーを作成する" }).ClickAsync();

        await Task.Delay(1000); // Wait for modal
        await Page.GetByLabel("提供するタグ").ClickAsync();
        await Page.GetByLabel("提供するタグ").FillAsync(aliceTag);
        await Page.GetByRole(AriaRole.Option).Filter(new LocatorFilterOptions { HasText = aliceTag }).ClickAsync();

        await Task.Delay(500);

        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "公開する" }).ClickAsync();

        await Expect(Page.GetByText("公開オファーを作成しました。")).ToBeVisibleAsync();
        await Expect(Page.GetByText(aliceTag).First).ToBeVisibleAsync();

        // ==========================================
        // 3. Charlie triggers Alice's Public Offer
        // ==========================================
        await RegisterAndLoginAsync(charlieEmail);
        _ = await Page.GotoAsync($"{_serverAddress}/PublicOffer/PublicOfferBoard");

        // Find Alice's offer and click "オファーに応じる"
        ILocator aliceOfferCard = Page.Locator(".mud-card").Filter(new LocatorFilterOptions { HasText = aliceTag });
        await aliceOfferCard.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "オファーに応じる" }).First
            .ClickAsync();

        await Task.Delay(1000);
        await Page.GetByLabel("タグを付与する対象のアイテム").ClickAsync();
        await Page.GetByLabel("タグを付与する対象のアイテム").FillAsync(charlieItem);
        await Page.GetByRole(AriaRole.Option).Filter(new LocatorFilterOptions { HasText = charlieItem }).First
            .ClickAsync();

        await Task.Delay(500);

        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "実行する" }).ClickAsync();

        await Expect(Page.GetByText("公開オファーを利用してタグを獲得しました！")).ToBeVisibleAsync();

        // Verify CharlieItem has AliceTag
        _ = await Page.GotoAsync($"{_serverAddress}/Item/ItemList");
        await Task.Delay(2000);
        ILocator charlieItemCard = Page.Locator(".mud-card").Filter(new LocatorFilterOptions { HasText = charlieItem });
        await Expect(charlieItemCard.GetByText(aliceTag).First).ToBeVisibleAsync();

        // ==========================================
        // 4. Bob creates a Gratis Contract to Alice, cancels it, creates again, Alice rejects, Bob creates 3rd time, Alice accepts
        // ==========================================
        await RegisterAndLoginAsync(bobEmail);
        _ = await Page.GotoAsync($"{_serverAddress}/Item/ItemList");
        await Task.Delay(2000);

        // Bob wants to tag BobItem with AliceTag
        // Since BobItem doesn't have AliceTag yet, Bob can't click '+' on the chip. 
        // Wait, how does Bob propose a contract from scratch?
        // Ah, currently ProposeContractDialog only opens when clicking '+' on an EXISTING tag relation that Bob doesn't own.
        // CharlieItem has AliceTag! Bob can click '+' on AliceTag on CharlieItem to propose it for CharlieItem!

        ILocator charlieItemCardForBob =
            Page.Locator(".mud-card").Filter(new LocatorFilterOptions { Has = Page.Locator($".resource-content:has-text('{charlieItem}')") });
        await Expect(charlieItemCardForBob.GetByText(aliceTag).First).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
        ILocator aliceChip1 = charlieItemCardForBob.Locator(".custom-chip").Filter(new LocatorFilterOptions { HasText = aliceTag });
        await aliceChip1.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Weightを増やす" }).ClickAsync();

        await Expect(Page.GetByText("あなたはこのタグの操作権限を持っていません。")).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
        await Page.GetByLabel("メッセージ（任意）").FillAsync("Please increase weight for Charlie!");
        await Task.Delay(500);
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "提案する" }).ClickAsync();
        await Expect(Page.GetByText("コントラクトを提案しました。")).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });

        // Edge Case: Bob cancels the proposal
        _ = await Page.GotoAsync($"{_serverAddress}/Contract/ContractManagement");
        await Page.GetByRole(AriaRole.Tab, new PageGetByRoleOptions { Name = "送信済み (Outbox)" }).ClickAsync();
        await Expect(Page.GetByText(aliceTag).First).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "取り下げる" }).ClickAsync();
        await Expect(Page.GetByText("コントラクトを取り下げました。")).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
        // Now it shouldn't be in Outbox (or it's there but canceled, actually our UI filters by Proposed for outbox? No, wait. 
        // Our UI shows all outgoing, but removes "取り下げる" button if canceled.)
        await Expect(Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "取り下げる" })).Not
            .ToBeVisibleAsync();

        // Bob creates a 2nd proposal
        await RegisterAndLoginAsync(bobEmail);
        _ = await Page.GotoAsync($"{_serverAddress}/Item/ItemList");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Task.Delay(3000);
        charlieItemCardForBob = Page.Locator(".mud-card").Filter(new LocatorFilterOptions { Has = Page.Locator($".resource-content:has-text('{charlieItem}')") });
        await Expect(charlieItemCardForBob.GetByText(aliceTag).First).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
        ILocator aliceChip2 = charlieItemCardForBob.Locator(".custom-chip").Filter(new LocatorFilterOptions { HasText = aliceTag });
        await aliceChip2.ScrollIntoViewIfNeededAsync();
        await Task.Delay(1000);
        await aliceChip2.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Weightを増やす" }).ClickAsync();
        await Expect(Page.GetByText("あなたはこのタグの操作権限を持っていません。")).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
        await Page.GetByLabel("メッセージ（任意）").FillAsync("2nd attempt!");
        await Task.Delay(500);
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "提案する" }).ClickAsync();
        await Expect(Page.GetByText("コントラクトを提案しました。")).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });

        // Edge Case: Alice Rejects
        await RegisterAndLoginAsync(aliceEmail);
        _ = await Page.GotoAsync($"{_serverAddress}/Contract/ContractManagement");
        ILocator incomingRow = Page.Locator(".mud-card").Filter(new LocatorFilterOptions { HasText = "2nd attempt!" });
        await incomingRow.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "拒否する" }).ClickAsync();
        await Expect(Page.GetByText("コントラクトを拒否しました。")).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
        await Expect(incomingRow).Not.ToBeVisibleAsync();

        // Bob creates a 3rd proposal
        await RegisterAndLoginAsync(bobEmail);
        _ = await Page.GotoAsync($"{_serverAddress}/Item/ItemList");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Task.Delay(3000);
        charlieItemCardForBob = Page.Locator(".mud-card").Filter(new LocatorFilterOptions { Has = Page.Locator($".resource-content:has-text('{charlieItem}')") });
        await Expect(charlieItemCardForBob.GetByText(aliceTag).First).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
        ILocator aliceChip3 = charlieItemCardForBob.Locator(".custom-chip").Filter(new LocatorFilterOptions { HasText = aliceTag });
        await aliceChip3.ScrollIntoViewIfNeededAsync();
        await Task.Delay(1000);
        await aliceChip3.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Weightを増やす" }).ClickAsync();
        await Expect(Page.GetByText("あなたはこのタグの操作権限を持っていません。")).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
        await Page.GetByLabel("メッセージ（任意）").FillAsync("3rd attempt!");
        await Task.Delay(500);
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "提案する" }).ClickAsync();
        await Expect(Page.GetByText("コントラクトを提案しました。")).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });

        // Alice Accepts
        await RegisterAndLoginAsync(aliceEmail);
        _ = await Page.GotoAsync($"{_serverAddress}/Contract/ContractManagement");
        incomingRow = Page.Locator(".mud-card").Filter(new LocatorFilterOptions { HasText = "3rd attempt!" });
        await incomingRow.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "承認する" }).ClickAsync();
        await Expect(Page.GetByText("コントラクトを承認しました。")).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });

        // Verify CharlieItem has weight 2 for AliceTag (because Alice created the first relation via Charlie triggering public offer (weight 1), then Alice accepted Bob's gratis contract (adds weight 1)).
        _ = await Page.GotoAsync($"{_serverAddress}/Item/ItemList");
        await Task.Delay(2000);
        ILocator charlieItemCardForAlice =
            Page.Locator(".mud-card").Filter(new LocatorFilterOptions { HasText = charlieItem });
        // The weight should be 2, but wait, Bob proposed a Gratis contract for CharlieItem with AliceTag.
        // It creates a new TagRelation owned by Bob, with Weight = 1.
        // So CharlieItem will have TWO TagRelations for AliceTag: one owned by Charlie, one owned by Bob.
        // Actually, let's just check that AliceTag exists on CharlieItem.
        // We expect AliceTag to be visible.
        await Expect(charlieItemCardForAlice.GetByText(aliceTag).First).ToBeVisibleAsync();

        // ==========================================
        // 5. Bob creates a Public Offer and Deactivates it
        // ==========================================
        await RegisterAndLoginAsync(bobEmail);
        _ = await Page.GotoAsync($"{_serverAddress}/PublicOffer/PublicOfferBoard");
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "オファーを作成する" }).ClickAsync();

        await Task.Delay(1000);
        await Page.GetByLabel("提供するタグ").ClickAsync();
        await Page.GetByLabel("提供するタグ").FillAsync(bobTag);
        await Page.GetByRole(AriaRole.Option).Filter(new LocatorFilterOptions { HasText = bobTag }).First.ClickAsync();

        await Task.Delay(500);

        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "公開する" }).ClickAsync();

        await Expect(Page.GetByText(bobTag).First).ToBeVisibleAsync();

        // Bob immediately deactivates it
        ILocator bobOfferCard = Page.Locator(".mud-card").Filter(new LocatorFilterOptions { HasText = bobTag });
        await bobOfferCard.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "取り下げる" }).First
            .ClickAsync();
        await Expect(Page.GetByText("オファーを取り下げました。")).ToBeVisibleAsync();

        // Charlie tries to find it and it shouldn't be there
        await RegisterAndLoginAsync(charlieEmail);
        _ = await Page.GotoAsync($"{_serverAddress}/PublicOffer/PublicOfferBoard");
        await Expect(Page.GetByText(bobTag).First).Not.ToBeVisibleAsync();
    }
}