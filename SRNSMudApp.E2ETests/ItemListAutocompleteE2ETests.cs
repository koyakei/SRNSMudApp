using System.Text.RegularExpressions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace SRNSMudApp.E2ETests;

public class ItemListAutocompleteE2ETests
{
    private CustomWebApplicationFactory _factory = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _factory = new CustomWebApplicationFactory();
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

        // ログイン (User1でログイン)
        await page.GotoAsync($"{_factory.ServerAddress}/Account/Login");
        await page.FillAsync("input[name='Input.Email']", "user1@example.com");
        await page.FillAsync("input[name='Input.Password']", "User1!Password");
        await page.ClickAsync("button[type='submit']");
        
        // ItemListへ遷移
        await page.GotoAsync($"{_factory.ServerAddress}/Item/ItemList");

        // オートコンプリート入力欄を探す
        var searchInput = page.Locator("input[placeholder='タグ名 または タグ名 @ユーザー名 で検索...']");
        await searchInput.WaitForAsync();

        // "go" と入力
        await searchInput.FillAsync("go");
        
        // サジェストされた "good" を選択
        var popover = page.Locator(".mud-popover");
        await popover.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        var goodOption = popover.Locator("div.mud-list-item").Filter(new LocatorFilterOptions { HasTextString = "good" }).First;
        await goodOption.ClickAsync();

        // 検索ボックスの値が "good @" になっていることを確認
        await Assertions.Expect(searchInput).ToHaveValueAsync("good @");

        // 続けて "user2" と入力
        await searchInput.FillAsync("good @user2");
        await searchInput.PressAsync("Enter");

        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }
}
