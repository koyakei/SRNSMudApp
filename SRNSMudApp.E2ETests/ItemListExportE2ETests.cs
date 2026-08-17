#region

using System.Text.Json;
using System.Text.RegularExpressions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

using SRNSMudApp.Data;

#endregion

namespace SRNSMudApp.E2ETests;

[TestFixture]
public partial class ItemListExportE2ETests : PageTest
{
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _factory = new CustomWebApplicationFactory();
        _factory.EnsureServer();
        _serverAddress = _factory.ServerAddress;
    }

    [OneTimeTearDown]
    public void OneTimeTearDown() => _factory?.Dispose();

    private CustomWebApplicationFactory? _factory;
    private string? _serverAddress;

    private async Task<(string email, string userId)> RegisterAndCreateItemAsync()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var email = $"export_{uniqueId}@example.com";
        const string password = "Password123!";

        {
            await Page.Context.ClearCookiesAsync();
            var userName = email.Contains('@') ? email.Split('@')[0] : email;
            await Page.GotoAsync($"{_serverAddress}/auth/callback?provider=Google&code=mock-{userName}");
            await Page.WaitForURLAsync(new Regex(@"^" + Regex.Escape(_serverAddress) + @"/?$"),
                new PageWaitForURLOptions { Timeout = 10000 });
            await Expect(Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Logout" }))
                .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });
        }

        string userId;
        using (IServiceScope scope = _factory.AppServices.CreateScope())
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            ApplicationUser? user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
            Assert.That(user, Is.Not.Null);
            userId = user.Id;

            var item = new Item
            {
                Content = $"This is a test item with a URL: https://example.com for {uniqueId}", OwnerId = userId
            };
            _ = db.Items.Add(item);
            _ = await db.SaveChangesAsync();
        }

        return (email, userId);
    }

    [Test]
    public async Task ExportToJson_ShouldIncludeLinkPreview()
    {
        _ = await RegisterAndCreateItemAsync();

        _ = await Page.GotoAsync($"{_serverAddress}/Item/ItemList");
        await Task.Delay(1500);

        IDownload download = await Page.RunAndWaitForDownloadAsync(async () =>
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "JSONエクスポート" }).ClickAsync());

        await using Stream stream = await download.CreateReadStreamAsync();
        using var reader = new StreamReader(stream);
        var jsonContent = await reader.ReadToEndAsync();

        Assert.That(jsonContent, Is.Not.Empty);

        List<ExportItemDtoTest>? exportList =
            JsonSerializer.Deserialize<List<ExportItemDtoTest>>(jsonContent, SerializerOptions);

        Assert.That(exportList, Is.Not.Null);
        var hasLinkPreview = false;
        foreach (ExportLinkPreviewDtoTest lp in exportList
                     .Where(item => item.LinkPreviews is { Count: > 0 })
                     .SelectMany(item => item.LinkPreviews)
                     .Where(lp => lp.Url.Contains("example.com")))
        {
            hasLinkPreview = true;
            Assert.That(lp.Title, Is.Not.Null.And.Not.Empty);
        }

        Assert.That(hasLinkPreview, Is.True, "JSON output should contain the Link Preview data for the URL.");
    }

    private class ExportItemDtoTest
    {
        public string Content { get; init; } = string.Empty;
        public List<ExportLinkPreviewDtoTest> LinkPreviews { get; init; } = [];
    }

    private class ExportLinkPreviewDtoTest
    {
        public string Url { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public string ImageUrl { get; init; } = string.Empty;
        public string SiteName { get; init; } = string.Empty;
    }

    private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNameCaseInsensitive = true };

    [GeneratedRegex("Click here to confirm your account", RegexOptions.IgnoreCase)]
    private static partial Regex ConfirmAccountRegex();
}