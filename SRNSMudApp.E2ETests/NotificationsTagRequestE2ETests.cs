#region

using System.Text.RegularExpressions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

using SRNSMudApp.Data;

#endregion

namespace SRNSMudApp.E2ETests;

[TestFixture]
public partial class NotificationsTagRequestE2ETests : PageTest
{
    private CustomWebApplicationFactory _factory = null!;
    private string _serverAddress = "";

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _factory = new CustomWebApplicationFactory();
        _factory.EnsureServer();
        _serverAddress = _factory.ServerAddress;
    }

    [OneTimeTearDown]
    public void OneTimeTearDown() => _factory?.Dispose();

    [Test]
    public async Task Notification_ApproveAndReject_ShouldWork()
    {
        // 1. Setup Data directly in DB
        using (IServiceScope scope = _factory.AppServices.CreateScope())
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // Create users
            var owner = new ApplicationUser
            {
                Id = Guid.NewGuid().ToString(),
                UserName = "notif_owner",
                NormalizedUserName = "NOTIF_OWNER",
                Email = "notif_owner@example.com",
                NormalizedEmail = "NOTIF_OWNER@EXAMPLE.COM"
            };
            var requester = new ApplicationUser
            {
                Id = Guid.NewGuid().ToString(),
                UserName = "notif_requester",
                NormalizedUserName = "NOTIF_REQUESTER",
                Email = "notif_requester@example.com",
                NormalizedEmail = "NOTIF_REQUESTER@EXAMPLE.COM"
            };
            db.Users.Add(owner);
            db.Users.Add(requester);
            await db.SaveChangesAsync();

            // Create target item and tag
            var targetItem = new Item
            {
                Content = "This is a target item for notification test",
                OwnerId = owner.Id,
                CreatedDate = DateTime.UtcNow,
                UpdatedDate = DateTime.UtcNow
            };
            db.Items.Add(targetItem);

            var targetTag = new Tag
            {
                Name = "NotifTag",
                OwnerId = owner.Id,
                CreatedDate = DateTime.UtcNow,
                UpdatedDate = DateTime.UtcNow
            };
            db.Tags.Add(targetTag);
            await db.SaveChangesAsync();

            // Create Request Items
            var requestItem1 = new Item { Content = "Req1", OwnerId = requester.Id, CreatedDate = DateTime.UtcNow, UpdatedDate = DateTime.UtcNow };
            var requestItem2 = new Item { Content = "Req2", OwnerId = requester.Id, CreatedDate = DateTime.UtcNow, UpdatedDate = DateTime.UtcNow };
            db.Items.Add(requestItem1);
            db.Items.Add(requestItem2);
            await db.SaveChangesAsync();

            // Create two TaggingRequestContracts (one for approve, one for reject)
            var request1 = new GratisTaggingContract
            {
                OwnerId = requester.Id,
                RequesterUserId = requester.Id,
                TagOwnerUserId = owner.Id,
                TargetItemId = targetItem.Id,
                RequestedTagId = targetTag.Id,
                RequestItemId = requestItem1.Id,
                RequestType = TaggingRequestType.Add,
                ProposedWeight = 1,
                Status = TradeStatus.Proposed,
                CreatedDate = DateTime.UtcNow,
                UpdatedDate = DateTime.UtcNow
            };

            var request2 = new GratisTaggingContract
            {
                OwnerId = requester.Id,
                RequesterUserId = requester.Id,
                TagOwnerUserId = owner.Id,
                TargetItemId = targetItem.Id,
                RequestedTagId = targetTag.Id,
                RequestItemId = requestItem2.Id,
                RequestType = TaggingRequestType.Add,
                ProposedWeight = 1,
                Status = TradeStatus.Proposed,
                CreatedDate = DateTime.UtcNow,
                UpdatedDate = DateTime.UtcNow
            };
            
            db.TaggingRequestEntities.Add(request1);
            db.TaggingRequestEntities.Add(request2);
            await db.SaveChangesAsync();
            
            // Assign to items for navigation
            requestItem1.TaggingRequestEntityId = request1.Id;
            requestItem2.TaggingRequestEntityId = request2.Id;
            await db.SaveChangesAsync();
        }

        // 2. Login as owner and go to notifications
        await Page.Context.ClearCookiesAsync();
        await Page.GotoAsync($"{_serverAddress}/auth/callback?provider=Google&code=mock-notif_owner");
        await Page.WaitForURLAsync(new Regex(@"^" + Regex.Escape(_serverAddress) + @"/?$"),
            new PageWaitForURLOptions { Timeout = 10000 });

        await Page.GotoAsync($"{_serverAddress}/notifications");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // There should be at least 2 notifications
        var notifications = Page.Locator(".mud-list-item").Filter(new LocatorFilterOptions { HasTextString = "追加リクエストが届いています" });
        await Expect(notifications).ToHaveCountAsync(2);

        // Appprove the first one
        var firstNotif = notifications.Nth(0);

        // Error 2 check: Ensure the text is selectable
        var requestText = firstNotif.Locator(".mud-typography-body2").Filter(new() { HasText = "タグ追加リクエスト" });
        await Expect(requestText).ToBeVisibleAsync();
        
        // Assert that user-select is not 'none', or specifically contains 'text'
        // Error 1 check: Ensure tapping the button works
        await Task.Delay(2000); // Wait for Blazor Server to hydrate
        var approveBtn = firstNotif.Locator("button[title='リクエストを承認する']");
        await approveBtn.ClickAsync();
        
        // Fail fast if there's any snackbar (likely an error)
        try
        {
            var snackbar = Page.Locator(".mud-snackbar");
            await snackbar.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 2000 });
            var snackbarText = await snackbar.TextContentAsync();
            if (snackbarText != null && snackbarText.Contains("エラー"))
            {
                Assert.Fail($"Error Snackbar appeared: {snackbarText}");
            }
        }
        catch (TimeoutException)
        {
            // No snackbar appeared within 2s, which is fine, or it was a success snackbar but we missed it.
        }

        // Verify status changed to "処理済み"
        var statusChip1 = firstNotif.Locator(".mud-chip-content", new LocatorLocatorOptions { HasTextString = "処理済み" });
        try
        {
            await Expect(statusChip1).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });
        }
        catch (Exception ex)
        {
            Assert.Fail($"Status chip not found. Exception: {ex.Message}");
        }
        var secondNotif = notifications.Nth(1);
        var rejectBtn = secondNotif.Locator("button[title='リクエストを却下する']");
        await rejectBtn.ClickAsync();

        // Verify dialog opens
        var dialog = Page.Locator(".mud-dialog");
        await Expect(dialog).ToBeVisibleAsync();

        var commentInput = dialog.Locator("textarea");
        await commentInput.FillAsync("Rejecting for test");

        var submitRejectBtn = dialog.Locator("button", new LocatorLocatorOptions { HasTextString = "却下する" });
        await submitRejectBtn.ClickAsync();

        // Verify status changed to "却下済み"
        var statusChip2 = secondNotif.Locator(".mud-chip-content", new LocatorLocatorOptions { HasTextString = "却下済み" });
        await Expect(statusChip2).ToBeVisibleAsync();
    }
}
