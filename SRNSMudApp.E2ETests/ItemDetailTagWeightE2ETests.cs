using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

using SRNSMudApp.Data;

namespace SRNSMudApp.E2ETests;

public class ItemDetailTagWeightE2ETests
{
    private CustomWebApplicationFactory _factory = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _factory = new CustomWebApplicationFactory();
        _factory.EnsureServer();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown() => _factory?.Dispose();

    [Test]
    public async Task ItemDetail_ShouldDisplayItemSpecificTagWeight_WhenVoteButtonClicked()
    {
        using IPlaywright playwright = await Playwright.CreateAsync();
        await using IBrowser browser =
            await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        IBrowserContext context = await browser.NewContextAsync();
        IPage page = await context.NewPageAsync();

        int testItemId;
        using (IServiceScope scope = _factory.AppServices.CreateScope())
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            UserManager<ApplicationUser> userManager =
                scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            ApplicationUser? user1 = await userManager.FindByEmailAsync("user1@example.com");
            if (user1 == null)
            {
                user1 = new ApplicationUser { UserName = "user1", Email = "user1@example.com" };
                await userManager.CreateAsync(user1);
            }

            Tag? testTag = db.Tags.FirstOrDefault(t => t.Name == "Item1_Tag");
            if (testTag == null)
            {
                testTag = new Tag { Name = "Item1_Tag", OwnerId = user1.Id };
                db.Tags.Add(testTag);
                await db.SaveChangesAsync();
            }

            var testItem = new Item { Content = "Test Item for Weight", OwnerId = user1.Id };
            db.Items.Add(testItem);
            await db.SaveChangesAsync();
            testItemId = testItem.Id;

            db.TagRelations.Add(new TagRelation
            {
                ItemId = testItem.Id, TagId = testTag.Id, OwnerId = user1.Id, Weight = 0
            });
            await db.SaveChangesAsync();
        }

        // ログイン
        await page.GotoAsync($"{_factory.ServerAddress}/auth/callback?provider=Google&code=mock-user1");
        await page.WaitForURLAsync(new Regex(@"^" + Regex.Escape(_factory.ServerAddress) + @"/?$"),
            new PageWaitForURLOptions { Timeout = 10000 });

        // アイテムの詳細画面へ遷移
        await page.GotoAsync($"{_factory.ServerAddress}/ItemDetail/{testItemId}");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // アイテムカードにあるDownvoteボタン(↓)をクリックする
        ILocator itemCard = page.Locator(".mud-card").First;
        // Item1_TagのWeightを減らすボタンをクリック
        ILocator downvoteButton = itemCard.Locator("span.position-relative:has-text('Item1_Tag')")
            .Locator("button[title='Weightを減らす']");
        await downvoteButton.ClickAsync();

        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // TagTableの中に表示されているItem1_Tagのウェイトが -1 になっていることを確認する
        ILocator tagTable = page.Locator("table.mud-table-root");
        ILocator goodTagRow = tagTable.Locator("tr").Filter(new LocatorFilterOptions { HasTextString = "Item1_Tag" })
            .First;

        // 行の中の DataLabel="Weight" のセルの内容が -1 であることを確認
        ILocator weightCell = goodTagRow.Locator("td[data-label='Weight']");
        await Assertions.Expect(weightCell).ToHaveTextAsync(new Regex(@"-1"));
    }
}