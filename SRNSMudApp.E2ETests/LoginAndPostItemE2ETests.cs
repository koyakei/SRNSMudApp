using System.Text.RegularExpressions;

using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace SRNSMudApp.E2ETests;

/// <summary>
///     認証プロバイダ（Google/LINE/GitHub）ごとのCookie認証パイプラインの健全性チェック。
///     /auth/callback?provider=X&amp;code=... から実際にログインCookieが発行され、
///     認証済みページへ到達できることのみを検証する。
///     アイテム投稿ロジックは SRNSMudApp.Tests/Components/Item/AddItemTests で
///     コンポーネントテストとしてカバー済みのため、ここでは重複検証しない。
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class LoginAndPostItemE2ETests : PageTest
{
    [OneTimeSetUp]
    public void OneTimeSetup()
    {
        _factory = SharedTestServerFixture.Factory;
        _serverAddress = SharedTestServerFixture.ServerAddress;
    }

    // ファクトリとMSSQLコンテナは SharedTestServerFixture で共有・破棄される

    [SetUp]
    public void Setup()
    {
        Page.Console += (_, msg) => TestContext.Progress.WriteLine($"[CONSOLE] {msg.Type}: {msg.Text}");
        Page.PageError += (_, error) => TestContext.Progress.WriteLine($"[PAGE ERROR]: {error}");
    }

    private CustomWebApplicationFactory _factory = null!;
    private string _serverAddress = "";

    [Test]
    public async Task GivenNewUser_WhenLoginWithGoogle_ThenAuthenticated()
    {
        await LoginWithMockProviderAsync("Google", "mock-google-test");
        await Expect(Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Logout" }))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
    }

    [Test]
    public async Task GivenNewUser_WhenLoginWithLINE_ThenAuthenticated()
    {
        await LoginWithMockProviderAsync("Line", "mock-line-test");
        await Expect(Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Logout" }))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
    }

    [Test]
    public async Task GivenNewUser_WhenLoginWithGitHub_ThenAuthenticated()
    {
        await LoginWithMockProviderAsync("Github", "mock-github-test");
        await Expect(Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Logout" }))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
    }

    /// <summary>
    ///     モックコールバックへ遷移し、Home へリダイレクトされるまで待機する。
    /// </summary>
    private async Task LoginWithMockProviderAsync(string provider, string code)
    {
        await Page.GotoAsync($"{_serverAddress}/auth/callback?provider={provider}&code={code}");
        await Page.WaitForURLAsync(new Regex(@"^" + Regex.Escape(_serverAddress) + @"/?$"),
            new PageWaitForURLOptions { Timeout = 10000 });
        await Expect(Page.Locator("body")).ToContainTextAsync("Home");
    }
}
