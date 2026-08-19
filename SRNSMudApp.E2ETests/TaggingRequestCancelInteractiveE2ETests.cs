using System.Text.RegularExpressions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

using SRNSMudApp.Data;

namespace SRNSMudApp.E2ETests;

[TestFixture]
public class TaggingRequestCancelInteractiveE2ETests : PageTest
{
    private CustomWebApplicationFactory? _factory;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _factory = new CustomWebApplicationFactory();
        _factory.EnsureServer();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown() => _factory?.Dispose();

    [Test]
    public async Task CancelTaggingRequest_ShouldUpdateIconInteractively()
    {
        // 1. ログイン
        await Page.GotoAsync($"{_factory!.ServerAddress}/auth/callback?provider=Google&code=mock-user1");
        await Page.WaitForURLAsync(new Regex(@"^" + Regex.Escape(_factory.ServerAddress) + @"/?$"));

        // 2. テスト用データの準備 (タグとアイテム)
        string currentUserId;
        int reqItemId;
        using (IServiceScope scope = _factory.AppServices.CreateScope())
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await db.Users.FirstAsync(u => u.Email == "user1@example.com");
            currentUserId = user.Id;

            var tag = new Tag { Name = "E2E_TestTag_Cancel", OwnerId = currentUserId };
            db.Tags.Add(tag);
            var item = new Item { Content = "E2E_TargetItem_Cancel", OwnerId = currentUserId };
            db.Items.Add(item);
            await db.SaveChangesAsync();

            // ユーザーが自らリクエストを作成
            var contract = new TaggingRequestEntity
        {
            ContractType = "Gratis",
                TargetItemId = item.Id,
                RequestedTagId = tag.Id,
                RequesterUserId = currentUserId,
                OwnerId = currentUserId,
                TagOwnerUserId = currentUserId,
                RequestType = TaggingRequestType.Add,
                Status = TradeStatus.Proposed
            };
            db.TaggingRequestEntities.Add(contract);
            
            // リクエストアイテムも作成
            var reqItem = new Item
            {
                Content = "This is a request to cancel",
                OwnerId = currentUserId,
                AsRequestOf = contract
            };
            db.Items.Add(reqItem);
            
            await db.SaveChangesAsync();
            reqItemId = reqItem.Id;
        }

        // 3. 自分のアイテム一覧（またはフィード）にアクセスしてリクエストアイテムを表示する
        await Page.GotoAsync($"{_factory.ServerAddress}/ItemDetail/{reqItemId}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        
        // リクエストリストまたはアイテムカードを探す
        ILocator requestAlert = Page.Locator(".mud-alert").Filter(new LocatorFilterOptions { HasTextString = "タグ追加リクエスト" }).First;
        await Assertions.Expect(requestAlert).ToBeVisibleAsync();

        // キャンセルボタンを探す
        ILocator cancelButton = requestAlert.Locator("button[title='リクエストを取り下げる']");
        await Assertions.Expect(cancelButton).ToBeVisibleAsync();

        // 4. キャンセルボタンをクリック
        await cancelButton.ClickAsync();

        // 5. アイコンがインタラクティブに変わったか（取り下げ済みアイコン：canceled-icon が表示されているか）確認する
        ILocator canceledIcon = requestAlert.Locator(".canceled-icon");
        await Assertions.Expect(canceledIcon).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });
        
        // キャンセルボタンが消えていることを確認
        await Assertions.Expect(cancelButton).ToBeHiddenAsync();
    }
}
