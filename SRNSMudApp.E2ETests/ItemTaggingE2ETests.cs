#region

using System.Text.RegularExpressions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

using SRNSMudApp.Data;

#endregion

namespace SRNSMudApp.E2ETests;

[TestFixture]
public partial class ItemTaggingE2ETests : PageTest
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
    private string? _serverAddress;

    [Test]
    public async Task AddTagToItem_FailsIfItemIsAddedAsNewEntity()
    {
        // ユーザー情報
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var email = $"testuser_tagging_{uniqueId}@example.com";
        const string password = "Password123!";

        // ユーザー登録
        {
            await Page.Context.ClearCookiesAsync();
            var userName = email.Contains('@') ? email.Split('@')[0] : email;
            await Page.GotoAsync($"{_serverAddress}/auth/callback?provider=Google&code=mock-{userName}");
            await Page.WaitForURLAsync(new Regex(@"^" + Regex.Escape(_serverAddress) + @"/?$"),
                new PageWaitForURLOptions { Timeout = 10000 });
            await Expect(Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Logout" }))
                .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });
        }

        // アイテムを追加
        _ = await Page.GotoAsync($"{_serverAddress}/Item/ItemList");
        await Task.Delay(2000); // 接続待ち

        var testContent = $"Test Item Content {uniqueId}";
        await Page.GetByPlaceholder("新しいアイテムのコンテンツを入力...").FillAsync(testContent);
        await Task.Delay(1500);
        await Page.GetByPlaceholder("新しいアイテムのコンテンツを入力...").FillAsync(testContent);
        await Page.GetByPlaceholder("新しいアイテムのコンテンツを入力...").BlurAsync();
        await Task.Delay(1000);
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "保存" }).ClickAsync();
        await Expect(Page.GetByText("アイテムが正常に保存されました。"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });

        // タグを作成 (直接DBへ挿入)
        var tagName = $"Test Tag {uniqueId}";
        using (IServiceScope scope = _factory!.AppServices.CreateScope())
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            ApplicationUser user = await db.Users.FirstAsync(u => u.Email == email);
            db.Tags.Add(new Tag { Name = tagName, Content = "Test content", OwnerId = user.Id, CachedWeight = 0 });
            await db.SaveChangesAsync();
        }

        // Item/ItemListページに移動
        _ = await Page.GotoAsync($"{_serverAddress}/Item/ItemList");
        await Task.Delay(2000);

        // 対象のアイテムのタグ追加ボタンをクリック
        ILocator itemCard = Page.Locator(".mud-card").Filter(new LocatorFilterOptions { HasText = testContent });
        await itemCard.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "タグを追加" }).ClickAsync();

        // タグを選択
        // 検索入力
        await Page.GetByPlaceholder("タグ名 または 内容を入力...").ClickAsync();
        await Page.GetByPlaceholder("タグ名 または 内容を入力...").FillAsync(tagName);
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "検索" }).ClickAsync();
        await Task.Delay(1000); // 検索結果の表示を待つ

        // 検索結果からタグを選択 (MudRadioをクリック)
        await Page.GetByText(tagName, new PageGetByTextOptions { Exact = true }).ClickAsync();

        // 選択して追加ボタンをクリック
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "選択して追加" }).ClickAsync();

        // エラーが発生せずに成功のSnackbarが表示されることを確認
        await Expect(Page.GetByText("タグを追加しました。"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });

        // Unhandled exceptionの画面にならないことを確認する
        await Expect(Page.Locator("text=An unhandled exception occurred")).Not
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 1000 });
    }

    [GeneratedRegex("Click here to confirm your account", RegexOptions.IgnoreCase)]
    private static partial Regex ConfirmAccountRegex();

    [GeneratedRegex(".*Tag/TagList.*")]
    private static partial Regex TagListUrlRegex();
}