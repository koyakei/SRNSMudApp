#region

using System.Text.Json;
using System.Text.RegularExpressions;

using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

#endregion

namespace SRNSMudApp.E2ETests;

[TestFixture]
public partial class PasskeyRenameE2ETests : PageTest
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
    public async Task RenamePasskey_FailsIfModelBindingIsBroken()
    {
        Console.WriteLine($"ServerAddress: {_serverAddress}");
        ICDPSession cdp = await Context.NewCDPSessionAsync(Page);
        _ = await cdp.SendAsync("WebAuthn.enable");

        var authenticatorOptions = new Dictionary<string, object>
        {
            { "protocol", "ctap2" },
            { "transport", "usb" },
            { "hasResidentKey", true },
            { "hasUserVerification", true },
            { "isUserVerified", true }
        };

        JsonElement? authenticatorId = await cdp.SendAsync("WebAuthn.addVirtualAuthenticator",
            new Dictionary<string, object> { { "options", authenticatorOptions } });

        // ユーザー情報
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var email = $"testuser_rename_{uniqueId}@example.com";
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

        // Passkey作成
        _ = await Page.GotoAsync($"{_serverAddress}/Account/Manage/Passkeys");
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Add a new passkey" }).ClickAsync();

        await Page.WaitForURLAsync("**/Account/Manage/RenamePasskey/**", new PageWaitForURLOptions { Timeout = 10000 });

        // Passkeyのリネームを実行
        await Page.GetByLabel("Passkey name").FillAsync("My Test Passkey");
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Continue" }).ClickAsync();

        // 「The Name field is required.」というエラーが出ないことを確認する（バグがある場合はここでタイムアウト失敗する）
        await Expect(Page.GetByText("The Name field is required.")).Not
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 2000 });

        // 成功リダイレクトを待つ
        await Page.WaitForURLAsync("**/Account/Manage/Passkeys", new PageWaitForURLOptions { Timeout = 5000 });
        await Expect(Page.GetByText("Passkey updated successfully.")).ToBeVisibleAsync();

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