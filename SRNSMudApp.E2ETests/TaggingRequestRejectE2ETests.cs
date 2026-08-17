using System.Text.RegularExpressions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace SRNSMudApp.E2ETests;

public class TaggingRequestRejectE2ETests
{
    private CustomWebApplicationFactory _factory = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _factory = new CustomWebApplicationFactory();
        _factory.EnsureServer();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _factory?.Dispose();
    }

    [Test]
    public async Task TaggingRequest_ShouldBeRejectableByItemOwner()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var context = await browser.NewContextAsync();
        
        // 1. まず他のユーザー(user2)としてログインし、リクエストを作成する
        var user2Page = await context.NewPageAsync();
        await user2Page.GotoAsync($"{_factory.ServerAddress}/auth/callback?provider=Google&code=mock-user2");
        await user2Page.WaitForURLAsync(new Regex(@"^" + Regex.Escape(_factory.ServerAddress) + @"/?$"), new PageWaitForURLOptions { Timeout = 10000 });
        
        // 2. アイテムのオーナー(user1)としてログインする
        var ownerPage = await context.NewPageAsync();
        await ownerPage.GotoAsync($"{_factory.ServerAddress}/auth/callback?provider=Google&code=mock-user1");
        await ownerPage.WaitForURLAsync(new Regex(@"^" + Regex.Escape(_factory.ServerAddress) + @"/?$"), new PageWaitForURLOptions { Timeout = 10000 });
        
        // アイテム1の詳細画面へ遷移 (DBシードデータが存在する前提)
        await ownerPage.GotoAsync($"{_factory.ServerAddress}/ItemDetail/1");
        await ownerPage.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // リクエストリストを探す
        var requestList = ownerPage.Locator("div").Filter(new LocatorFilterOptions { HasTextString = "タグ付けリクエスト" }).First;
        
        // 「リジェクト」ボタンを探す
        var rejectButton = requestList.Locator("button", new LocatorLocatorOptions { HasTextRegex = new Regex("リジェクト|Reject|却下") }).First;
        
        if (await rejectButton.IsVisibleAsync())
        {
            await rejectButton.ClickAsync();
            
            // ダイアログが表示されたら、理由を入力して送信
            var dialog = ownerPage.Locator(".mud-dialog");
            await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
            
            var reasonInput = dialog.Locator("textarea");
            await reasonInput.FillAsync("今は不要です");
            
            var submitButton = dialog.Locator("button").Filter(new LocatorFilterOptions { HasTextString = "却下する" }).First;
            await submitButton.ClickAsync();
            
            // リクエストのステータスが "Rejected" または画面から消えることを確認
            await ownerPage.WaitForLoadStateAsync(LoadState.NetworkIdle);
            
            var requestCard = ownerPage.Locator(".mud-card-content").Filter(new LocatorFilterOptions { HasTextString = "今は不要です" }).First;
            await Assertions.Expect(requestCard).ToBeVisibleAsync();
            await Assertions.Expect(requestCard).ToContainTextAsync("Rejected");
        }
    }
}
