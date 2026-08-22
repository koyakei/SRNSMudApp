#region

using System.Text.Json;

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
        _factory = SharedTestServerFixture.Factory;
        _serverAddress = SharedTestServerFixture.ServerAddress;
    }

    // ファクトリとMSSQLコンテナは SharedTestServerFixture で共有・破棄される
    private CustomWebApplicationFactory _factory = null!;
    private string _serverAddress = "";

    [Test]
    public async Task RenamePasskey_FailsIfModelBindingIsBroken()
    {
        Console.WriteLine($"ServerAddress: {_serverAddress}");
        (ICDPSession cdp, JsonElement? authenticatorId) =
            await WebAuthnTestHelpers.EnableVirtualAuthenticatorAsync(Page);

        // ユーザー情報
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var email = $"testuser_rename_{uniqueId}@example.com";

        // ユーザー登録
        await WebAuthnTestHelpers.LoginWithMockGoogleAsync(Page, _serverAddress!, email);

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

        await WebAuthnTestHelpers.RemoveVirtualAuthenticatorAsync(cdp, authenticatorId);
    }
}
