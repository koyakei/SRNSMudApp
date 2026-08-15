#region

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

using SRNSMudApp.Data;

#endregion

namespace SRNSMudApp.E2ETests;

[TestFixture]
public class MudPopoverE2ETests : PageTest
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
    private string? _serverAddress;

    [Test]
    public async Task UserSearch_PopoverShouldAppear_WhenTyping()
    {
        // データベースにモックユーザーを登録
        using (IServiceScope scope = _factory.AppServices.CreateScope())
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            if (!db.Users.Any(u => u.UserName == "testuser"))
            {
                _ = db.Users.Add(new ApplicationUser
                {
                    Id = Guid.NewGuid().ToString(),
                    UserName = "testuser",
                    NormalizedUserName = "TESTUSER",
                    Email = "test@example.com",
                    NormalizedEmail = "TEST@EXAMPLE.COM"
                });
                _ = await db.SaveChangesAsync();
            }
        }

        // 1. ページへ遷移
        _ = await Page.GotoAsync($"{_serverAddress}/User/UserSearch");

        try
        {
            ILocator input = Page.Locator(".mud-input-slot").First;
            await input.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });

            // サーバー側でSignalRが接続され、Interactiveになるまで少し待機
            await Task.Delay(1500);

            // 検索文字列を入力
            await input.FillAsync("testuser");

            // 3. プロバイダが存在すれば、ポップオーバーが開くはず
            ILocator popover = Page.Locator(".mud-popover.mud-popover-open");
            await Expect(popover).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });

            // 4. 検索結果として "testuser" が表示されていることを検証
            ILocator userItem = popover.Locator("text=testuser");
            await Expect(userItem).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });

            // 追加の確認として、Blazor のエラーUIが表示されていないことを確認
            ILocator errorUi = Page.Locator("#blazor-error-ui");
            await Expect(errorUi).ToBeHiddenAsync();
        }
        catch
        {
            var body = await Page.InnerTextAsync("body");
            Console.WriteLine("--- Page Text on Timeout ---");
            Console.WriteLine(body);
            throw;
        }
    }
}