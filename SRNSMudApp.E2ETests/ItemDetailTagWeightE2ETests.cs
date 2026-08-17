using System.Text.RegularExpressions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace SRNSMudApp.E2ETests;

public class ItemDetailTagWeightE2ETests
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
    public async Task ItemDetail_ShouldDisplayItemSpecificTagWeight_WhenVoteButtonClicked()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var context = await browser.NewContextAsync();
        var page = await context.NewPageAsync();

        // ログイン
        await page.GotoAsync($"{_factory.ServerAddress}/Account/Login");
        await page.FillAsync("input[name='Input.Email']", "user1@example.com");
        await page.FillAsync("input[name='Input.Password']", "User1!Password");
        await page.ClickAsync("button[type='submit']");
        
        // アイテム1の詳細画面へ遷移 (DBシードデータが存在する前提)
        await page.GotoAsync($"{_factory.ServerAddress}/ItemDetail/1");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // アイテムカードにあるDownvoteボタン(↓)をクリックする
        var itemCard = page.Locator(".mud-card").First;
        var downvoteButton = itemCard.Locator("button[title='よくない']");
        await downvoteButton.ClickAsync();
        
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // TagTableの中に表示されているgoodタグのウェイトが -1 になっていることを確認する
        var tagTable = page.Locator("table.mud-table-root");
        var goodTagRow = tagTable.Locator("tr").Filter(new LocatorFilterOptions { HasTextString = "good" }).First;
        
        // 行の中の DataLabel="Weight" のセルの内容が -1 であることを確認
        var weightCell = goodTagRow.Locator("td[data-label='Weight']");
        await Assertions.Expect(weightCell).ToHaveTextAsync(new Regex(@"-1"));
    }
}
