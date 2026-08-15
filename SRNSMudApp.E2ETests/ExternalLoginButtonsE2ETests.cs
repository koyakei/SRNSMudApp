using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace SRNSMudApp.E2ETests;

[TestFixture]
public partial class ExternalLoginButtonsE2ETests : PageTest
{
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        Environment.SetEnvironmentVariable("Authentication__Google__ClientId", "1234567890-dummy.apps.googleusercontent.com");
        _factory = new CustomWebApplicationFactory();
        _factory.EnsureServer();
        _serverAddress = _factory.ServerAddress;
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        Environment.SetEnvironmentVariable("Authentication__Google__ClientId", null);
        _factory?.Dispose();
    }

    private CustomWebApplicationFactory? _factory;
    private string? _serverAddress;

    [Test]
    public async Task LoginButtons_ShouldBeRendered()
    {
        _ = await Page.GotoAsync($"{_serverAddress}/Account/Login");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Verify the Google login button container is rendered in the DOM
        ILocator googleButtonContainer = Page.Locator("#google-login-button-container");
        await Expect(googleButtonContainer).ToBeAttachedAsync();

        // Verify that customAuth.renderGoogleButton is defined in JS
        var isRenderButtonDefined = await Page.EvaluateAsync<bool>("typeof window.customAuth.renderGoogleButton === 'function'");
        Assert.That(isRenderButtonDefined, Is.True, "customAuth.renderGoogleButton should be defined in JS");

        // LINE Login button
        ILocator lineButton = Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Continue with LINE" });
        await Expect(lineButton).ToBeVisibleAsync();

        // GitHub Login button
        ILocator githubButton = Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Continue with GitHub" });
        await Expect(githubButton).ToBeVisibleAsync();
    }
}
