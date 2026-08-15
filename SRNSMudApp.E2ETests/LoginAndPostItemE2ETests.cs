using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using SRNSMudApp.Data;

namespace SRNSMudApp.E2ETests;

[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class LoginAndPostItemE2ETests : PageTest
{
    private CustomWebApplicationFactory _factory = null!;
    private string _serverAddress = "";

    [OneTimeSetUp]
    public void OneTimeSetup()
    {
        _factory = new CustomWebApplicationFactory();
        _factory.EnsureServer();
        _serverAddress = _factory.ServerAddress;
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _factory.Dispose();
    }

    [SetUp]
    public void Setup()
    {
        Page.Console += (_, msg) => TestContext.Progress.WriteLine($"[CONSOLE] {msg.Type}: {msg.Text}");
        Page.PageError += (_, error) => TestContext.Progress.WriteLine($"[PAGE ERROR]: {error}");
        // Remove the global Dialog handler because we will handle it in the specific test
    }

    private async Task<string> CreateTestInvitationAsync()
    {
        using var scope = _factory.AppServices.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        
        // Ensure dummy admin user exists
        var adminId = "dummy-admin-id";
        if (!await db.Users.AnyAsync(u => u.Id == adminId))
        {
            db.Users.Add(new ApplicationUser
            {
                Id = adminId,
                UserName = "admin@example.com",
                Email = "admin@example.com",
                EmailConfirmed = true
            });
            await db.SaveChangesAsync();
        }

        var invite = new Invitation
        {
            InvitationCode = Guid.NewGuid().ToString("N")[..10],
            Email = "test@example.com",
            CreatedDate = DateTime.UtcNow,
            ExpirationDate = DateTime.UtcNow.AddDays(1),
            OwnerId = adminId,
            InvitedByAdminId = adminId
        };
        db.Invitations.Add(invite);
        await db.SaveChangesAsync();
        return invite.InvitationCode;
    }

    [Test]
    public async Task GivenNewUser_WhenLoginWithGoogle_ThenCanPostItem()
    {
        // Navigate to the Google callback directly since Google UI isn't testable
        await Page.GotoAsync($"{_serverAddress}/auth/callback?provider=Google&code=mock-google-test");

        // Wait for redirect to happen (either to Home or some other page)
        // Usually, successful login redirects to Home "/"
        await Page.WaitForURLAsync(new Regex(@"^" + Regex.Escape(_serverAddress) + @"/?$"), new PageWaitForURLOptions { Timeout = 10000 });
        await Expect(Page.Locator("body")).ToContainTextAsync("Home");

        // Go to Item List page
        await Page.GotoAsync($"{_serverAddress}/Item/ItemList");
        
        // Wait for Blazor Server circuit to connect before interacting
        await Task.Delay(1500);
        
        // Ensure the input field is visible
        await Task.Delay(1500);
        var input = Page.GetByPlaceholder("新しいアイテムのコンテンツを入力...");
        await input.WaitForAsync();
        
        var contentText = "Test item from Google user " + Guid.NewGuid().ToString();
        await input.FillAsync(contentText);
        
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "保存" }).ClickAsync();
        
        // Wait for the item to appear in the list below or a snackbar
        await Expect(Page.Locator("body")).ToContainTextAsync("アイテムが正常に保存されました。");
        await Expect(Page.Locator("body")).ToContainTextAsync(contentText);
    }

    [Test]
    public async Task GivenNewUser_WhenLoginWithLINE_ThenCanPostItem()
    {
        await Page.GotoAsync($"{_serverAddress}/auth/callback?provider=Line&code=mock-line-test");

        await Page.WaitForURLAsync(new Regex(@"^" + Regex.Escape(_serverAddress) + @"/?$"), new PageWaitForURLOptions { Timeout = 10000 });
        await Expect(Page.Locator("body")).ToContainTextAsync("Home");

        await Page.GotoAsync($"{_serverAddress}/Item/ItemList");
        
        await Task.Delay(1500);
        var input = Page.GetByPlaceholder("新しいアイテムのコンテンツを入力...");
        await input.WaitForAsync();
        
        var contentText = "Test item from LINE user " + Guid.NewGuid().ToString();
        await input.FillAsync(contentText);
        
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "保存" }).ClickAsync();
        
        await Expect(Page.Locator("body")).ToContainTextAsync("アイテムが正常に保存されました。");
        await Expect(Page.Locator("body")).ToContainTextAsync(contentText);
    }

    [Test]
    public async Task GivenNewUser_WhenLoginWithGitHub_ThenCanPostItem()
    {
        await Page.GotoAsync($"{_serverAddress}/auth/callback?provider=Github&code=mock-github-test");

        await Page.WaitForURLAsync(new Regex(@"^" + Regex.Escape(_serverAddress) + @"/?$"), new PageWaitForURLOptions { Timeout = 10000 });
        await Expect(Page.Locator("body")).ToContainTextAsync("Home");

        await Page.GotoAsync($"{_serverAddress}/Item/ItemList");
        
        await Task.Delay(1500);
        var input = Page.GetByPlaceholder("新しいアイテムのコンテンツを入力...");
        await input.WaitForAsync();
        
        var contentText = "Test item from GitHub user " + Guid.NewGuid().ToString();
        await input.FillAsync(contentText);
        
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "保存" }).ClickAsync();
        
        await Expect(Page.Locator("body")).ToContainTextAsync("アイテムが正常に保存されました。");
        await Expect(Page.Locator("body")).ToContainTextAsync(contentText);
    }


}
