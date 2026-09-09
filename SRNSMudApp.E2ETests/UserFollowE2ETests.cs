// UserFollowE2ETests.cs
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

using SRNSMudApp.Data;

namespace SRNSMudApp.E2ETests;

/// <summary>
///     ユーザー詳細画面におけるユーザーフォロー・フォロー解除のトグル動作、
///     およびフォロワーカウント・一覧の表示を検証する E2E テスト。
/// </summary>
[TestFixture]
public class UserFollowE2ETests : PageTest
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
    public async Task GivenAuthenticatedUser_WhenVisitAnotherUserProfile_CanFollowAndUnfollow()
    {
        // 1. UserA でログイン
        var emailA = $"followA-{Guid.NewGuid():N}@example.com";
        await WebAuthnTestHelpers.LoginWithMockGoogleAsync(Page, _serverAddress, emailA);

        // 2. UserB を DB に作成
        string targetUserId;
        var targetUserName = $"FollowTarget_{Guid.NewGuid():N}"[..18];
        using (IServiceScope scope = _factory.AppServices.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userB = new ApplicationUser
            {
                UserName = targetUserName,
                Email = $"followB-{Guid.NewGuid():N}@example.com",
                NormalizedUserName = targetUserName.ToUpperInvariant(),
                EmailConfirmed = true
            };
            db.Users.Add(userB);
            await db.SaveChangesAsync();
            targetUserId = userB.Id;
        }

        // 3. UserB の詳細ページに遷移
        await Page.GotoAsync($"{_serverAddress}/User/UserDetail/{targetUserId}");
        await Page.WaitForSelectorAsync("#jqtree-container-user-detail, .tree-container, body", new PageWaitForSelectorOptions { Timeout = 15000 });

        // フォローボタンが表示されていることを確認
        ILocator followButton = Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "フォロー", Exact = true });
        await Expect(followButton).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });

        // フォロワー数が 0 であることを確認
        await Expect(Page.Locator("body")).ToContainTextAsync("0 フォロワー");

        // 4. フォローボタンをクリック
        await followButton.ClickAsync();

        // ボタンが「フォロー解除」に切り替わり、フォロワー数が 1 に更新されることを確認
        ILocator unfollowButton = Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "フォロー解除", Exact = true });
        await Expect(unfollowButton).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
        await Expect(Page.Locator("body")).ToContainTextAsync("1 フォロワー");

        // 5. フォロワー一覧タブをクリックして UserA が含まれていることを確認
        ILocator followerTab = Page.GetByRole(AriaRole.Tab, new PageGetByRoleOptions { NameRegex = new System.Text.RegularExpressions.Regex("フォロワー") });
        await followerTab.ClickAsync();
        var userAName = emailA.Split('@')[0];
        await Expect(Page.Locator("body")).ToContainTextAsync(userAName);

        // 6. 再度クリックしてフォロー解除
        await unfollowButton.ClickAsync();

        // ボタンが「フォロー」に戻り、フォロワー数が 0 に戻ることを確認
        await Expect(followButton).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
        await Expect(Page.Locator("body")).ToContainTextAsync("0 フォロワー");
    }
}