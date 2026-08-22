#region

using System.Text.Json;
using System.Text.RegularExpressions;

using Microsoft.Playwright;

#endregion

namespace SRNSMudApp.E2ETests;

/// <summary>
///     PasskeyLoginE2ETests / PasskeyRenameE2ETests で共用する
///     WebAuthn仮想オーセンティケータとモックログインのヘルパー。
/// </summary>
public static class WebAuthnTestHelpers
{
    /// <summary>
    ///     CDPセッションでWebAuthnを有効化し、仮想オーセンティケータを追加する。
    /// </summary>
    public static async Task<(ICDPSession Cdp, JsonElement? AuthenticatorId)> EnableVirtualAuthenticatorAsync(
        IPage page)
    {
        ICDPSession cdp = await page.Context.NewCDPSessionAsync(page);
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

        return (cdp, authenticatorId);
    }

    /// <summary>
    ///     仮想オーセンティケータを後処理で削除する。
    /// </summary>
    public static async Task RemoveVirtualAuthenticatorAsync(ICDPSession cdp, JsonElement? authenticatorId)
    {
        if (authenticatorId != null)
        {
            _ = await cdp.SendAsync("WebAuthn.removeVirtualAuthenticator",
                new Dictionary<string, object>
                {
                    { "authenticatorId", authenticatorId.Value.GetProperty("authenticatorId").GetString() }
                });
        }
    }

    /// <summary>
    ///     モックGoogleコールバックへ遷移し、ログイン完了（Logoutボタン表示）まで待機する。
    /// </summary>
    public static async Task LoginWithMockGoogleAsync(IPage page, string serverAddress, string email)
    {
        await page.Context.ClearCookiesAsync();
        var userName = email.Contains('@') ? email.Split('@')[0] : email;
        await page.GotoAsync($"{serverAddress}/auth/callback?provider=Google&code=mock-{userName}");
        await page.WaitForURLAsync(new Regex(@"^" + Regex.Escape(serverAddress) + @"/?$"),
            new PageWaitForURLOptions { Timeout = 10000 });
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Logout" }))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });
    }
}
