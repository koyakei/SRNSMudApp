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
        _factory = new CustomWebApplicationFactory();
        _factory.EnsureServer(); // Initialize the host
        _serverAddress = _factory.ServerAddress;
    }

    [OneTimeTearDown]
    public void OneTimeTearDown() => _factory?.Dispose();

    private CustomWebApplicationFactory? _factory;
    private string? _serverAddress;

    [Test]
    public async Task Register_LoginPassword_CreatePasskey_LoginPasskey()
    {
        Console.WriteLine($"ServerAddress: {_serverAddress}");
        // 1. WebAuthn モックの有効化
        ICDPSession cdp = await Context.NewCDPSessionAsync(Page);
        _ = await cdp.SendAsync("WebAuthn.enable");

        Dictionary<string, object> authenticatorOptions = new()
        {
            { "protocol", "ctap2" },
            { "transport", "usb" },
            { "hasResidentKey", true },
            { "hasUserVerification", true },
            { "isUserVerified", true }
        };

        JsonElement? authenticatorId = await cdp.SendAsync("WebAuthn.addVirtualAuthenticator",
            new Dictionary<string, object> { { "options", authenticatorOptions } });

        // テスト用ユーザー情報
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var email = $"testuser_{uniqueId}@example.com";
        const string password = "Password123!";

        // 2. ユーザー登録
        {
            await Page.Context.ClearCookiesAsync();
            var userName = email.Contains('@') ? email.Split('@')[0] : email;
            await Page.GotoAsync($"{_serverAddress}/auth/callback?provider=Google&code=mock-{userName}");
            await Page.WaitForURLAsync(new System.Text.RegularExpressions.Regex(@"^" + System.Text.RegularExpressions.Regex.Escape(_serverAddress) + @"/?$"), new Microsoft.Playwright.PageWaitForURLOptions { Timeout = 10000 });
            await Expect(Page.GetByRole(Microsoft.Playwright.AriaRole.Button, new Microsoft.Playwright.PageGetByRoleOptions { Name = "Logout" })).ToBeVisibleAsync(new Microsoft.Playwright.LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });
        }

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
        {
            await Page.Context.ClearCookiesAsync();
            var userName = email.Contains('@') ? email.Split('@')[0] : email;
            await Page.GotoAsync($"{_serverAddress}/auth/callback?provider=Google&code=mock-{userName}");
            await Page.WaitForURLAsync(new System.Text.RegularExpressions.Regex(@"^" + System.Text.RegularExpressions.Regex.Escape(_serverAddress) + @"/?$"), new Microsoft.Playwright.PageWaitForURLOptions { Timeout = 10000 });
            await Expect(Page.GetByRole(Microsoft.Playwright.AriaRole.Button, new Microsoft.Playwright.PageGetByRoleOptions { Name = "Logout" })).ToBeVisibleAsync(new Microsoft.Playwright.LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });
        }

        // クリーンアップ
        if (authenticatorId != null)
        {
            _ = await cdp.SendAsync("WebAuthn.removeVirtualAuthenticator",
                new Dictionary<string, object>
                {
                    { "authenticatorId", authenticatorId.Value.GetProperty("authenticatorId").GetString() }
                });
        }
    }

    [GeneratedRegex("Click here to confirm your account", RegexOptions.IgnoreCase)]
    private static partial Regex ConfirmAccountRegex();
}