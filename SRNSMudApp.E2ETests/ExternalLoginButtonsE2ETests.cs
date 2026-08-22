using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace SRNSMudApp.E2ETests;

/// <summary>
///     ログインページの外部認証ボタン描画と、実JS（window.customAuth.renderGoogleButton 等）
///     のグローバル関数が実際に定義されていることを検証する。実ブラウザAPI依存のため
///     コンポーネントテスト化は不可（維持・変更なし）。
///     注意: 環境変数 Authentication__Google__ClientId をプロセス全体に設定する。
///     本クラスには [Parallelizable] が付いておらず、アセンブリレベルの並列化も無効なため
///     現状は副作用がないが、将来並列化する場合は環境変数依存をやめて
///     WebApplicationFactory の設定注入へリファクタリングすること。
/// </summary>
[TestFixture]
public class ExternalLoginButtonsE2ETests : PageTest
{
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        // ダミーClientIdの設定はホスト構築前に必要なため SharedTestServerFixture で行う
        _factory = SharedTestServerFixture.Factory;
        _serverAddress = SharedTestServerFixture.ServerAddress;
    }

    // ファクトリとMSSQLコンテナは SharedTestServerFixture で共有・破棄される
    private CustomWebApplicationFactory _factory = null!;
    private string _serverAddress = "";

    [Test]
    public async Task LoginButtons_ShouldBeRendered()
    {
        _ = await Page.GotoAsync($"{_serverAddress}/Account/Login");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Verify the Google login button container is rendered in the DOM
        ILocator googleButtonContainer = Page.Locator("#google-login-button-container");
        await Expect(googleButtonContainer).ToBeAttachedAsync();

        // Verify that customAuth.renderGoogleButton is defined in JS
        var isRenderButtonDefined =
            await Page.EvaluateAsync<bool>("typeof window.customAuth.renderGoogleButton === 'function'");
        Assert.That(isRenderButtonDefined, Is.True, "customAuth.renderGoogleButton should be defined in JS");

        // LINE Login button
        ILocator lineButton = Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Continue with LINE" });
        await Expect(lineButton).ToBeVisibleAsync();

        // GitHub Login button
        ILocator githubButton =
            Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Continue with GitHub" });
        await Expect(githubButton).ToBeVisibleAsync();
    }
}