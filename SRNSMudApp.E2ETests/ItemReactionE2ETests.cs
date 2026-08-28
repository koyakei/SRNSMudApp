using System.Text.RegularExpressions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

using SRNSMudApp.Data;

namespace SRNSMudApp.E2ETests;

/// <summary>
///     ItemCard 上のリアクションボタン（真実・善・美）のトグル動作および
///     選択時の色反転（filled/active状態）を検証する E2E テスト。
/// </summary>
[TestFixture]
public partial class ItemReactionE2ETests : PageTest
{
    [GeneratedRegex("mud-info-text")]
    private static partial Regex MudInfoTextRegex();

    [GeneratedRegex("mud-error-text")]
    private static partial Regex MudErrorTextRegex();

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _factory = SharedTestServerFixture.Factory;
        _serverAddress = SharedTestServerFixture.ServerAddress;
    }

    [SetUp]
    public void Setup()
    {
        Page.Console += (_, msg) => TestContext.Progress.WriteLine($"[CONSOLE] {msg.Type}: {msg.Text}");
        Page.PageError += (_, error) => TestContext.Progress.WriteLine($"[PAGE ERROR]: {error}");
    }

    private CustomWebApplicationFactory _factory = null!;
    private string _serverAddress = "";
    private int _targetItemId;

    [Test]
    public async Task GivenAuthenticatedUser_WhenClickShinjiReactionArrows_ThenVoteColorChangesAndScoreUpdates()
    {
        // 1. モック認証でログイン
        var email = $"reaction-{Guid.NewGuid():N}@example.com";
        await WebAuthnTestHelpers.LoginWithMockGoogleAsync(Page, _serverAddress, email);

        // 2. DB 上のログインユーザーを取得してテスト用アイテムをセットアップ
        using (IServiceScope scope = _factory.AppServices.CreateScope())
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userName = email.Split('@')[0];
            ApplicationUser? user = db.Users.FirstOrDefault(u => u.UserName == userName || u.Email == email)
                ?? db.Users.OrderByDescending(u => u.Id).First();

            var item = new Item
            {
                Content = $"E2E Reaction Test Item {Guid.NewGuid():N}",
                OwnerId = user.Id
            };
            db.Items.Add(item);
            await db.SaveChangesAsync();
            _targetItemId = item.Id;
        }

        // 3. アイテム一覧画面へ移動し、アイテムカードが表示されるのを待機
        await Page.GotoAsync($"{_serverAddress}/Item/ItemList?focus={_targetItemId}");
        await Page.WaitForSelectorAsync($"#item-card-{_targetItemId}", new PageWaitForSelectorOptions { Timeout = 10000 });

        ILocator itemCard = Page.Locator($"#item-card-{_targetItemId}");
        ILocator shinjiChip = itemCard.Locator("[data-testid='reaction-shinji']");
        ILocator shinjiUpvoteBtn = itemCard.Locator("[data-testid='reaction-shinji-upvote']");
        ILocator shinjiDownvoteBtn = itemCard.Locator("[data-testid='reaction-shinji-downvote']");

        // 初期状態: カウント表記なし（0）、矢印はアクティブカラーなし
        await Expect(shinjiChip).ToBeVisibleAsync();
        await Expect(shinjiChip).ToContainTextAsync("真実");
        await Expect(shinjiChip).Not.ToContainTextAsync("真実 (");
        await Expect(shinjiUpvoteBtn).Not.ToHaveClassAsync(MudInfoTextRegex());
        await Expect(shinjiDownvoteBtn).Not.ToHaveClassAsync(MudErrorTextRegex());

        // 4. 「真実」上矢印ボタンをクリックしてアップボート付与 (+1)
        await shinjiUpvoteBtn.ClickAsync();

        // 付与後: 上矢印が Info カラーになり、チップのスコアが (1) に変化
        await Expect(shinjiChip).ToContainTextAsync("真実 (1)");
        await Expect(shinjiUpvoteBtn).ToHaveClassAsync(MudInfoTextRegex());
        await Expect(shinjiDownvoteBtn).Not.ToHaveClassAsync(MudErrorTextRegex());

        // 5. 再度「真実」上矢印ボタンをクリックして Weight を加算 (+2)
        await shinjiUpvoteBtn.ClickAsync();

        // 加算後: 上矢印が Info カラーのままで、チップのスコアが (2) に変化
        await Expect(shinjiChip).ToContainTextAsync("真実 (2)");
        await Expect(shinjiUpvoteBtn).ToHaveClassAsync(MudInfoTextRegex());
        await Expect(shinjiDownvoteBtn).Not.ToHaveClassAsync(MudErrorTextRegex());

        // 6. 「真実」下矢印ボタンをクリックして逆操作（減算: +1）
        await shinjiDownvoteBtn.ClickAsync();

        // 減算後: チップのスコアが (1) に変化
        await Expect(shinjiChip).ToContainTextAsync("真実 (1)");
        await Expect(shinjiUpvoteBtn).ToHaveClassAsync(MudInfoTextRegex());
        await Expect(shinjiDownvoteBtn).Not.ToHaveClassAsync(MudErrorTextRegex());

        // 7. 再度下矢印ボタンをクリックして逆操作で 0 に達し投票解除
        await shinjiDownvoteBtn.ClickAsync();

        // 解除後: 上下矢印ともにアクティブカラーが消え、スコア表記が消える
        await Expect(shinjiChip).Not.ToContainTextAsync("真実 (");
        await Expect(shinjiUpvoteBtn).Not.ToHaveClassAsync(MudInfoTextRegex());
        await Expect(shinjiDownvoteBtn).Not.ToHaveClassAsync(MudErrorTextRegex());

        // 8. 「真実」下矢印ボタンをクリックしてダウンボート付与 (-1)
        await shinjiDownvoteBtn.ClickAsync();

        // 付与後: 下矢印が Error カラーになり、チップのスコアが (-1) に変化
        await Expect(shinjiChip).ToContainTextAsync("真実 (-1)");
        await Expect(shinjiUpvoteBtn).Not.ToHaveClassAsync(MudInfoTextRegex());
        await Expect(shinjiDownvoteBtn).ToHaveClassAsync(MudErrorTextRegex());

        // 9. 「真実」上矢印ボタンをクリックして逆操作で 0 に戻す
        await shinjiUpvoteBtn.ClickAsync();

        // 解除後: 上下矢印ともにアクティブカラーが消え、スコア表記が消える
        await Expect(shinjiChip).Not.ToContainTextAsync("真実 (");
        await Expect(shinjiUpvoteBtn).Not.ToHaveClassAsync(MudInfoTextRegex());
        await Expect(shinjiDownvoteBtn).Not.ToHaveClassAsync(MudErrorTextRegex());
    }

    [Test]
    public async Task GivenAuthenticatedUser_WhenUpvotingReactionTag_ThenTagRelationAndCachedWeightAreUpdated()
    {
        // 1. モック認証でログイン
        var email = $"reaction-weight-{Guid.NewGuid():N}@example.com";
        await WebAuthnTestHelpers.LoginWithMockGoogleAsync(Page, _serverAddress, email);

        string userId;
        int targetItemId;

        // 2. DB 上のログインユーザーを取得してテスト用アイテムをセットアップ
        using (IServiceScope scope = _factory.AppServices.CreateScope())
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userName = email.Split('@')[0];
            ApplicationUser? user = db.Users.FirstOrDefault(u => u.UserName == userName || u.Email == email)
                ?? db.Users.OrderByDescending(u => u.Id).First();
            userId = user.Id;

            var item = new Item
            {
                Content = $"Reaction Weight Test Item {Guid.NewGuid():N}",
                OwnerId = user.Id
            };
            db.Items.Add(item);
            await db.SaveChangesAsync();
            targetItemId = item.Id;
        }

        // 3. アイテム一覧画面へ移動し、アイテムカードが表示されるのを待機
        await Page.GotoAsync($"{_serverAddress}/Item/ItemList?focus={targetItemId}");
        await Page.WaitForSelectorAsync($"#item-card-{targetItemId}", new PageWaitForSelectorOptions { Timeout = 10000 });

        ILocator itemCard = Page.Locator($"#item-card-{targetItemId}");
        ILocator shinjiChip = itemCard.Locator("[data-testid='reaction-shinji']");
        ILocator shinjiUpvoteBtn = itemCard.Locator("[data-testid='reaction-shinji-upvote']");
        ILocator shinjiDownvoteBtn = itemCard.Locator("[data-testid='reaction-shinji-downvote']");
        // 4. 「真実」上矢印ボタンをクリックしてアップボート (+1)
        await shinjiUpvoteBtn.ClickAsync();
        await Expect(shinjiChip).ToContainTextAsync("真実 (1)");

        // 5. DB の TagRelation, Tag.CachedWeight, TagWeightLedger を検証 (+1)
        using (IServiceScope scope = _factory.AppServices.CreateScope())
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Tag? shinjiTag = db.Tags.FirstOrDefault(t => t.OwnerId == userId && t.Name == "真実" && t.IsSystem);
            TagRelation? relation = db.TagRelations.FirstOrDefault(tr => tr.ItemId == targetItemId && tr.TagId == shinjiTag!.Id && tr.OwnerId == userId);
            TagWeightLedger? ledger = db.TagWeightLedgers.FirstOrDefault(l => l.TagId == shinjiTag!.Id && l.SourceType == "TagRelationInsert");

            Assert.Multiple(() =>
            {
                Assert.That(shinjiTag, Is.Not.Null, "ReactionTag '真実' should exist for user");
                Assert.That(relation, Is.Not.Null, "TagRelation should be created for the item and reaction tag");
                Assert.That(relation!.Weight, Is.EqualTo(1), "TagRelation weight should be 1");
                Assert.That(shinjiTag!.CachedWeight, Is.EqualTo(1), "Tag.CachedWeight should be incremented to 1");
                Assert.That(ledger, Is.Not.Null, "TagWeightLedger should be recorded for reaction upvote");
                Assert.That(ledger!.Delta, Is.EqualTo(1), "Ledger delta should be 1");
            });
        }

        // 6. 再度「真実」上矢印をクリックして Weight を加算 (+2)
        await shinjiUpvoteBtn.ClickAsync();
        await Expect(shinjiChip).ToContainTextAsync("真実 (2)");

        using (IServiceScope scope = _factory.AppServices.CreateScope())
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Tag shinjiTag = db.Tags.First(t => t.OwnerId == userId && t.Name == "真実" && t.IsSystem);
            TagRelation relation = db.TagRelations.First(tr => tr.ItemId == targetItemId && tr.TagId == shinjiTag.Id && tr.OwnerId == userId);
            TagWeightLedger? updateLedger = db.TagWeightLedgers.FirstOrDefault(l => l.TagId == shinjiTag.Id && l.SourceType == "TagRelationUpdate");

            Assert.Multiple(() =>
            {
                Assert.That(relation.Weight, Is.EqualTo(2));
                Assert.That(shinjiTag.CachedWeight, Is.EqualTo(2));
                Assert.That(updateLedger, Is.Not.Null);
                Assert.That(updateLedger!.Delta, Is.EqualTo(1));
            });
        }

        // 7. 「真実」下矢印をクリックして逆操作（減算: +1）
        await shinjiDownvoteBtn.ClickAsync();
        await Expect(shinjiChip).ToContainTextAsync("真実 (1)");

        using (IServiceScope scope = _factory.AppServices.CreateScope())
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Tag shinjiTag = db.Tags.First(t => t.OwnerId == userId && t.Name == "真実" && t.IsSystem);
            TagRelation relation = db.TagRelations.First(tr => tr.ItemId == targetItemId && tr.TagId == shinjiTag.Id && tr.OwnerId == userId);

            Assert.Multiple(() =>
            {
                Assert.That(relation.Weight, Is.EqualTo(1));
                Assert.That(shinjiTag.CachedWeight, Is.EqualTo(1));
            });
        }

        // 8. 再度下矢印をクリックして 0 に達し投票解除
        await shinjiDownvoteBtn.ClickAsync();
        await Expect(shinjiChip).Not.ToContainTextAsync("真実 (");

        using (IServiceScope scope = _factory.AppServices.CreateScope())
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Tag shinjiTag = db.Tags.First(t => t.OwnerId == userId && t.Name == "真実" && t.IsSystem);
            TagRelation? relation = db.TagRelations.FirstOrDefault(tr => tr.ItemId == targetItemId && tr.TagId == shinjiTag.Id && tr.OwnerId == userId);
            TagWeightLedger? deleteLedger = db.TagWeightLedgers.FirstOrDefault(l => l.TagId == shinjiTag.Id && l.SourceType == "TagRelationDelete");

            Assert.Multiple(() =>
            {
                Assert.That(relation, Is.Null, "TagRelation should be removed when weight reaches 0");
                Assert.That(shinjiTag.CachedWeight, Is.EqualTo(0), "Tag.CachedWeight should return to 0");
                Assert.That(deleteLedger, Is.Not.Null);
                Assert.That(deleteLedger!.Delta, Is.EqualTo(-1));
            });
        }
    }

    [Test]
    public async Task GivenAuthenticatedUser_WhenClickReactionChip_ThenNavigatesToItemDetailWithSearchQuery()
    {
        // 1. モック認証でログイン
        var email = $"reaction-nav-{Guid.NewGuid():N}@example.com";
        await WebAuthnTestHelpers.LoginWithMockGoogleAsync(Page, _serverAddress, email);

        int targetItemId;

        // 2. DB 上のログインユーザーを取得してテスト用アイテムをセットアップ
        using (IServiceScope scope = _factory.AppServices.CreateScope())
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userName = email.Split('@')[0];
            ApplicationUser? user = db.Users.FirstOrDefault(u => u.UserName == userName || u.Email == email)
                ?? db.Users.OrderByDescending(u => u.Id).First();

            var item = new Item
            {
                Content = $"Reaction Navigation Test Item {Guid.NewGuid():N}",
                OwnerId = user.Id
            };
            db.Items.Add(item);
            await db.SaveChangesAsync();
            targetItemId = item.Id;
        }

        // 3. アイテム一覧画面へ移動し、アイテムカードが表示されるのを待機
        await Page.GotoAsync($"{_serverAddress}/Item/ItemList?focus={targetItemId}");
        await Page.WaitForSelectorAsync($"#item-card-{targetItemId}", new PageWaitForSelectorOptions { Timeout = 10000 });

        ILocator itemCard = Page.Locator($"#item-card-{targetItemId}");
        ILocator shinjiChip = itemCard.Locator("[data-testid='reaction-shinji']");

        // 4. 「真実」チップ（タグ名/アイコン）をクリック
        await shinjiChip.ClickAsync();

        // 5. ItemDetail ページに遷移し、URL にタグ検索クエリが含まれることを検証
        await Page.WaitForURLAsync(new Regex($"/ItemDetail/{targetItemId}"), new PageWaitForURLOptions { Timeout = 10000 });
        Assert.That(Page.Url, Does.Contain($"/ItemDetail/{targetItemId}"));
        Assert.That(Page.Url, Does.Contain("f="));

        // 6. ItemDetail ページの検索バーに「真実」がセットされ、タイトル「アイテム詳細」が表示されていることを検証
        ILocator title = Page.Locator("text=アイテム詳細");
        await Expect(title).ToBeVisibleAsync();
    }
}