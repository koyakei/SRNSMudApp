#region

using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

#endregion

namespace SRNSMudApp.E2ETests;

[TestFixture]
public partial class ContractManagementE2ETests : PageTest
{
    private CustomWebApplicationFactory? _factory;
    private string? _serverAddress;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _factory = new CustomWebApplicationFactory();
        _factory.EnsureServer(); // Initialize the host
        _serverAddress = _factory.ServerAddress;
    }

    [OneTimeTearDown]
    public void OneTimeTearDown() => _factory?.Dispose();

    private async Task RegisterAndLoginAsync(string email, string password)
    {
        await Page.Context.ClearCookiesAsync();
        var userName = email.Contains('@') ? email.Split('@')[0] : email;
        await Page.GotoAsync($"{_serverAddress}/auth/callback?provider=Google&code=mock-{userName}");
        await Page.WaitForURLAsync(new Regex(@"^" + Regex.Escape(_serverAddress) + @"/?$"), new PageWaitForURLOptions { Timeout = 10000 });
        await Expect(Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Logout" })).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });
    }

    [GeneratedRegex("Click here to confirm your account", RegexOptions.IgnoreCase)]
    private static partial Regex ConfirmAccountRegex();
}
