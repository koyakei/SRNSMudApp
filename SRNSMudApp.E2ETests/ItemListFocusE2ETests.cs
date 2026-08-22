#region

using System.Text.RegularExpressions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

using SRNSMudApp.Data;

#endregion

namespace SRNSMudApp.E2ETests;

/// <summary>
///     スクロールによる自動フォーカス（IntersectionObserver ベース）の検証のみを担う。
///     クリックフォーカスとスタイル適用、URL直接遷移によるフォーカス復元は
///     SRNSMudApp.Tests/Components/Item/ItemListFocusTests へ移行済み。
///     タグフィルタとの併用（tags= と focusItem= の共存）も同ファイルへ移行済み。
///     作者リンククリックの挙動も同ファイルへ移行済み。
/// </summary>
[TestFixture]
public class ItemListFocusE2ETests : PageTest
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
    private string _userId = "";

    [Test]
    public async Task ScrollingToItem_AutoFocusesIt_AndUpdatesUrl()
    {
        // データベースにモックユーザーとアイテムを複数登録
        using (IServiceScope scope = _factory.AppServices.CreateScope())
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            ApplicationUser? testUser = db.Users.FirstOrDefault(u => u.UserName == "testuser");
            if (testUser == null)
            {
                testUser = new ApplicationUser
                {
                    Id = Guid.NewGuid().ToString(),
                    UserName = "testuser",
                    NormalizedUserName = "TESTUSER",
                    Email = "test@example.com",
                    NormalizedEmail = "TEST@EXAMPLE.COM"
                };
                _ = db.Users.Add(testUser);
                _ = await db.SaveChangesAsync();
            }

            _userId = testUser.Id;

            // 既存のアイテムをクリーンアップ（共有DBのため本テストユーザーのものだけを削除。
            //   全件削除すると他テストが作った契約から参照されているアイテムでFK違反になる）
            System.Linq.IQueryable<Item> staleItems = db.Items.Where(i => i.OwnerId == _userId);
            db.Items.RemoveRange(staleItems);
            _ = await db.SaveChangesAsync();

            // アイテムを10件追加 (スクロールが必要なように少し長めのコンテンツ)
            for (var i = 1; i <= 10; i++)
            {
                _ = db.Items.Add(new Item
                {
                    Content =
                        $"Test Item {i}\nThis is a long text to ensure the card has some height.\nLine 3\nLine 4\nLine 5",
                    OwnerId = _userId
                });
                _ = await db.SaveChangesAsync();
                await Task.Delay(10);
            }
        }

        using (IServiceScope scope = _factory.AppServices.CreateScope())
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // 1. ページへ遷移
            _ = await Page.GotoAsync($"{_serverAddress}/Item/ItemList");

            // サーバー側でSignalRが接続され、データがロードされるのを待機
            await Task.Delay(2000);

            // --- スクロールによる自動フォーカスと URL 更新 ---
            // 画面の中央に到達できる中間のアイテムをスクロール先にする
            Item scrollTargetItem = db.Items.OrderByDescending(i => i.UpdatedDate).Skip(5).First();

            // 初期状態では URL に focusItem がないこと
            Assert.That(Page.Url, Does.Not.Contain($"focusItem={scrollTargetItem.Id}"));

            _ = await Page.EvaluateAsync(
                $"document.getElementById('item-card-{scrollTargetItem.Id}').scrollIntoView({{ block: 'center' }});");

            // IntersectionObserverが反応して状態が更新されるのを待つ
            await Task.Delay(1500);

            // URLが自動的に更新されていることを確認
            await Expect(Page).ToHaveURLAsync(new Regex($"focusItem={scrollTargetItem.Id}"));
        }
    }
}
