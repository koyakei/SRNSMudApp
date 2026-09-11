using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

using SRNSMudApp.Data;

namespace SRNSMudApp.E2ETests;

/// <summary>
///     他ユーザーのItemに対する「選択テキストを別アイテムに分割」リクエストの送信、
///     アイテムカードおよび通知画面での承認フローを検証するE2Eテスト。
/// </summary>
[TestFixture]
public class ItemSplitRequestE2ETests : PageTest
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
    public async Task GivenOtherUsersItem_WhenRequestingSplit_AndOwnerApproves_ThenNewItemIsCreatedAndLinked()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var alice = $"alice_{suffix}";
        var bob = $"bob_{suffix}";

        var splitSnippet = $"分割対象_{suffix}";
        var originalContent = $"こんにちは！{splitSnippet}ここはテストテキストです。";

        int targetItemId;

        // 1. Alice でログインしてアイテムを作成
        await WebAuthnTestHelpers.LoginWithMockGoogleAsync(Page, _serverAddress, alice);

        using (IServiceScope scope = _factory.AppServices.CreateScope())
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            ApplicationUser aliceUser = db.Users.FirstOrDefault(u => u.UserName == alice || u.Email == $"{alice}@example.com")
                ?? db.Users.OrderByDescending(u => u.Id).First();

            var item = new Item
            {
                Content = originalContent,
                OwnerId = aliceUser.Id,
                CreatedDate = DateTime.UtcNow,
                UpdatedDate = DateTime.UtcNow
            };
            db.Items.Add(item);
            await db.SaveChangesAsync();
            targetItemId = item.Id;
        }

        // 2. Bob でログインし、Alice のアイテムを表示
        await WebAuthnTestHelpers.LoginWithMockGoogleAsync(Page, _serverAddress, bob);
        await Page.GotoAsync($"{_serverAddress}/Item/ItemList?focus={targetItemId}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        ILocator itemCard = Page.Locator($"#item-card-{targetItemId}");
        await Expect(itemCard).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });

        // 他人のアイテムなので「分割リクエスト」ボタンが表示されていることを確認
        ILocator requestSplitBtn = Page.Locator($"[data-testid='request-split-button-{targetItemId}']");
        await Expect(requestSplitBtn).ToBeVisibleAsync();

        // 3. JavaScript で分割対象テキストを選択
        await Page.EvaluateAsync(@"(args) => {
            const el = document.querySelector('#item-card-' + args.itemId);
            const walker = document.createTreeWalker(el, NodeFilter.SHOW_TEXT);
            let node;
            while (node = walker.nextNode()) {
                const idx = node.nodeValue.indexOf(args.snippet);
                if (idx >= 0) {
                    const range = document.createRange();
                    range.setStart(node, idx);
                    range.setEnd(node, idx + args.snippet.length);
                    const sel = window.getSelection();
                    sel.removeAllRanges();
                    sel.addRange(range);
                    break;
                }
            }
        }", new { itemId = targetItemId, snippet = splitSnippet });

        // 4. 分割リクエストボタンをクリック
        await requestSplitBtn.ClickAsync();

        // 確認ダイアログが表示されるのを確認
        ILocator dialog = Page.Locator(".mud-dialog");
        await Expect(dialog).ToBeVisibleAsync();
        await Expect(dialog).ToContainTextAsync(splitSnippet);

        // ダイアログで「リクエスト送信」をクリック
        ILocator confirmBtn = Page.Locator("[data-testid='confirm-split-request-button']");
        await confirmBtn.ClickAsync();
        await Expect(dialog).Not.ToBeVisibleAsync();

        // Bob の画面で「取り下げ」ボタン付きのアラートが表示されることを確認
        ILocator cancelBtn = itemCard.Locator("[data-testid^='cancel-split-request-']");
        await Expect(cancelBtn).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });

        // 5. Alice で再ログイン
        await WebAuthnTestHelpers.LoginWithMockGoogleAsync(Page, _serverAddress, alice);

        // アイテム一覧で Alice がカードを見ると「承認」「却下」ボタンが表示されていることを確認
        await Page.GotoAsync($"{_serverAddress}/Item/ItemList?focus={targetItemId}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        ILocator aliceItemCard = Page.Locator($"#item-card-{targetItemId}");
        await Expect(aliceItemCard).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });

        ILocator approveBtn = aliceItemCard.Locator("[data-testid^='approve-split-request-']");
        ILocator rejectBtn = aliceItemCard.Locator("[data-testid^='reject-split-request-']");
        await Expect(approveBtn).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
        await Expect(rejectBtn).ToBeVisibleAsync();

        // 6. Alice が承認をクリック
        await approveBtn.ClickAsync();

        // 承認後、保留中アラート（承認ボタン）が消えることを確認
        await Expect(approveBtn).Not.ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });

        // 内部リンクプレビューのピルが表示されていることを確認
        ILocator previewPill = aliceItemCard.Locator(".cursor-pointer.px-1.mx-1.rounded");
        await Expect(previewPill).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
        await Expect(previewPill).ToContainTextAsync(splitSnippet);

        // DB 側で新規アイテムが作成され、元アイテム本文が /ItemDetail/ リンクに置換されていることを確認
        using (IServiceScope scope = _factory.AppServices.CreateScope())
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Item? original = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(db.Items, i => i.Id == targetItemId);
            Assert.That(original, Is.Not.Null);
            Assert.That(original!.Content, Does.Contain("/ItemDetail/"));

            Item? newItem = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(db.Items.OrderByDescending(i => i.Id));
            Assert.That(newItem, Is.Not.Null);
            Assert.That(newItem!.Content, Is.EqualTo(splitSnippet));
            Assert.That(newItem.OwnerId, Is.EqualTo(original.OwnerId));
        }

        // 7. 通知画面 (/notifications) でも確認
        await Page.GotoAsync($"{_serverAddress}/notifications");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Expect(Page.Locator("text=承認済み")).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
    }
}