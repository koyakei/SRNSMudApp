using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

using SRNSMudApp.Data;

namespace SRNSMudApp.E2ETests;

/// <summary>
///     ItemDetail を開いた時に選択された Item が画面の表示領域内に
///     自動スクロールされることを検証する E2E テスト。
/// </summary>
[TestFixture]
public class ItemDetailScrollE2ETests : PageTest
{
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _factory = SharedTestServerFixture.Factory;
        _serverAddress = SharedTestServerFixture.ServerAddress;
    }

    private CustomWebApplicationFactory _factory = null!;
    private string _serverAddress = "";

    [Test]
    public async Task GivenItemThread_WhenOpeningItemDetail_ThenFocusedItemIsScrolledIntoView()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var username = $"scroll_user_{suffix}";
        var email = $"{username}@example.com";

        await WebAuthnTestHelpers.LoginWithMockGoogleAsync(Page, _serverAddress, username);

        int targetItemId;

        using (IServiceScope scope = _factory.AppServices.CreateScope())
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            ApplicationUser user = db.Users.FirstOrDefault(u => u.UserName == username || u.Email == email)
                ?? db.Users.OrderByDescending(u => u.Id).First();

            // 親アイテムを作成
            var parentItem = new Item
            {
                Content = "Parent Thread Item\nLong text\nLine 2\nLine 3\nLine 4\nLine 5",
                OwnerId = user.Id,
                CreatedDate = DateTime.UtcNow.AddMinutes(-30)
            };
            _ = db.Items.Add(parentItem);
            _ = await db.SaveChangesAsync();

            // 親に対するリプライ（兄弟アイテム）を3件作成して、ターゲットアイテムが画面下に来るようにする
            for (var i = 1; i <= 3; i++)
            {
                _ = db.Items.Add(new Item
                {
                    ParentItemId = parentItem.Id,
                    Content = $"Earlier Sibling {i}\nSome height line 1\nLine 2\nLine 3\nLine 4\nLine 5",
                    OwnerId = user.Id,
                    CreatedDate = DateTime.UtcNow.AddMinutes(-20 + i)
                });
            }

            var targetItem = new Item
            {
                ParentItemId = parentItem.Id,
                Content = "Target Focused Item\nThis item should be in viewport after scroll\nLine 2\nLine 3\nLine 4",
                OwnerId = user.Id,
                CreatedDate = DateTime.UtcNow.AddMinutes(-10)
            };
            _ = db.Items.Add(targetItem);

            _ = await db.SaveChangesAsync();
            targetItemId = targetItem.Id;
        }

        // ItemDetail へ遷移
        _ = await Page.GotoAsync($"{_serverAddress}/ItemDetail/{targetItemId}");

        // ページロードおよびスクロール処理が走るのを待機
        _ = await Page.WaitForSelectorAsync($"#item-card-{targetItemId}");
        await Task.Delay(1500);

        // ターゲットアイテムがビューポート内に表示されているか評価
        var isInViewport = await Page.EvaluateAsync<bool>($@"() => {{
            const el = document.getElementById('item-card-{targetItemId}');
            if (!el) return false;
            const rect = el.getBoundingClientRect();
            const windowHeight = window.innerHeight || document.documentElement.clientHeight;
            // 画面の上端（AppBar 64px 考慮）から下端の間に要素が入っていること
            return rect.bottom > 64 && rect.top < windowHeight;
        }}");

        Assert.That(isInViewport, Is.True, "選択中のアイテムが画面の表示領域内にスクロールされていること");
    }
}