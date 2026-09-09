using System.Text.RegularExpressions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

using SRNSMudApp.Data;

namespace SRNSMudApp.E2ETests;

/// <summary>
///     ItemCard 上の「引用」ボタンによる引用リツイート作成、
///     引用プレビューの描画、および「引用の表示」ボタンによる引用一覧ダイアログの表示を検証する E2E テスト。
/// </summary>
[TestFixture]
public class ItemQuoteE2ETests : PageTest
{
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _factory = SharedTestServerFixture.Factory;
        _serverAddress = SharedTestServerFixture.ServerAddress;
    }

    [SetUp]
    public void Setup()
    {
        Page.Console += (_, msg) => TestContext.Progress.WriteLine($"[CONSOLE] {msg.Type}: {msg.Text}");
        Page.PageError += (_, error) => TestContext.Progress.WriteLine($"[PAGE ERROR]: {error}");
    }

    private CustomWebApplicationFactory _factory = null!;
    private string _serverAddress = "";

    [Test]
    public async Task GivenAuthenticatedUser_WhenQuoteItem_ThenQuoteIsCreated_AndCanBeViewedFromShowQuotesButton()
    {
        // 1. ログイン
        var email = $"quote-user-{Guid.NewGuid():N}@example.com";
        await WebAuthnTestHelpers.LoginWithMockGoogleAsync(Page, _serverAddress, email);

        // 2. 元アイテムの作成
        int targetItemId;
        var targetContent = $"Original Target Item {Guid.NewGuid():N}";
        using (IServiceScope scope = _factory.AppServices.CreateScope())
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userName = email.Split('@')[0];
            ApplicationUser? user = db.Users.FirstOrDefault(u => u.UserName == userName || u.Email == email)
                ?? db.Users.OrderByDescending(u => u.Id).First();

            var item = new Item
            {
                Content = targetContent,
                OwnerId = user.Id,
                CreatedDate = DateTime.UtcNow,
                UpdatedDate = DateTime.UtcNow
            };
            db.Items.Add(item);
            await db.SaveChangesAsync();
            targetItemId = item.Id;
        }

        // 3. アイテム一覧ページに移動
        await Page.GotoAsync($"{_serverAddress}/Item/ItemList");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // 元アイテムのカードが表示されるのを待機
        var targetCardLocator = Page.Locator($"#item-card-{targetItemId}");
        await Expect(targetCardLocator).ToBeVisibleAsync();

        // 4. 「引用」ボタンをクリック
        var quoteButton = Page.Locator($"[data-testid='quote-button-{targetItemId}']");
        await Expect(quoteButton).ToBeVisibleAsync();
        await quoteButton.ClickAsync();

        // 5. 引用ダイアログが表示され、本文を入力して送信
        var dialogLocator = Page.Locator(".mud-dialog");
        await Expect(dialogLocator).ToBeVisibleAsync();

        var contentInput = Page.Locator(".mud-dialog textarea");
        await Expect(contentInput).ToBeVisibleAsync();

        var quoteContent = $"My Quote Comment {Guid.NewGuid():N}";
        await contentInput.FillAsync(quoteContent);

        var submitButton = Page.Locator("[data-testid='quote-item-submit-button']");
        await Expect(submitButton).ToBeEnabledAsync();
        await submitButton.ClickAsync();

        // ダイアログが閉じるのを待機
        await Expect(dialogLocator).Not.ToBeVisibleAsync();

        // 6. 投稿された引用アイテムのカードおよび引用元プレビューが表示され、URL に focus が付与されていることを確認
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Item/ItemList\?focus=\d+"));

        var quotedPreviewLocator = Page.Locator($"[data-testid='quoted-item-preview-{targetItemId}']").First;
        await Expect(quotedPreviewLocator).ToBeVisibleAsync();
        await Expect(quotedPreviewLocator).ToContainTextAsync(targetContent);

        // 7. 元アイテムの「引用の表示」ボタンをクリック
        var showQuotesButton = Page.Locator($"[data-testid='show-quotes-button-{targetItemId}']");
        await Expect(showQuotesButton).ToBeVisibleAsync();
        await Expect(showQuotesButton).ToContainTextAsync("引用の表示 (1)");
        await showQuotesButton.ClickAsync();

        // 8. 引用一覧ダイアログが開き、引用投稿の本文が表示されていることを確認
        var quotesDialog = Page.Locator(".mud-dialog");
        await Expect(quotesDialog).ToBeVisibleAsync();
        await Expect(quotesDialog).ToContainTextAsync("引用された投稿一覧");
        await Expect(quotesDialog).ToContainTextAsync(quoteContent);
    }
}