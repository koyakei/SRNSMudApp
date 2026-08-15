#region

using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

using SRNSMudApp.Data;

#endregion

namespace SRNSMudApp.E2ETests;

[TestFixture]
public partial class UserDetailTreeE2ETests : PageTest
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
    public async Task UserDetail_ShouldShowUserTags_InTree()
    {
        var testUserId = Guid.NewGuid().ToString();
        const string testTagName = "MyUniqueTestTag_12345";

        // データベースにモックユーザーとモックタグを追加
        using (IServiceScope scope = _factory.AppServices.CreateScope())
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = new ApplicationUser
            {
                Id = testUserId,
                UserName = "treetestuser",
                NormalizedUserName = "TREETESTUSER",
                Email = "treetest@example.com",
                NormalizedEmail = "TREETEST@EXAMPLE.COM"
            };
            _ = db.Users.Add(user);

            var tag = new Tag
            {
                Name = testTagName,
                Content = "This is a test tag for tree visualization.",
                OwnerId = user.Id
            };
            _ = db.Tags.Add(tag);

            _ = await db.SaveChangesAsync();
        }

        // UserDetailページへ遷移
        _ = await Page.GotoAsync($"{_serverAddress}/User/UserDetail/{testUserId}");

        // SignalRが接続され、Interactiveになるまで少し待機
        await Task.Delay(2000);

        // ページタイトルや要素がロードされるのを待機
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // BlazorのエラーUIが表示されていないことを確認
        ILocator errorUi = Page.Locator("#blazor-error-ui");
        await Expect(errorUi).ToBeHiddenAsync();

        // タブをクリックしてツリーを表示する
        ILocator tab = Page.GetByRole(AriaRole.Tab, new PageGetByRoleOptions { Name = "作成したタグツリー" });
        await tab.ClickAsync();

        ILocator treeContainer = Page.Locator("#jqtree-container-user-detail");
        await Expect(treeContainer).ToBeVisibleAsync();

        ILocator tagElement = Page.Locator($".jqtree-title:has-text(\"{testTagName}\")");
        await Expect(tagElement).ToBeVisibleAsync();
    }

    [Test]
    public async Task AddTag_FromUI_Then_ViewProfile_ShouldShowTag()
    {
        // 1. ユーザー登録
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var email = $"treetest_{uniqueId}@example.com";
        const string password = "Password123!";

        {
            await Page.Context.ClearCookiesAsync();
            var userName = email.Contains('@') ? email.Split('@')[0] : email;
            await Page.GotoAsync($"{_serverAddress}/auth/callback?provider=Google&code=mock-{userName}");
            await Page.WaitForURLAsync(new System.Text.RegularExpressions.Regex(@"^" + System.Text.RegularExpressions.Regex.Escape(_serverAddress) + @"/?$"), new Microsoft.Playwright.PageWaitForURLOptions { Timeout = 10000 });
            await Expect(Page.GetByRole(Microsoft.Playwright.AriaRole.Button, new Microsoft.Playwright.PageGetByRoleOptions { Name = "Logout" })).ToBeVisibleAsync(new Microsoft.Playwright.LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });
        }

        // 3. タグの追加 (直接DBへ挿入)
        var testTagName = $"MyUiTag_{uniqueId}";
        using (var scope = _factory!.AppServices.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await db.Users.FirstAsync(u => u.Email == email);
            db.Tags.Add(new Tag { Name = testTagName, Content = "Test tag added via DB.", OwnerId = user.Id, CachedWeight = 0 });
            await db.SaveChangesAsync();
        }

        // 4. プロフィール (UserDetail) へ遷移
        // Profile リンクをクリック
        await Page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Profile" }).ClickAsync();

        // タブをクリックしてツリーを表示する
        ILocator tab = Page.GetByRole(AriaRole.Tab, new PageGetByRoleOptions { Name = "作成したタグツリー" });
        await tab.ClickAsync();

        // jqTreeコンテナが表示されるか確認
        ILocator treeContainer = Page.Locator("#jqtree-container-user-detail");
        await Expect(treeContainer).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });

        // 5. タグがツリーに表示されているか確認
        ILocator tagElement = Page.Locator($".jqtree-title:has-text(\"{testTagName}\")");
        await Expect(tagElement).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });
    }

    [Test]
    public async Task UserDetail_ShouldShowUserTags_WhenParentIsOwnedByAnotherUser()
    {
        var userAId = Guid.NewGuid().ToString();
        var userBId = Guid.NewGuid().ToString();
        var userATagName = "UserATagWithForeignParent_" + Guid.NewGuid().ToString("N")[..5];
        var userBTagName = "UserBForeignParentTag_" + Guid.NewGuid().ToString("N")[..5];

        // データベースにモックユーザーとモックタグを追加
        using (IServiceScope scope = _factory.AppServices.CreateScope())
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userA = new ApplicationUser
            {
                Id = userAId,
                UserName = "user_a",
                NormalizedUserName = "USER_A",
                Email = "usera@example.com",
                NormalizedEmail = "USERA@EXAMPLE.COM"
            };
            var userB = new ApplicationUser
            {
                Id = userBId,
                UserName = "user_b",
                NormalizedUserName = "USER_B",
                Email = "userb@example.com",
                NormalizedEmail = "USERB@EXAMPLE.COM"
            };
            db.Users.AddRange(userA, userB);

            // User Bのタグ（親になるタグ）
            var tagB = new Tag { Name = userBTagName, Content = "Parent tag owned by User B", OwnerId = userB.Id };
            _ = db.Tags.Add(tagB);
            _ = await db.SaveChangesAsync(); // 保存してIdを確定

            // User Aのタグ（User Bのタグを親に持つ）
            var tagA = new Tag
            {
                Name = userATagName,
                Content = "Child tag owned by User A",
                OwnerId = userA.Id,
                ParentTagId = tagB.Id // 親が別のユーザー
            };
            _ = db.Tags.Add(tagA);
            _ = await db.SaveChangesAsync();
        }

        // User AのUserDetailページへ遷移
        _ = await Page.GotoAsync($"{_serverAddress}/User/UserDetail/{userAId}");

        // SignalRが接続され、Interactiveになるまで少し待機
        await Task.Delay(2000);

        // ページタイトルや要素がロードされるのを待機
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // BlazorのエラーUIが表示されていないことを確認
        ILocator errorUi = Page.Locator("#blazor-error-ui");
        await Expect(errorUi).ToBeHiddenAsync();

        // タブをクリックしてツリーを表示する
        ILocator tab = Page.GetByRole(AriaRole.Tab, new PageGetByRoleOptions { Name = "作成したタグツリー" });
        await tab.ClickAsync();

        ILocator treeContainer = Page.Locator("#jqtree-container-user-detail");
        await Expect(treeContainer).ToBeVisibleAsync();

        // User Aのタグが正しくルートレベルに表示されていることを確認（修正前はこれが失敗する）
        ILocator tagElement = Page.Locator($".jqtree-title:has-text(\"{userATagName}\")");
        await Expect(tagElement).ToBeVisibleAsync();
    }

    [GeneratedRegex("Click here to confirm your account", RegexOptions.IgnoreCase)]
    private static partial Regex ConfirmAccountRegex();

    [GeneratedRegex(".*Tag/TagList.*")]
    private static partial Regex TagListUrlRegex();
}