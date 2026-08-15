#region

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

using SRNSMudApp.Data;

#endregion

namespace SRNSMudApp.E2ETests;

[TestFixture]
public class GlobalPopoverE2ETests : PageTest
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
    [TestCase("/")]
    [TestCase("/Tag/TagSearch")]
    [TestCase("/Tag/TagList")]
    [TestCase("/tag-tree")]
    [TestCase("/Item/ItemList")]
    [TestCase("/User/UserSearch")]
    public async Task Pages_ShouldNotShowBlazorError_WhenNavigated(string route)
    {
        // データベースにモックデータを登録 (ポップオーバーがレンダリングされる条件を満たすため)
        using (IServiceScope scope = _factory.AppServices.CreateScope())
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            if (!db.Users.Any(u => u.UserName == "globaltestuser"))
            {
                var user = new ApplicationUser
                {
                    Id = Guid.NewGuid().ToString(),
                    UserName = "globaltestuser",
                    NormalizedUserName = "GLOBALTESTUSER",
                    Email = "global@example.com",
                    NormalizedEmail = "GLOBAL@EXAMPLE.COM"
                };
                _ = db.Users.Add(user);

                var tag = new Tag { Name = "GlobalTestTag", Content = "GlobalTestContent", OwnerId = user.Id };
                _ = db.Tags.Add(tag);

                var item = new Item { Content = "GlobalTestItemContent", OwnerId = user.Id };
                _ = db.Items.Add(item);

                _ = await db.SaveChangesAsync();
            }
        }

        // ページへ遷移
        Page.Console += (_, msg) => Console.WriteLine($"Browser Console: {msg.Type}: {msg.Text}");
        Page.PageError += (_, msg) => Console.WriteLine($"Browser PageError: {msg}");

        _ = await Page.GotoAsync($"{_serverAddress}{route}");

        // サーバー側でSignalRが接続され、Interactiveになるまで少し待機
        await Task.Delay(2000);

        // ページタイトルや要素がロードされるのを待機 (確実にレンダリングが終わるように)
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Blazor のエラーUIが表示されていないことを確認
        ILocator errorUi = Page.Locator("#blazor-error-ui");

        // 期待値: エラーUIが隠れている（例外が発生していない）
        await Expect(errorUi).ToBeHiddenAsync();
    }
}