#region

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

using SRNSMudApp.Data;

#endregion

namespace SRNSMudApp.E2ETests;

[TestFixture]
public class ItemListTagSearchE2ETests : PageTest
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
    private string _serverAddress = "";

    [Test]
    public async Task ClickingSuggestion_AddsTagChip()
    {
        // Arrange: タグとアイテムをDBに登録
        string tagName;
        using (IServiceScope scope = _factory.AppServices.CreateScope())
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            ApplicationUser? testUser = db.Users.FirstOrDefault(u => u.UserName == "tagtest_user");
            if (testUser == null)
            {
                testUser = new ApplicationUser
                {
                    Id = Guid.NewGuid().ToString(),
                    UserName = "tagtest_user",
                    NormalizedUserName = "TAGTEST_USER",
                    Email = "tagtest@example.com",
                    NormalizedEmail = "TAGTEST@EXAMPLE.COM"
                };
                db.Users.Add(testUser);
                await db.SaveChangesAsync();
            }

            // クリーンアップ
            db.TagRelations.RemoveRange(db.TagRelations);
            db.Items.RemoveRange(db.Items);
            db.Tags.RemoveRange(db.Tags);
            await db.SaveChangesAsync();

            // ユニークなタグ名を作成（他テストとの衝突回避）
            tagName = $"SearchTestTag_{Guid.NewGuid().ToString()[..8]}";

            var tag = new Tag { Name = tagName, Content = "test tag content", OwnerId = testUser.Id };
            db.Tags.Add(tag);
            await db.SaveChangesAsync();

            var item = new Item
            {
                Content = "Item linked to search test tag",
                OwnerId = testUser.Id
            };
            db.Items.Add(item);
            await db.SaveChangesAsync();

            db.TagRelations.Add(new TagRelation
            {
                ItemId = item.Id,
                TagId = tag.Id,
                OwnerId = testUser.Id,
                Weight = 1
            });
            await db.SaveChangesAsync();
        }

        // Act: ページへ遷移し、検索フィールドにタグ名を入力
        await Page.GotoAsync($"{_serverAddress}/Item/ItemList");
        await Task.Delay(2000); // Blazor Server 初期化待ち

        // 検索フィールドに入力
        ILocator searchInput = Page.Locator("input[placeholder='タグで絞り込み...']").First;
        await Expect(searchInput).ToBeVisibleAsync();
        await searchInput.FillAsync(tagName);

        // 候補ドロップダウンが表示されるのを待つ
        ILocator suggestionItem = Page.Locator(".suggestion-item", new PageLocatorOptions { HasTextString = tagName }).First;
        await Expect(suggestionItem).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });

        // 候補をクリック
        await suggestionItem.ClickAsync();

        // Blazor Server のレンダリング完了を待つ
        await Task.Delay(3000);

        // Assert: 候補ドロップダウンが閉じていること
        await Expect(suggestionItem).Not.ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 3000 });

        // Assert: 選択済みタグチップが表示されること
        ILocator tagChip = Page.Locator(".mud-chip", new PageLocatorOptions { HasTextString = tagName }).First;
        await Expect(tagChip).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });

        // Assert: URLにtagsパラメータが含まれること
        var url = Page.Url;
        Assert.That(url, Does.Contain("tags="), $"タグ選択後、URLにtags=クエリパラメータが含まれるべき。現在のURL: {url}");
    }
}
