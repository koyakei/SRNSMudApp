#region

using System.Text.RegularExpressions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

using SRNSMudApp.Data;

#endregion

namespace SRNSMudApp.E2ETests;

[TestFixture]
public class ItemListFocusE2ETests : PageTest
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
    private string _serverAddress = "";
    private string _userId = "";

    [Test]
    public async Task ItemFocus_UpdatesUrlAndScrolls()
    {
        // データベースにモックユーザーとアイテムを複数登録
        using (IServiceScope scope = _factory.AppServices.CreateScope())
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            ApplicationUser? testUser = db.Users.FirstOrDefault(u => u.UserName == "testuser");
            if (testUser == null)
            {
                testUser = new ApplicationUser
                {
                    Id = Guid.NewGuid().ToString(),
                    UserName = "testuser",
                    NormalizedUserName = "TESTUSER",
                    Email = "test@example.com",
                    NormalizedEmail = "TEST@EXAMPLE.COM"
                };
                _ = db.Users.Add(testUser);
                _ = await db.SaveChangesAsync();
            }

            _userId = testUser.Id;

            // 既存のアイテムをクリーンアップ
            db.Items.RemoveRange(db.Items);
            _ = await db.SaveChangesAsync();

            // アイテムを10件追加 (スクロールが必要なように少し長めのコンテンツ)
            for (var i = 1; i <= 10; i++)
            {
                _ = db.Items.Add(new Item
                {
                    Content =
                        $"Test Item {i}\nThis is a long text to ensure the card has some height.\nLine 3\nLine 4\nLine 5",
                    OwnerId = _userId
                });
                _ = await db.SaveChangesAsync();
                await Task.Delay(10);
            }
        }

        using (IServiceScope scope = _factory.AppServices.CreateScope())
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Item firstItem = db.Items.OrderByDescending(i => i.UpdatedDate).First();
            _ = db.Items.OrderByDescending(i => i.UpdatedDate).Last();

            // 1. ページへ遷移
            _ = await Page.GotoAsync($"{_serverAddress}/Item/ItemList");

            // サーバー側でSignalRが接続され、データがロードされるのを待機
            await Task.Delay(2000);

            // リストのアイテムが表示されていることを確認
            ILocator firstItemCard = Page.Locator($"#item-card-{firstItem.Id}");
            await Expect(firstItemCard).ToBeVisibleAsync();

            // --- テストケース1: クリックによるフォーカスと URL 更新 ---
            await firstItemCard.ClickAsync();

            // URLが更新されるのを待つ
            await Expect(Page).ToHaveURLAsync(new Regex($"focusItem={firstItem.Id}"));

            // スタイルが更新されているか（border-width が 2px になっているか）を確認
            var style = await firstItemCard.GetAttributeAsync("style");
            Assert.That(style, Does.Contain("border-width: 2px"));
            Assert.That(style, Does.Contain("var(--mud-palette-primary)"));

            // --- テストケース2: スクロールによる自動フォーカスと URL 更新 ---
            // 画面の中央に到達できる中間のアイテムをスクロール先にする
            Item scrollTargetItem = db.Items.OrderByDescending(i => i.UpdatedDate).Skip(5).First();
            _ = await Page.EvaluateAsync(
                $"document.getElementById('item-card-{scrollTargetItem.Id}').scrollIntoView({{ block: 'center' }});");

            // IntersectionObserverが反応して状態が更新されるのを待つ
            await Task.Delay(1500);

            // URLが自動的に更新されていることを確認
            await Expect(Page).ToHaveURLAsync(new Regex($"focusItem={scrollTargetItem.Id}"));

            // --- テストケース3: パラメータ付きURLに直接遷移した際のフォーカス復元 ---
            Item middleItem = db.Items.OrderByDescending(i => i.UpdatedDate).Skip(4).First();

            // 新しいURLに直接遷移
            _ = await Page.GotoAsync($"{_serverAddress}/Item/ItemList?focusItem={middleItem.Id}");

            await Task.Delay(2000);

            // 画面が自動スクロールされ、フォーカスされていることを確認
            ILocator middleItemCard = Page.Locator($"#item-card-{middleItem.Id}");
            await Expect(middleItemCard).ToBeVisibleAsync();

            var middleStyle = await middleItemCard.GetAttributeAsync("style");
            Assert.That(middleStyle, Does.Contain("border-width: 2px"));
            Assert.That(middleStyle, Does.Contain("var(--mud-palette-primary)"));
        }
    }

    [Test]
    public async Task ItemFocus_WithTagFilterAndScroll()
    {
        int targetTagId;
        using (IServiceScope scope = _factory.AppServices.CreateScope())
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            ApplicationUser? testUser = db.Users.FirstOrDefault(u => u.UserName == "testuser");
            if (testUser == null)
            {
                testUser = new ApplicationUser
                {
                    Id = Guid.NewGuid().ToString(),
                    UserName = "testuser",
                    NormalizedUserName = "TESTUSER",
                    Email = "test@example.com",
                    NormalizedEmail = "TEST@EXAMPLE.COM"
                };
                _ = db.Users.Add(testUser);
                _ = await db.SaveChangesAsync();
            }

            _userId = testUser.Id;

            // クリーンアップ
            db.Items.RemoveRange(db.Items);
            db.Tags.RemoveRange(db.Tags);
            _ = await db.SaveChangesAsync();

            var tag = new Tag { Name = "E2E_Test_Tag", OwnerId = _userId };
            _ = db.Tags.Add(tag);
            _ = await db.SaveChangesAsync();
            targetTagId = tag.Id;

            var items = new List<Item>();
            for (var i = 1; i <= 10; i++)
            {
                var item = new Item
                {
                    Content =
                        $"Filterable Item {i}\nThis is a long text to ensure the card has some height.\nLine 3\nLine 4\nLine 5",
                    OwnerId = _userId
                };
                _ = db.Items.Add(item);
                _ = await db.SaveChangesAsync();
                await Task.Delay(10);
                items.Add(item); // 後のタグ関連付け処理のため保持
            }

            foreach (Item item in items)
            {
                _ = db.TagRelations.Add(new TagRelation
                {
                    ItemId = item.Id, TagId = tag.Id, OwnerId = _userId, Weight = 1
                });
            }

            _ = await db.SaveChangesAsync();
        }

        using (IServiceScope scope = _factory.AppServices.CreateScope())
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var itemsDesc = db.Items.OrderByDescending(i => i.UpdatedDate).ToList();
            Item firstItem = itemsDesc.First();
            Item scrollTargetItem = itemsDesc.Skip(5).First();

            _ = await Page.GotoAsync($"{_serverAddress}/Item/ItemList");
            await Task.Delay(2000);

            // 1. アイテムをフォーカス
            ILocator firstItemCard = Page.Locator($"#item-card-{firstItem.Id}");
            await Expect(firstItemCard).ToBeVisibleAsync();
            await firstItemCard.ClickAsync();
            await Expect(Page).ToHaveURLAsync(new Regex($"focusItem={firstItem.Id}"));

            // 2. 検索してタグを追加
            ILocator searchInput = Page.Locator("input[placeholder='タグ名 または タグ名 @ユーザー名 で検索...']").First;
            await searchInput.FillAsync("E2E_Test_Tag");

            ILocator suggestion = Page.Locator("div.mud-list-item",
                new PageLocatorOptions { HasTextString = "E2E_Test_Tag" }).First;
            await Expect(suggestion).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });
            await suggestion.EvaluateAsync("el => el.click()");
            await Task.Delay(1000);
            await Page.Locator(".mud-input-adornment button").First.ClickAsync();

            await Task.Delay(1500); // フィルタ更新待ち

            // タグが選択されたのでURLにtagsが含まれるはず
            var currentUrl = Page.Url;
            Assert.That(currentUrl, Does.Contain($"tags={targetTagId}"));

            // 3. 別のアイテムへスクロール
            _ = await Page.EvaluateAsync(
                $"document.getElementById('item-card-{scrollTargetItem.Id}').scrollIntoView({{ block: 'center' }});");
            await Task.Delay(1500); // Observerの反映待ち

            // URLが両方のクエリを持っているか確認
            var finalUrl = Page.Url;
            Assert.That(finalUrl, Does.Contain($"tags={targetTagId}"));
            Assert.That(finalUrl, Does.Contain($"focusItem={scrollTargetItem.Id}"));

            // 新しいフォーカス対象にスタイルが適用されているか
            ILocator scrolledCard = Page.Locator($"#item-card-{scrollTargetItem.Id}");
            var style = await scrolledCard.GetAttributeAsync("style");
            Assert.That(style, Does.Contain("border-width: 2px"));
            Assert.That(style, Does.Contain("var(--mud-palette-primary)"));
        }
    }

    [Test]
    public async Task AuthorLinkClick_NavigatesToUserDetail_WithoutFocusQuery()
    {
        using (IServiceScope scope = _factory.AppServices.CreateScope())
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            ApplicationUser? testUser = db.Users.FirstOrDefault(u => u.UserName == "testuser");
            if (testUser == null)
            {
                testUser = new ApplicationUser
                {
                    Id = Guid.NewGuid().ToString(),
                    UserName = "testuser",
                    NormalizedUserName = "TESTUSER",
                    Email = "test@example.com",
                    NormalizedEmail = "TEST@EXAMPLE.COM"
                };
                _ = db.Users.Add(testUser);
                _ = await db.SaveChangesAsync();
            }

            _userId = testUser.Id;

            // クリーンアップ
            db.Items.RemoveRange(db.Items);
            _ = await db.SaveChangesAsync();

            // アイテムを追加
            _ = db.Items.Add(new Item
            {
                Content = "Author Link Test Item\nThis is a long text to ensure the card has some height.",
                OwnerId = _userId,
                UpdatedDate = DateTime.UtcNow
            });
            _ = await db.SaveChangesAsync();
        }

        using (IServiceScope scope = _factory.AppServices.CreateScope())
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Item item = db.Items.First();

            _ = await Page.GotoAsync($"{_serverAddress}/Item/ItemList");
            await Task.Delay(2000);

            ILocator itemCard = Page.Locator($"#item-card-{item.Id}");
            await Expect(itemCard).ToBeVisibleAsync();

            // アイテムをクリックしてフォーカスを当てる（クエリパラメータが付与される）
            await itemCard.ClickAsync();
            await Expect(Page).ToHaveURLAsync(new Regex($"focusItem={item.Id}"));

            // 投稿者リンクをクリック
            ILocator authorLink = itemCard.Locator("a", new LocatorLocatorOptions { HasTextString = "testuser" }).First;
            await authorLink.ClickAsync();

            // UserDetailページへの遷移を待つ
            await Expect(Page).ToHaveURLAsync(new Regex($"/User/UserDetail/{_userId}"));

            // focusItem クエリパラメータが含まれていないことを確認する
            var url = Page.Url;
            Assert.That(url, Does.Not.Contain("focusItem="), "UserDetailに遷移した際、focusItemのクエリパラメータは引き継がれるべきではありません");
        }
    }
}