using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

using NUnit.Framework;

using SRNSMudApp.Data;

namespace SRNSMudApp.E2ETests;

/// <summary>
///     ItemCard 上のリアクションボタン（真実・善・美）のトグル動作および
///     選択時の色反転（filled/active状態）を検証する E2E テスト。
/// </summary>
[TestFixture]
public class ItemReactionE2ETests : PageTest
{
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
        await Expect(shinjiUpvoteBtn).Not.ToHaveClassAsync(new Regex("mud-info-text"));
        await Expect(shinjiDownvoteBtn).Not.ToHaveClassAsync(new Regex("mud-error-text"));

        // 4. 「真実」上矢印ボタンをクリックしてアップボート付与 (+1)
        await shinjiUpvoteBtn.ClickAsync();

        // 付与後: 上矢印が Info カラーになり、チップのスコアが (1) に変化
        await Expect(shinjiChip).ToContainTextAsync("真実 (1)");
        await Expect(shinjiUpvoteBtn).ToHaveClassAsync(new Regex("mud-info-text"));
        await Expect(shinjiDownvoteBtn).Not.ToHaveClassAsync(new Regex("mud-error-text"));

        // 5. 再度「真実」上矢印ボタンをクリックして Weight を加算 (+2)
        await shinjiUpvoteBtn.ClickAsync();

        // 加算後: 上矢印が Info カラーのままで、チップのスコアが (2) に変化
        await Expect(shinjiChip).ToContainTextAsync("真実 (2)");
        await Expect(shinjiUpvoteBtn).ToHaveClassAsync(new Regex("mud-info-text"));
        await Expect(shinjiDownvoteBtn).Not.ToHaveClassAsync(new Regex("mud-error-text"));

        // 6. 「真実」下矢印ボタンをクリックして逆操作（減算: +1）
        await shinjiDownvoteBtn.ClickAsync();

        // 減算後: チップのスコアが (1) に変化
        await Expect(shinjiChip).ToContainTextAsync("真実 (1)");
        await Expect(shinjiUpvoteBtn).ToHaveClassAsync(new Regex("mud-info-text"));
        await Expect(shinjiDownvoteBtn).Not.ToHaveClassAsync(new Regex("mud-error-text"));

        // 7. 再度下矢印ボタンをクリックして逆操作で 0 に達し投票解除
        await shinjiDownvoteBtn.ClickAsync();

        // 解除後: 上下矢印ともにアクティブカラーが消え、スコア表記が消える
        await Expect(shinjiChip).Not.ToContainTextAsync("真実 (");
        await Expect(shinjiUpvoteBtn).Not.ToHaveClassAsync(new Regex("mud-info-text"));
        await Expect(shinjiDownvoteBtn).Not.ToHaveClassAsync(new Regex("mud-error-text"));

        // 8. 「真実」下矢印ボタンをクリックしてダウンボート付与 (-1)
        await shinjiDownvoteBtn.ClickAsync();

        // 付与後: 下矢印が Error カラーになり、チップのスコアが (-1) に変化
        await Expect(shinjiChip).ToContainTextAsync("真実 (-1)");
        await Expect(shinjiUpvoteBtn).Not.ToHaveClassAsync(new Regex("mud-info-text"));
        await Expect(shinjiDownvoteBtn).ToHaveClassAsync(new Regex("mud-error-text"));

        // 9. 「真実」上矢印ボタンをクリックして逆操作で 0 に戻す
        await shinjiUpvoteBtn.ClickAsync();

        // 解除後: 上下矢印ともにアクティブカラーが消え、スコア表記が消える
        await Expect(shinjiChip).Not.ToContainTextAsync("真実 (");
        await Expect(shinjiUpvoteBtn).Not.ToHaveClassAsync(new Regex("mud-info-text"));
        await Expect(shinjiDownvoteBtn).Not.ToHaveClassAsync(new Regex("mud-error-text"));
    }
}