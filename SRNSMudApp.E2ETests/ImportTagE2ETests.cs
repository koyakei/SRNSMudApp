#region

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

using SRNSMudApp.Data;

#endregion

namespace SRNSMudApp.E2ETests;

[TestFixture]
public class ImportTagE2ETests : PageTest
{
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _factory = new CustomWebApplicationFactory();
        _factory.EnsureServer(); // Initialize the host
        _serverAddress = _factory.ServerAddress;

        // Create a temporary CSV file
        _tempCsvFilePath = Path.GetTempFileName() + ".csv";
        File.WriteAllText(_tempCsvFilePath, "Animal,Dog\nAnimal,Cat");
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        if (File.Exists(_tempCsvFilePath))
        {
            File.Delete(_tempCsvFilePath);
        }

        _factory?.Dispose();
    }

    private CustomWebApplicationFactory? _factory;
    private string? _serverAddress;
    private string? _tempCsvFilePath;

    [Test]
    public async Task ImportTag_WithParentTag_ShouldImportTagsUnderParent()
    {
        // 1. ユーザー登録
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var email = $"importtest_{uniqueId}@example.com";
        const string password = "Password123!";

        {
            await Page.Context.ClearCookiesAsync();
            var userName = email.Contains('@') ? email.Split('@')[0] : email;
            await Page.GotoAsync($"{_serverAddress}/auth/callback?provider=Google&code=mock-{userName}");
            await Page.WaitForURLAsync(new System.Text.RegularExpressions.Regex(@"^" + System.Text.RegularExpressions.Regex.Escape(_serverAddress) + @"/?$"), new Microsoft.Playwright.PageWaitForURLOptions { Timeout = 10000 });
            await Expect(Page.GetByRole(Microsoft.Playwright.AriaRole.Button, new Microsoft.Playwright.PageGetByRoleOptions { Name = "Logout" })).ToBeVisibleAsync(new Microsoft.Playwright.LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });
        }

        // ユーザーIDを取得し、親タグを作成
        string userId;
        using (IServiceScope scope = _factory.AppServices.CreateScope())
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            ApplicationUser? user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
            Assert.That(user, Is.Not.Null);
            userId = user.Id;

            // 親タグをデータベースに直接追加
            var parentTag = new Tag { Name = $"RootTag_{uniqueId}", OwnerId = userId };
            _ = db.Tags.Add(parentTag);
            _ = await db.SaveChangesAsync();
        }

        // 3. インポート画面へ遷移
        _ = await Page.GotoAsync($"{_serverAddress}/import-tag");

        // Wait for connection
        await Task.Delay(1500);

        // 4. 親タグを検索して選択
        var rootTagName = $"RootTag_{uniqueId}";
        await Page.GetByLabel("親タグを検索").FillAsync(rootTagName);

        // MudAutocompleteのドロップダウンアイテムが表示されるのを待ってクリック
        ILocator autocompleteItem = Page.Locator($".mud-popover .mud-list-item:has-text(\"{rootTagName}\")");
        await Expect(autocompleteItem).ToBeVisibleAsync();
        await autocompleteItem.ClickAsync();

        // 5. CSVファイルを選択
        // 隠しinput要素（type="file"）を見つけてファイルを設定
        ILocator fileInput = Page.Locator("input[type='file']");
        await fileInput.SetInputFilesAsync(_tempCsvFilePath);

        // 6. インポート実行
        ILocator importButton = Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "インポート実行" });
        await Expect(importButton).ToBeEnabledAsync();
        await importButton.ClickAsync();

        // 成功メッセージを待機
        ILocator snackbar = Page.Locator(".mud-snackbar:has-text(\"インポートが完了しました\")");
        await Expect(snackbar).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });

        // 7. データベースで親子関係を検証
        using (IServiceScope scope = _factory.AppServices.CreateScope())
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            List<Tag> tags = await db.Tags.Where(t => t.OwnerId == userId).ToListAsync();

            Tag? rootTag = tags.FirstOrDefault(t => t.Name == rootTagName);
            Assert.That(rootTag, Is.Not.Null);

            Tag? animalTag = tags.FirstOrDefault(t => t.Name == "Animal");
            Assert.That(animalTag, Is.Not.Null, "Animal tag should be created");
            Assert.That(animalTag.ParentTagId, Is.EqualTo(rootTag.Id),
                "Animal tag's parent should be the selected RootTag");

            Tag? dogTag = tags.FirstOrDefault(t => t.Name == "Dog");
            Assert.That(dogTag, Is.Not.Null, "Dog tag should be created");
            Assert.That(dogTag.ParentTagId, Is.EqualTo(animalTag.Id), "Dog tag's parent should be Animal tag");

            Tag? catTag = tags.FirstOrDefault(t => t.Name == "Cat");
            Assert.That(catTag, Is.Not.Null, "Cat tag should be created");
            Assert.That(catTag.ParentTagId, Is.EqualTo(animalTag.Id), "Cat tag's parent should be Animal tag");
        }
    }
}