using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using SRNSMudApp.Data;

namespace SRNSMudApp.E2ETests;

[TestFixture]
public partial class TaggingRequestReplyE2ETests : PageTest
{
    private CustomWebApplicationFactory? _factory;
    private string? _serverAddress;

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
    public async Task CanViewTaggingRequestAndSubmitReply()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var requesterEmail = $"requester_{uniqueId}@example.com";
        var tagOwnerEmail = $"owner_{uniqueId}@example.com";
        var tagName = $"Request Tag {uniqueId}";
        var itemContent = $"Request Item {uniqueId}";

        // 1. ユーザー1(タグの所有者)の作成とタグの作成
        {
            await Page.Context.ClearCookiesAsync();
            var userName = tagOwnerEmail.Split('@')[0];
            await Page.GotoAsync($"{_serverAddress}/auth/callback?provider=Google&code=mock-{userName}");
            await Page.WaitForURLAsync(new Regex(@"^" + Regex.Escape(_serverAddress) + @"/?$"));
            await Expect(Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Logout" })).ToBeVisibleAsync();
        }

        using (var scope = _factory!.AppServices.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tagOwnerUser = await db.Users.FirstAsync(u => u.Email == tagOwnerEmail);
            
            var tag = new Tag { Name = tagName, Content = "Test content", OwnerId = tagOwnerUser.Id, CachedWeight = 0 };
            db.Tags.Add(tag);
            await db.SaveChangesAsync();
        }

        // 2. ユーザー2(リクエスター)の作成とアイテムの作成
        {
            await Page.Context.ClearCookiesAsync();
            var userName = requesterEmail.Split('@')[0];
            await Page.GotoAsync($"{_serverAddress}/auth/callback?provider=Google&code=mock-{userName}");
            await Page.WaitForURLAsync(new Regex(@"^" + Regex.Escape(_serverAddress) + @"/?$"));
        }

        _ = await Page.GotoAsync($"{_serverAddress}/Item/ItemList");
        await Task.Delay(2000);

        await Page.GetByPlaceholder("新しいアイテムのコンテンツを入力...").FillAsync(itemContent);
        await Task.Delay(1000);
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "保存" }).ClickAsync();
        await Expect(Page.GetByText("アイテムが正常に保存されました。")).ToBeVisibleAsync();

        // 3. DBからIDを取得してリクエストコントラクトを作成
        using (var scope = _factory.AppServices.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var requesterUser = await db.Users.FirstAsync(u => u.Email == requesterEmail);
            var tagOwnerUser = await db.Users.FirstAsync(u => u.Email == tagOwnerEmail);
            var item = await db.Items.FirstAsync(i => i.Content == itemContent);
            var tag = await db.Tags.FirstAsync(t => t.Name == tagName);

            var contract = new GratisTaggingContract
            {
                OwnerId = requesterUser.Id,
                RequesterUserId = requesterUser.Id,
                TagOwnerUserId = tagOwnerUser.Id,
                TargetItemId = item.Id,
                RequestedTagId = tag.Id,
                Status = TradeStatus.Proposed,
                RequesterMessage = "Please add this tag",
                RequestType = TaggingRequestType.Add
            };

            db.TaggingRequestEntities.Add(contract);
            await db.SaveChangesAsync();
        }

        // 4. ItemListをリロードしてリクエストのチップとリプライボタンを確認
        await Page.GotoAsync($"{_serverAddress}/Item/ItemList");
        await Task.Delay(2000);

        var itemCard = Page.Locator(".mud-card").Filter(new LocatorFilterOptions { HasText = itemContent });
        
        // タグ名が含まれているか
        await Expect(itemCard.Locator($".mud-chip:has-text('{tagName}')")).ToBeVisibleAsync();
        
        // リプライボタンをクリック
        await itemCard.Locator($".mud-chip:has-text('{tagName}')").Locator("button").ClickAsync();

        // 5. ダイアログが開き、リクエスト本文が見えるか確認
        var dialog = Page.Locator(".mud-dialog");
        await Expect(dialog).ToBeVisibleAsync();
        await Expect(dialog.Locator($"text={tagName} を追加するリクエストをしました。")).ToBeVisibleAsync();

        // 6. リプライを送信
        var replyText = "Sure, I agree! " + uniqueId;
        await dialog.GetByPlaceholder("返信を投稿...").FillAsync(replyText);
        await dialog.Locator(".mud-dialog-actions .mud-icon-button").ClickAsync();

        // 7. リプライが表示されているか確認
        await Expect(dialog.Locator($"text={replyText}")).ToBeVisibleAsync();

        // 8. ダイアログを閉じて、バッジに1と表示されるか確認
        await dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Close" }).ClickAsync();
        
        // 状態更新後も表示されるか、念のためリロードして確認する
        await Page.ReloadAsync();
        await Task.Delay(2000);
        itemCard = Page.Locator(".mud-card").Filter(new LocatorFilterOptions { HasText = itemContent });
        
        await Expect(itemCard.Locator(".mud-badge").First).ToHaveTextAsync("1");
    }
}
