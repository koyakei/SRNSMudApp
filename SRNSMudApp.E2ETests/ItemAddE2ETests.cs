#region

using System.Text.RegularExpressions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

using SRNSMudApp.Data;

#endregion

namespace SRNSMudApp.E2ETests;

[TestFixture]
public partial class ItemAddE2ETests : PageTest
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
    public async Task AddItem_FailsIfOwnerIsAddedAsNewEntity()
    {
        // ユーザー情報
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var email = $"testuser_additem_{uniqueId}@example.com";
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

        // Item/ItemListページに移動
        _ = await Page.GotoAsync($"{_serverAddress}/Item/ItemList");

        // Blazor ServerのSignalR接続が完了するまで待機（プリレンダリング時のイベント消失を防ぐため）
        await Task.Delay(2000);

        // アイテムを追加
        var testContent = $"Test Item Content {uniqueId}";

        // プリレンダリングによるワイプ対策のため、2回入力する
        await Page.GetByPlaceholder("新しいアイテムのコンテンツを入力...").FillAsync(testContent);
        await Task.Delay(1500); // 接続と再レンダリングを待つ

        // もう一度入力（もし最初の入力がワイプされていればここで確実に入力される）
        await Page.GetByPlaceholder("新しいアイテムのコンテンツを入力...").FillAsync(testContent);
        await Page.GetByPlaceholder("新しいアイテムのコンテンツを入力...").BlurAsync();

        // Blazor Serverのモデル同期を待機するため、少し待つ
        await Task.Delay(1000);

        // クライアント側でtextareaの値がどうなっているか確認する
        var textareaValue = await Page.GetByPlaceholder("新しいアイテムのコンテンツを入力...").InputValueAsync();
        Assert.That(textareaValue, Is.EqualTo(testContent), "ブラウザ上のtextareaの値が正しくありません。");

        // 保存ボタンをクリック
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "保存" }).ClickAsync();

        // 成功のSnackbarが表示されるかを待つ（HandleValidSubmitが最後まで実行されたかの確認）
        await Expect(Page.GetByText("アイテムが正常に保存されました。"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });


        // エラーが出ていないこと（Unahndled exceptionの画面にならないこと）を確認する。
        await Expect(Page.Locator("text=An unhandled exception occurred")).Not
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 1000 });

        // 直接データベースを検索して、保存されているか確認する
        using (IServiceScope scope = _factory.AppServices.CreateScope())
        {
            ApplicationDbContext dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Item? itemInDb = dbContext.Items.FirstOrDefault(i => i.Content == testContent);
            Assert.That(itemInDb, Is.Not.Null, $"アイテム '{testContent}' がデータベースに存在しません。");
        }

        // 代わりにアイテムのコンテンツが画面上に表示されるのを待機する前にリロードして確実にする
        _ = await Page.ReloadAsync();
        await Expect(Page.GetByText(testContent))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });
    }

    [GeneratedRegex("Click here to confirm your account", RegexOptions.IgnoreCase)]
    private static partial Regex ConfirmAccountRegex();
}