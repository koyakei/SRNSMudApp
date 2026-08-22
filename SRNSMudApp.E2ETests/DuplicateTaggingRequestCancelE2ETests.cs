using SRNSMudApp.Models.Unions;
using System.Text.RegularExpressions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

using SRNSMudApp.Data;

namespace SRNSMudApp.E2ETests;

/// <summary>
///     シナリオ:
///     1. ユーザーA がタグ付けリクエストをする
///     2. ユーザーB が同じタグ・同じアイテムへのタグ付けリクエストをする
///     3. タグオーナー(ユーザーC) がユーザーAのリクエストを承認する → タグが付与される
///     4. ユーザーBのリクエストはまだ Proposed 状態で残っている
///     5. ユーザーBが自分のリクエストを確認し、ユーザーAのリクエストが既に実行されていることを知って取り下げる
/// </summary>
[TestFixture]
public class DuplicateTaggingRequestCancelE2ETests : PageTest
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
    public async Task WhenUserARequestIsApproved_UserBCanCancelTheirOwnRequest()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];

        var userAEmail = $"usera_{uniqueId}@example.com";
        var userBEmail = $"userb_{uniqueId}@example.com";
        var tagOwnerEmail = $"tagowner_{uniqueId}@example.com";
        var tagName = $"DupTag {uniqueId}";
        var itemContent = $"DupItem {uniqueId}";

        // ================================================================
        // Step 1: タグオーナー(ユーザーC)を作成してタグを作る
        // ================================================================
        await LoginWithMockAsync(tagOwnerEmail);

        string tagOwnerUserId;
        int tagId;
        using (IServiceScope scope = _factory!.AppServices.CreateScope())
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            ApplicationUser tagOwner = await db.Users.FirstAsync(u => u.Email == tagOwnerEmail);
            tagOwnerUserId = tagOwner.Id;

            var tag = new Tag
            {
                Name = tagName, Content = $"DupTag content {uniqueId}", OwnerId = tagOwnerUserId, CachedWeight = 0
            };
            db.Tags.Add(tag);
            await db.SaveChangesAsync();
            tagId = tag.Id;
        }

        // ================================================================
        // Step 2: ユーザーA を作成し、アイテムを作る
        // ================================================================
        await LoginWithMockAsync(userAEmail);

        await Page.GotoAsync($"{_serverAddress}/Item/ItemList");
        await Task.Delay(2000);
        await Page.GetByPlaceholder("新しいアイテムのコンテンツを入力...").FillAsync(itemContent);
        await Task.Delay(500);
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "保存" }).ClickAsync();
        await Expect(Page.GetByText("アイテムが正常に保存されました。"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });

        // ================================================================
        // Step 3: ユーザーB を登録し、DB でユーザーA・B のリクエストを作成する
        // ================================================================
        await LoginWithMockAsync(userBEmail);

        string userAId;
        string userBId;
        int itemId;
        int contractAId;
        int contractBId;

        using (IServiceScope scope = _factory.AppServices.CreateScope())
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            ApplicationUser userA = await db.Users.FirstAsync(u => u.Email == userAEmail);
            ApplicationUser userB = await db.Users.FirstAsync(u => u.Email == userBEmail);
            Item item = await db.Items.FirstAsync(i => i.Content == itemContent);

            userAId = userA.Id;
            userBId = userB.Id;
            itemId = item.Id;

            // ユーザーA のリクエスト
            var contractA = new TaggingRequestEntity
        {
            ContractType = "Gratis",
                OwnerId = userAId,
                RequesterUserId = userAId,
                TagOwnerUserId = tagOwnerUserId,
                TargetItemId = itemId,
                RequestedTagId = tagId,
                Status = TradeStatus.Proposed,
                Payload = new GratisPayload($"UserA request {uniqueId}"),
                RequestType = TaggingRequestType.Add
            };

            // ユーザーB のリクエスト（同じタグ・同じアイテム）
            var contractB = new TaggingRequestEntity
        {
            ContractType = "Gratis",
                OwnerId = userBId,
                RequesterUserId = userBId,
                TagOwnerUserId = tagOwnerUserId,
                TargetItemId = itemId,
                RequestedTagId = tagId,
                Status = TradeStatus.Proposed,
                Payload = new GratisPayload($"UserB request {uniqueId}"),
                RequestType = TaggingRequestType.Add
            };

            db.TaggingRequestEntities.Add(contractA);
            db.TaggingRequestEntities.Add(contractB);
            await db.SaveChangesAsync();

            contractAId = contractA.Id;
            contractBId = contractB.Id;
        }

        // ================================================================
        // Step 4: タグオーナー(ユーザーC) が ItemDetail からユーザーAのリクエストを承認する
        // ================================================================
        await LoginWithMockAsync(tagOwnerEmail);

        await Page.GotoAsync($"{_serverAddress}/ItemDetail/{itemId}?tab=requests");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Task.Delay(2000);

        // 「関連リクエスト」タブが自動で選択されていなければクリック
        ILocator requestsTab = Page.GetByRole(AriaRole.Tab,
            new PageGetByRoleOptions { Name = "関連リクエスト" });
        if (await requestsTab.IsVisibleAsync())
        {
            await requestsTab.ClickAsync();
            await Task.Delay(1000);
        }

        // ユーザーA のユーザー名を含む行の「承認」ボタンをクリック
        var userAName = userAEmail.Split('@')[0];
        ILocator userARow = Page.Locator("tr")
            .Filter(new LocatorFilterOptions { HasText = userAName })
            .First;
        await userARow
            .Locator("button", new LocatorLocatorOptions { HasText = "承認" })
            .ClickAsync();
        await Task.Delay(1000);

        // 承認成功のスナックバーを確認
        await Expect(Page.GetByText("リクエストを承認しました。"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });

        // ================================================================
        // Step 5: DB でユーザーAが Executed、ユーザーBがまだ Proposed であることを確認
        // ================================================================
        using (IServiceScope scope = _factory.AppServices.CreateScope())
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            TaggingRequestEntity? contractA = await db.TaggingRequestEntities.FindAsync(contractAId);
            TaggingRequestEntity? contractB = await db.TaggingRequestEntities.FindAsync(contractBId);

            Assert.That(contractA, Is.Not.Null, "ユーザーAのリクエストが存在しない");
            Assert.That(contractA!.Status, Is.EqualTo(TradeStatus.Executed),
                "ユーザーAのリクエストは Executed になっているべき");

            Assert.That(contractB, Is.Not.Null, "ユーザーBのリクエストが存在しない");
            Assert.That(contractB!.Status, Is.EqualTo(TradeStatus.Proposed),
                "ユーザーBのリクエストはまだ Proposed のまま残っているべき");
        }

        // ================================================================
        // Step 6: ユーザーB が ContractManagement で自分のリクエストを取り下げる
        // ================================================================
        await LoginWithMockAsync(userBEmail);

        await Page.GotoAsync($"{_serverAddress}/Contract/ContractManagement");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Task.Delay(2000);

        // 「送信済み (Outbox)」タブをクリック
        ILocator outboxTab = Page.GetByRole(AriaRole.Tab,
            new PageGetByRoleOptions { Name = "送信済み" });
        if (await outboxTab.IsVisibleAsync())
        {
            await outboxTab.ClickAsync();
            await Task.Delay(1000);
        }

        // ユーザーBのリクエストカードが表示されていることを確認
        ILocator contractBCard = Page.Locator(".mud-card")
            .Filter(new LocatorFilterOptions { HasText = tagName })
            .First;
        await Expect(contractBCard)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });

        // 「提案中」ステータスが表示されていることを確認
        await Expect(contractBCard.GetByText("提案中")).ToBeVisibleAsync();

        // 「取り下げる」ボタンをクリック
        await contractBCard
            .GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "取り下げる" })
            .ClickAsync();
        await Task.Delay(1000);

        // 取り下げ成功のスナックバーを確認
        await Expect(Page.GetByText("コントラクトを取り下げました。"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });

        // ================================================================
        // Step 7: DB でユーザーBのリクエストが Canceled になっていることを確認
        // ================================================================
        using (IServiceScope scope = _factory.AppServices.CreateScope())
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            TaggingRequestEntity? contractB = await db.TaggingRequestEntities.FindAsync(contractBId);

            Assert.That(contractB, Is.Not.Null, "ユーザーBのリクエストが存在しない");
            Assert.That(contractB!.Status, Is.EqualTo(TradeStatus.Canceled),
                "ユーザーBのリクエストは Canceled になっているべき");
        }
    }

    /// <summary>
    ///     モック認証でログインし、ホームページへのリダイレクトを待つ
    /// </summary>
    private async Task LoginWithMockAsync(string email)
    {
        await Page.Context.ClearCookiesAsync();
        var userName = email.Split('@')[0];
        await Page.GotoAsync($"{_serverAddress}/auth/callback?provider=Google&code=mock-{userName}");
        await Page.WaitForURLAsync(
            new Regex(@"^" + Regex.Escape(_serverAddress!) + @"/?$"),
            new PageWaitForURLOptions { Timeout = 10000 });
        await Expect(Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Logout" }))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });
    }
}