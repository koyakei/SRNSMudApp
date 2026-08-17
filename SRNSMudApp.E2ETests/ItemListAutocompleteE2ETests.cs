using System.Text.RegularExpressions;
using Microsoft.Playwright;
using NUnit.Framework;
using Microsoft.Extensions.DependencyInjection;
using SRNSMudApp.Data;

namespace SRNSMudApp.E2ETests;

public class ItemListAutocompleteE2ETests
{
    private CustomWebApplicationFactory _factory = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _factory = new CustomWebApplicationFactory();
        _factory.EnsureServer();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _factory?.Dispose();
    }

    [Test]
    public async Task TwoStepAutocomplete_ShouldAppendAtSymbolAndFilterByUser()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var context = await browser.NewContextAsync();
        var page = await context.NewPageAsync();

        using (var scope = _factory.AppServices.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>>();
            var user1 = await userManager.FindByEmailAsync("user1@example.com");
            if (user1 == null) {
                user1 = new ApplicationUser { UserName = "user1", Email = "user1@example.com" };
                await userManager.CreateAsync(user1);
            }
            if (!db.Tags.Any(t => t.Name == "Item1_Tag")) {
                db.Tags.Add(new Tag { Name = "Item1_Tag", OwnerId = user1.Id });
                await db.SaveChangesAsync();
            }
        }

        // ログイン (User1でログイン)
        await page.GotoAsync($"{_factory.ServerAddress}/auth/callback?provider=Google&code=mock-user1");
        await page.WaitForURLAsync(new Regex(@"^" + Regex.Escape(_factory.ServerAddress) + @"/?$"), new PageWaitForURLOptions { Timeout = 10000 });
        
        // ItemListへ遷移
        await page.GotoAsync($"{_factory.ServerAddress}/Item/ItemList");

        // オートコンプリート入力欄を探す
        var searchInput = page.Locator("input[placeholder='タグ名 または タグ名 @ユーザー名 で検索...']");
        await searchInput.WaitForAsync();

        // "Item1" と入力
        await searchInput.FillAsync("Item1");
        
        // サジェストされた "Item1_Tag" を選択
        var goodOption = page.GetByRole(AriaRole.Option).Filter(new LocatorFilterOptions { HasTextString = "Item1_Tag" }).First;
        await goodOption.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await goodOption.ClickAsync();

        // 検索ボックスの値が "Item1_Tag @" になっていることを確認
        await Assertions.Expect(searchInput).ToHaveValueAsync("Item1_Tag @");

        // 続けて "user2" と入力
        await searchInput.FillAsync("Item1_Tag @user2");
        await searchInput.PressAsync("Enter");

        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }
}
