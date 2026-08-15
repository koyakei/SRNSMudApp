#region

using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

using SRNSMudApp.Data;
using Microsoft.EntityFrameworkCore;

#endregion

namespace SRNSMudApp.E2ETests;

[TestFixture]
public partial class PublicOfferE2ETests : PageTest
{
    private CustomWebApplicationFactory? _factory;
    private string? _serverAddress;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _factory = new CustomWebApplicationFactory();
        _factory.EnsureServer(); // Initialize the host
        _serverAddress = _factory.ServerAddress;
    }

    [OneTimeTearDown]
    public void OneTimeTearDown() => _factory?.Dispose();

    private async Task RegisterAndLoginAsync(string email, string password)
    {
        await Page.Context.ClearCookiesAsync();
        var userName = email.Contains('@') ? email.Split('@')[0] : email;
        await Page.GotoAsync($"{_serverAddress}/auth/callback?provider=Google&code=mock-{userName}");
        await Page.WaitForURLAsync(new Regex(@"^" + Regex.Escape(_serverAddress) + @"/?$"), new PageWaitForURLOptions { Timeout = 10000 });
        await Expect(Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Logout" })).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });
    }

    [Test]
    public async Task PublicOffer_CreateAndTrigger()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var userAEmail = $"usera_{uniqueId}@example.com";
        var userBEmail = $"userb_{uniqueId}@example.com";
        const string password = "Password123!";

        var tagName = $"OfferTag_{uniqueId}";
        var itemContent = $"OfferItem_{uniqueId}";

        // --- User A (Offer Creator) ---
        await RegisterAndLoginAsync(userAEmail, password);

        using (var scope = _factory!.AppServices.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userA = await db.Users.FirstAsync(u => u.Email == userAEmail);
            var userAId = userA.Id;

            var tag = new Tag { Name = tagName, Content = "Test Tag", OwnerId = userAId, CachedWeight = 0 };
            db.Tags.Add(tag);
            await db.SaveChangesAsync();
        }

        // Create Public Offer (Gratis)
        _ = await Page.GotoAsync($"{_serverAddress}/PublicOffer/PublicOfferBoard");
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "オファーを作成する" }).ClickAsync();
        
        await Task.Delay(1000);
        await Page.GetByLabel("提供するタグ").ClickAsync();
        await Page.GetByLabel("提供するタグ").FillAsync(tagName);
        await Page.GetByRole(AriaRole.Option).Filter(new LocatorFilterOptions { HasText = tagName }).ClickAsync();
        
        await Task.Delay(500);

        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "公開する" }).ClickAsync();
        await Expect(Page.GetByText("公開オファーを作成しました。")).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });

        // Verify it's on the board
        await Expect(Page.GetByText(tagName)).ToBeVisibleAsync();

        // --- User B (Triggerer) ---
        await RegisterAndLoginAsync(userBEmail, password);

        // User B creates an Item
        _ = await Page.GotoAsync($"{_serverAddress}/Item/ItemList");
        await Task.Delay(2000);
        await Page.GetByPlaceholder("新しいアイテムのコンテンツを入力...").FillAsync(itemContent);
        await Task.Delay(1000);
        await Page.GetByPlaceholder("新しいアイテムのコンテンツを入力...").FillAsync(itemContent);
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "保存" }).ClickAsync();
        await Expect(Page.GetByText("アイテムが正常に保存されました。")).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });

        // User B triggers the offer
        _ = await Page.GotoAsync($"{_serverAddress}/PublicOffer/PublicOfferBoard");
        await Expect(Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "オファーに応じる" })).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "オファーに応じる" }).First.ClickAsync();

        await Task.Delay(1000);
        await Page.GetByLabel("タグを付与する対象のアイテム").ClickAsync();
        await Page.GetByLabel("タグを付与する対象のアイテム").FillAsync(itemContent);
        await Page.GetByRole(AriaRole.Option).Filter(new LocatorFilterOptions { HasText = itemContent }).ClickAsync();
        
        await Task.Delay(500);

        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "実行する" }).ClickAsync();
        await Expect(Page.GetByText("公開オファーを利用してタグを獲得しました！")).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });
    }

    [GeneratedRegex("Click here to confirm your account", RegexOptions.IgnoreCase)]
    private static partial Regex ConfirmAccountRegex();
}
