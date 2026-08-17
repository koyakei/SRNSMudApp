using System.Text.RegularExpressions;

using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace SRNSMudApp.E2ETests;

[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class TagDeletionTrackingE2ETests : PageTest
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

    private CustomWebApplicationFactory _factory = null!;
    private string _baseUrl = null!;

    private string? _serverAddress;

    [Test]
    public async Task DeleteTag_WithSameOwnerForTagAndItem_ShouldNotThrowTrackingException()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var email = $"testuser_tagdelete_{uniqueId}@example.com";
        var userName = email.Contains('@') ? email.Split('@')[0] : email;

        // 1. Navigate and Login (Mock)
        await Page.Context.ClearCookiesAsync();
        await Page.GotoAsync($"{_serverAddress}/auth/callback?provider=Google&code=mock-{userName}");
        await Page.WaitForURLAsync(new Regex(@"^" + Regex.Escape(_serverAddress) + @"/?$"));

        // 2. Post an Item
        await Page.GotoAsync($"{_serverAddress}/Item/ItemList");
        await Task.Delay(2000); // Wait for SignalR connection

        var testContent = $"Item to test tag deletion {uniqueId}";
        await Page.GetByPlaceholder("新しいアイテムのコンテンツを入力...").FillAsync(testContent);
        await Task.Delay(1500); // Wait for re-render if prerendering
        await Page.GetByPlaceholder("新しいアイテムのコンテンツを入力...").FillAsync(testContent);

        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "保存" }).ClickAsync();

        await Task.Delay(1000); // Wait for the item to be added to the list

        // 3. Add a tag to the item
        // Ensure item is visible
        await Expect(Page.Locator($"text={testContent}").First).ToBeVisibleAsync();

        // Click Add Tag button on the item card
        await Page.Locator($".mud-card:has-text('{testContent}')").Locator("button[title='タグを追加']").ClickAsync();

        // Type tag name and submit
        ILocator dialog = Page.Locator(".mud-dialog");
        await dialog.Locator("text=新規作成").ClickAsync();
        await dialog.GetByLabel("タグ名").FillAsync("TrackingTestTag");
        await dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "作成して追加" }).ClickAsync();

        // Wait for the tag to appear
        ILocator tagChip = Page.Locator($".mud-card:has-text('{testContent}')")
            .Locator(".mud-chip:has-text('TrackingTestTag')");
        await Expect(tagChip).ToBeVisibleAsync();

        // 4. Delete the tag
        // Click the close icon on the chip
        ILocator closeButton = tagChip.Locator(".mud-chip-close-button").First;
        await closeButton.ClickAsync();

        await Task.Delay(1000); // Wait for the deletion to process over SignalR

        // 5. Verify the tag is deleted and no error UI appears
        await Expect(Page.Locator("#blazor-error-ui")).Not.ToBeVisibleAsync();
        await Expect(tagChip).Not.ToBeVisibleAsync();
    }
}