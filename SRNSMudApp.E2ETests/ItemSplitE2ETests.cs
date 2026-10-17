#region

using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

#endregion

namespace SRNSMudApp.E2ETests;

/// <summary>
///     アイテム分割（直接分割および分割リクエスト）機能について、
///     実ブラウザ (Playwright) 上での所有者／非所有者のボタン表示切り替え、
///     未選択時の警告スナックバー表示、SignalR 接続下での UI 挙動を検証する E2E テスト。
/// </summary>
[TestFixture]
public class ItemSplitE2ETests : PageTest
{
    private string _serverAddress = "";

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _serverAddress = SharedTestServerFixture.ServerAddress;
    }

    [Test]
    public async Task ItemSplit_OwnerRendersDirectSplit_NonOwnerRendersSplitRequest_AndShowsWarningWhenNoTextSelected()
    {
        var uniqueIdA = Guid.NewGuid().ToString("N")[..8];
        var emailA = $"e2e_split_a_{uniqueIdA}@example.com";

        var uniqueIdB = Guid.NewGuid().ToString("N")[..8];
        var emailB = $"e2e_split_b_{uniqueIdB}@example.com";

        // 1. ユーザーAでログイン
        await WebAuthnTestHelpers.LoginWithMockGoogleAsync(Page, _serverAddress, emailA);

        var itemContent = $"E2E_Content_{uniqueIdA}_分割対象テキスト_末尾";

        // DB にテスト用アイテムを作成（AddItem UI経由の不確実性を排除）
        var userNameA = emailA.Split('@')[0];
        _ = await SharedTestServerFixture.Factory.CreateItemAsync(userNameA, itemContent);

        // 2. /Item/ItemList に移動
        _ = await Page.GotoAsync($"{_serverAddress}/Item/ItemList");

        // アイテムが一覧に表示されることを確認
        var itemLocator = Page.GetByText(itemContent).First;
        await Expect(itemLocator).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });

        // 3. 所有者（ユーザーA）の画面では「直接分割」ボタンが表示され、「分割リクエスト」ボタンは非表示
        var directSplitButton = Page.Locator("button[title='選択テキストを別アイテムに分割']").First;
        await Expect(directSplitButton).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });

        var splitRequestButton = Page.Locator("button[title='選択テキストを別アイテムに分割するリクエストを送信']");
        var countForOwner = await splitRequestButton.CountAsync();
        Assert.That(countForOwner, Is.EqualTo(0));

        // 4. ログアウト
        var logoutBtn = Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Logout" });
        if (await logoutBtn.IsVisibleAsync())
        {
            await logoutBtn.ClickAsync();
        }
        else
        {
            _ = await Page.GotoAsync($"{_serverAddress}/Account/Logout");
        }

        // 5. ユーザーBでログイン
        await WebAuthnTestHelpers.LoginWithMockGoogleAsync(Page, _serverAddress, emailB);

        // 6. /Item/ItemList を再訪
        _ = await Page.GotoAsync($"{_serverAddress}/Item/ItemList");
        await Expect(Page.GetByText(itemContent).First)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });

        // 7. 非所有者（ユーザーB）の画面では「分割リクエスト」ボタンが表示され、「直接分割」ボタンは非表示
        var splitReqBtnForNonOwner = Page.Locator("button[title='選択テキストを別アイテムに分割するリクエストを送信']").First;
        await Expect(splitReqBtnForNonOwner).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });

        var directSplitCountForNonOwner = await Page.Locator("button[title='選択テキストを別アイテムに分割']").CountAsync();
        Assert.That(directSplitCountForNonOwner, Is.EqualTo(0));

        // 8. テキスト未選択のまま分割リクエストボタンをクリックすると、警告スナックバーが表示される
        await splitReqBtnForNonOwner.ClickAsync();

        await Expect(Page.GetByText("分割するテキストが選択されていません。"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });
    }
}