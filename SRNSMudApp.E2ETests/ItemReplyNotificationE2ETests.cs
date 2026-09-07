using System.Text.RegularExpressions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

using SRNSMudApp.Data;

namespace SRNSMudApp.E2ETests;

/// <summary>
///     自分がownerのItemにリプライされた場合、および自分がリプライしているItemに他人がリプライした場合に、
///     Twitterのように返信時の通知対象ユーザーを選択でき、選択された相手のNavMenuバッジと
///     通知ページ(/notifications)へ通知が正常に届くことを検証するE2Eテスト。
/// </summary>
[TestFixture]
public class ItemReplyNotificationE2ETests : PageTest
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
    public async Task GivenItemThread_WhenReplyingWithTargetSelection_ThenOnlySelectedUsersReceiveNotificationsAndBadge()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var alice = $"alice_{suffix}";
        var bob = $"bob_{suffix}";
        var charlie = $"charlie_{suffix}";

        var aliceEmail = $"{alice}@example.com";
        var bobEmail = $"{bob}@example.com";
        var charlieEmail = $"{charlie}@example.com";

        int targetItemId;

        // 1. ユーザー Alice でログインしてアイテムを作成
        await WebAuthnTestHelpers.LoginWithMockGoogleAsync(Page, _serverAddress, alice);

        using (IServiceScope scope = _factory.AppServices.CreateScope())
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            ApplicationUser aliceUser = db.Users.FirstOrDefault(u => u.UserName == alice || u.UserName == aliceEmail || u.Email == aliceEmail)
                ?? db.Users.OrderByDescending(u => u.Id).First();

            var item = new Item
            {
                Content = $"E2E Reply Notification Root Item {suffix}",
                OwnerId = aliceUser.Id
            };
            db.Items.Add(item);
            await db.SaveChangesAsync();
            targetItemId = item.Id;
        }

        // 2. ユーザー Bob でログインし、Aliceのアイテムにリプライ
        await WebAuthnTestHelpers.LoginWithMockGoogleAsync(Page, _serverAddress, bob);

        // アイテム一覧ページへ移動し、該当アイテムカードを表示
        await Page.GotoAsync($"{_serverAddress}/Item/ItemList?focus={targetItemId}");
        ILocator itemCard = Page.Locator($"#item-card-{targetItemId}");
        await Expect(itemCard).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });

        // 「リプライ」ボタンを押してリプライスレッドを展開
        ILocator replyToggleBtn = Page.Locator($"[data-testid='reply-toggle-button-{targetItemId}']");
        await replyToggleBtn.ClickAsync();

        ILocator candidatesContainer = Page.Locator($"[data-testid='reply-target-candidates-container-{targetItemId}']");
        await Expect(candidatesContainer).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });

        // 返信先チェックボックスに「Alice」が表示され、チェックされていることを検証
        ILocator aliceCheckbox = Page.Locator($"[data-testid='reply-target-checkbox-{targetItemId}-{aliceEmail}']");
        await Expect(aliceCheckbox).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });

        // リプライ本文を入力して送信
        var bobReplyText = $"Bob reply to Alice {suffix}";
        ILocator replyInput = Page.Locator($"[data-testid='reply-input-form-{targetItemId}'] textarea");
        await replyInput.FillAsync(bobReplyText);
        await Page.Locator($"[data-testid='reply-input-form-{targetItemId}'] button").ClickAsync();

        // リプライがスレッド内に表示されるのを確認
        await Expect(itemCard.Locator($"text={bobReplyText}")).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });

        // 3. ユーザー Alice でログインし、バッジと通知ページを検証
        await WebAuthnTestHelpers.LoginWithMockGoogleAsync(Page, _serverAddress, alice);

        // NavMenu 上の通知アイコンに「1」のバッジが表示されていることを確認
        ILocator aliceNavBadge = Page.Locator("[data-testid='nav-notifications-badge']");
        await Expect(aliceNavBadge).ToContainTextAsync("1", new LocatorAssertionsToContainTextOptions { Timeout = 10000 });

        // 通知ページへ遷移
        await Page.GotoAsync($"{_serverAddress}/notifications");
        await Page.WaitForURLAsync(new Regex(@"/notifications"), new PageWaitForURLOptions { Timeout = 10000 });

        // Bobからのリプライ通知（自分がownerのItemに対する通知）が表示されていることを確認
        ILocator aliceNotificationItem = Page.Locator($"text={bobEmail}さんがあなたのアイテムにリプライしました。");
        await Expect(aliceNotificationItem).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });

        // 通知をクリックして既読化（アイテム詳細へ遷移）
        await aliceNotificationItem.ClickAsync();
        await Page.WaitForURLAsync(new Regex(@"/ItemDetail/"), new PageWaitForURLOptions { Timeout = 10000 });

        // 4. ユーザー Charlie でログインし、スレッド参加者から Alice を除外して Bob のみへリプライ
        await WebAuthnTestHelpers.LoginWithMockGoogleAsync(Page, _serverAddress, charlie);

        await Page.GotoAsync($"{_serverAddress}/Item/ItemList?focus={targetItemId}");
        itemCard = Page.Locator($"#item-card-{targetItemId}");
        await Expect(itemCard).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });

        // リプライスレッドを展開
        replyToggleBtn = Page.Locator($"[data-testid='reply-toggle-button-{targetItemId}']");
        await replyToggleBtn.ClickAsync();

        candidatesContainer = Page.Locator($"[data-testid='reply-target-candidates-container-{targetItemId}']");
        await Expect(candidatesContainer).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });

        // 返信先候補に「Alice」と「Bob」の両方が表示されていることを確認
        aliceCheckbox = Page.Locator($"[data-testid='reply-target-checkbox-{targetItemId}-{aliceEmail}']");
        ILocator bobCheckbox = Page.Locator($"[data-testid='reply-target-checkbox-{targetItemId}-{bobEmail}']");
        await Expect(aliceCheckbox).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
        await Expect(bobCheckbox).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });

        // Twitterのように Alice のチェックを外し、Bob のみ通知対象にする
        await aliceCheckbox.ClickAsync();

        // Charlie の返信を送信
        var charlieReplyText = $"Charlie reply to Bob only {suffix}";
        replyInput = Page.Locator($"[data-testid='reply-input-form-{targetItemId}'] textarea");
        await replyInput.FillAsync(charlieReplyText);
        await Page.Locator($"[data-testid='reply-input-form-{targetItemId}'] button").ClickAsync();

        // リプライが追加されたことを確認
        await Expect(itemCard.Locator($"text={charlieReplyText}")).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });

        // 5. ユーザー Bob（自分がリプライしているItemに他人がリプライ）でログイン
        await WebAuthnTestHelpers.LoginWithMockGoogleAsync(Page, _serverAddress, bob);

        // Bob の NavMenu 通知アイコンにバッジ「1」が表示されることを確認
        ILocator bobNavBadge = Page.Locator("[data-testid='nav-notifications-badge']");
        await Expect(bobNavBadge).ToContainTextAsync("1", new LocatorAssertionsToContainTextOptions { Timeout = 10000 });

        // Bob の通知ページへ移動
        await Page.GotoAsync($"{_serverAddress}/notifications");
        await Page.WaitForURLAsync(new Regex(@"/notifications"), new PageWaitForURLOptions { Timeout = 10000 });

        // Charlieからの参加アイテムへのリプライ通知が表示されることを確認
        ILocator bobNotificationItem = Page.Locator($"text={charlieEmail}さんがあなたの参加しているアイテムにリプライしました。");
        await Expect(bobNotificationItem).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });

        // 6. ユーザー Alice で再ログインし、Charlieからの通知が届いていないことを確認
        await WebAuthnTestHelpers.LoginWithMockGoogleAsync(Page, _serverAddress, alice);

        await Page.GotoAsync($"{_serverAddress}/notifications");
        await Page.WaitForURLAsync(new Regex(@"/notifications"), new PageWaitForURLOptions { Timeout = 10000 });

        // Alice の通知ページには Charlie からの通知は存在しない
        await Expect(Page.Locator($"text={charlieEmail}さんが")).Not.ToBeVisibleAsync();
    }
}