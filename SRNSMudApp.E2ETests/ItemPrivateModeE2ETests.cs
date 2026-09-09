// ItemPrivateModeE2ETests.cs
#region

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

using SRNSMudApp.Data;

#endregion

namespace SRNSMudApp.E2ETests;

/// <summary>
///     プライベートモード（フォロワー限定）のアイテムが、非フォロワーには非表示であり、
///     フォロー後に表示されることを検証する E2E テスト。
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class ItemPrivateModeE2ETests : PageTest
{
    private CustomWebApplicationFactory _factory = null!;
    private string _serverAddress = "";

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

    [Test]
    public async Task GivenPrivateFollowersOnlyItem_WhenNotFollowing_Hidden_WhenFollowing_Visible()
    {
        var tid = Guid.NewGuid().ToString("N")[..8];
        var authorEmail = $"author-{tid}@example.com";
        var viewerEmail = $"viewer-{tid}@example.com";
        var authorName = $"Author_{tid}";
        var viewerName = $"Viewer_{tid}";

        string authorId;
        int privateItemId;
        var privateContent = $"Secret_Content_{tid}";

        // 1. Author と Private Item を DB に作成
        using (IServiceScope scope = _factory.AppServices.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var author = new ApplicationUser
            {
                UserName = authorName,
                NormalizedUserName = authorName.ToUpperInvariant(),
                Email = authorEmail,
                NormalizedEmail = authorEmail.ToUpperInvariant(),
                EmailConfirmed = true
            };
            db.Users.Add(author);
            await db.SaveChangesAsync();
            authorId = author.Id;

            var item = new Item
            {
                Content = privateContent,
                OwnerId = author.Id,
                IsPrivate = true,
                TargetUserGroupId = null
            };
            db.Items.Add(item);
            await db.SaveChangesAsync();
            privateItemId = item.Id;
        }

        // 2. Viewer でログイン
        await WebAuthnTestHelpers.LoginWithMockGoogleAsync(Page, _serverAddress, viewerEmail);

        // 3. 一覧ページ (/Item/ItemList) に遷移し、プライベートアイテムが表示されていないことを確認
        await Page.GotoAsync($"{_serverAddress}/Item/ItemList");
        await Page.WaitForSelectorAsync("body", new PageWaitForSelectorOptions { Timeout = 15000 });

        ILocator itemCardBeforeFollow = Page.Locator($"#item-card-{privateItemId}");
        await Expect(itemCardBeforeFollow).ToHaveCountAsync(0);

        // 4. Author の詳細ページ (/User/UserDetail/{authorId}) に遷移し、Author をフォロー
        await Page.GotoAsync($"{_serverAddress}/User/UserDetail/{authorId}");
        await Page.WaitForSelectorAsync("#jqtree-container-user-detail, .tree-container, body", new PageWaitForSelectorOptions { Timeout = 15000 });

        ILocator followButton = Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "フォロー", Exact = true });
        await Expect(followButton).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
        await followButton.ClickAsync();

        // フォロー完了を確認
        ILocator unfollowButton = Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "フォロー解除", Exact = true });
        await Expect(unfollowButton).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });

        // 5. 再度一覧ページ (/Item/ItemList) に遷移し、プライベートアイテムが表示され、バッジ「フォロワー限定」があることを確認
        await Page.GotoAsync($"{_serverAddress}/Item/ItemList");
        ILocator itemCardAfterFollow = Page.Locator($"#item-card-{privateItemId}");
        await Expect(itemCardAfterFollow).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });
        await Expect(itemCardAfterFollow).ToContainTextAsync(privateContent);
        await Expect(itemCardAfterFollow).ToContainTextAsync("フォロワー限定");
    }
}