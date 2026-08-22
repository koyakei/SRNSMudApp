#region

using System.Text.Json;
using System.Text.RegularExpressions;

using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

#endregion

namespace SRNSMudApp.E2ETests;

[TestFixture]
public partial class PasskeyLoginE2ETests : PageTest
{
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _factory = SharedTestServerFixture.Factory;
        _serverAddress = SharedTestServerFixture.ServerAddress;
    }

    // ファクトリとMSSQLコンテナは SharedTestServerFixture で共有・破棄される
    private CustomWebApplicationFactory _factory = null!;
    private string _serverAddress = "";

    [Test]
    public async Task Register_LoginPassword_CreatePasskey_LoginPasskey()
    {
        Console.WriteLine($"ServerAddress: {_serverAddress}");
        // 1. WebAuthn モックの有効化
        (ICDPSession cdp, JsonElement? authenticatorId) =
            await WebAuthnTestHelpers.EnableVirtualAuthenticatorAsync(Page);

        // テスト用ユーザー情報
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var email = $"testuser_{uniqueId}@example.com";

        // 2. ユーザー登録
        await WebAuthnTestHelpers.LoginWithMockGoogleAsync(Page, _serverAddress!, email);

        // 4. Passkeyの作成
        _ = await Page.GotoAsync($"{_serverAddress}/Account/Manage/Passkeys");
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Add a new passkey" }).ClickAsync();

        // Passkey追加後のリダイレクトや完了確認
        try
        {
            await Page.WaitForURLAsync("**/Account/Manage/RenamePasskey/**",
                new PageWaitForURLOptions { Timeout = 10000 });
        }
        catch
        {
            var body = await Page.InnerTextAsync("body");
            Console.WriteLine("--- Timeout waiting for RenamePasskey. Page Text ---");
            Console.WriteLine(body);
            throw;
        }

        // 5. ログアウト
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Logout" }).ClickAsync();

        // 6. Passkeyによるログイン
        await WebAuthnTestHelpers.LoginWithMockGoogleAsync(Page, _serverAddress!, email);

        // クリーンアップ
        await WebAuthnTestHelpers.RemoveVirtualAuthenticatorAsync(cdp, authenticatorId);
    }

    [GeneratedRegex("Click here to confirm your account", RegexOptions.IgnoreCase)]
    private static partial Regex ConfirmAccountRegex();
}
