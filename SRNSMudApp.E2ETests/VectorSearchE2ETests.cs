#region

using System.Text.RegularExpressions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

using SRNSMudApp.Data;
using SRNSMudApp.Services;

#endregion

namespace SRNSMudApp.E2ETests;

[TestFixture]
public partial class VectorSearchE2ETests : PageTest
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

    private async Task RegisterAndCreateYakuzaTagAsync()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var email = $"vector_{uniqueId}@example.com";
        const string password = "Password123!";

        {
            await Page.Context.ClearCookiesAsync();
            var userName = email.Contains('@') ? email.Split('@')[0] : email;
            await Page.GotoAsync($"{_serverAddress}/auth/callback?provider=Google&code=mock-{userName}");
            await Page.WaitForURLAsync(
                new System.Text.RegularExpressions.Regex(@"^" +
                                                         System.Text.RegularExpressions.Regex.Escape(_serverAddress) +
                                                         @"/?$"),
                new Microsoft.Playwright.PageWaitForURLOptions { Timeout = 10000 });
            await Expect(Page.GetByRole(Microsoft.Playwright.AriaRole.Button,
                    new Microsoft.Playwright.PageGetByRoleOptions { Name = "Logout" }))
                .ToBeVisibleAsync(new Microsoft.Playwright.LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });
        }

        string userId;
        using IServiceScope scope = _factory.AppServices.CreateScope();
        ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        ITagEmbeddingService embeddingService = scope.ServiceProvider.GetRequiredService<ITagEmbeddingService>();

        ApplicationUser? user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
        Assert.That(user, Is.Not.Null);
        userId = user.Id;

        const string tagName = "ヤクザ";
        ReadOnlyMemory<float> embedding = await embeddingService.GenerateEmbeddingAsync(tagName);

        var dummyTargetTag = new Tag
        {
            Name = $"Target_{uniqueId}",
            OwnerId = userId,
            Embedding = (await embeddingService.GenerateEmbeddingAsync($"Target_{uniqueId}")).ToArray()
        };
        _ = db.Tags.Add(dummyTargetTag);

        var tag = new Tag { Name = tagName, OwnerId = userId, Embedding = embedding.ToArray() };
        _ = db.Tags.Add(tag);
        _ = await db.SaveChangesAsync();
    }

    [Test]
    public async Task VectorSearch_TagSearchPage_ShouldFindSemanticallySimilarTag()
    {
        await RegisterAndCreateYakuzaTagAsync();

        _ = await Page.GotoAsync($"{_serverAddress}/Tag/TagSearch");
        await Task.Delay(1500);

        await Page.GetByLabel("タグを検索").FillAsync("反社会的勢力");

        ILocator autocompleteItem = Page.Locator(".mud-popover .mud-list-item:has-text(\"ヤクザ\")").First;
        await Expect(autocompleteItem).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
    }

    [Test]
    public async Task VectorSearch_ItemList_ShouldFindSemanticallySimilarTag()
    {
        await RegisterAndCreateYakuzaTagAsync();

        _ = await Page.GotoAsync($"{_serverAddress}/Item/ItemList");
        await Task.Delay(1500);

        await Page.GetByPlaceholder("タグで絞り込み...").FillAsync("反社会的勢力");

        ILocator autocompleteItem = Page.Locator(".mud-paper .suggestion-item:has-text(\"ヤクザ\")").First;
        await Expect(autocompleteItem).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
    }

    [Test]
    public async Task VectorSearch_ImportTag_ShouldFindSemanticallySimilarTag()
    {
        await RegisterAndCreateYakuzaTagAsync();

        _ = await Page.GotoAsync($"{_serverAddress}/import-tag");
        await Task.Delay(1500);

        await Page.GetByLabel("親タグを検索").FillAsync("反社会的勢力");

        ILocator autocompleteItem = Page.Locator(".mud-popover .mud-list-item:has-text(\"ヤクザ\")").First;
        await Expect(autocompleteItem).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
    }

    [Test]
    public async Task VectorSearch_TagAddDialog_ShouldFindSemanticallySimilarTag()
    {
        await RegisterAndCreateYakuzaTagAsync();

        _ = await Page.GotoAsync($"{_serverAddress}/Tag/TagList");
        await Task.Delay(1500);

        ILocator addTagButton = Page.Locator("button[title='タグを追加']").First;
        await Expect(addTagButton).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
        await addTagButton.ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Dialog)).ToBeVisibleAsync();

        await Page.Locator(".mud-dialog .mud-input-control:has-text('タグを検索') input").FillAsync("反社会的勢力");
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "検索" }).ClickAsync();

        ILocator radioItem = Page.Locator(".mud-dialog .mud-radio:has-text(\"ヤクザ\")").First;
        await Expect(radioItem).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
    }

    [GeneratedRegex("Click here to confirm your account", RegexOptions.IgnoreCase)]
    private static partial Regex ConfirmAccountRegex();
}

public union Pet(Cat, Dog, Bird);

public record class Cat(string Name);

public record class Dog(string Name);

public record class Bird(string Name);