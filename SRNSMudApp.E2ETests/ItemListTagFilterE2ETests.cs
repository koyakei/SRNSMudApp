using System.Text.RegularExpressions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

using SRNSMudApp.Data;

namespace SRNSMudApp.E2ETests;

[TestFixture]
public class ItemListTagFilterE2ETests : PageTest
{
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _factory = SharedTestServerFixture.Factory;
        _serverAddress = SharedTestServerFixture.ServerAddress;
    }

    private CustomWebApplicationFactory _factory = null!;
    private string _serverAddress = "";

    [Test]
    public async Task GivenTagIdFilterWithEmail_WhenNavigatingToItemList_ThenOnlyDescendantTagsAndItemsAreDisplayed()
    {
        string email = $"tagfilter-{Guid.NewGuid():N}@example.com";
        string userName = email.Split('@')[0];
        int parentTagAId;
        int childTagA1Id;
        int otherTagBId;
        int itemAId;
        int itemBId;
        string rootTagName = Tag.RootTagName;

        using (IServiceScope scope = _factory.AppServices.CreateScope())
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var user = new ApplicationUser
            {
                Id = Guid.NewGuid().ToString(),
                UserName = userName,
                NormalizedUserName = userName.ToUpperInvariant(),
                Email = email,
                NormalizedEmail = email.ToUpperInvariant()
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();

            Tag rootTag = await db.Tags.FirstAsync(t => t.Name == rootTagName);

            HierarchyId? lastChildOfRoot = await db.Tags
                .Where(t => t.ParentTagId == rootTag.Id || (t.Node != null && t.Node.GetAncestor(1) == rootTag.Node))
                .OrderByDescending(t => t.Node)
                .Select(t => (HierarchyId?)t.Node)
                .FirstOrDefaultAsync();

            var parentTagA = new Tag
            {
                Name = $"ParentTagA_{Guid.NewGuid():N}",
                OwnerId = user.Id,
                ParentTagId = rootTag.Id,
                Node = rootTag.Node.GetDescendant(lastChildOfRoot, null)
            };
            db.Tags.Add(parentTagA);
            await db.SaveChangesAsync();

            var childTagA1 = new Tag
            {
                Name = $"ChildTagA1_{Guid.NewGuid():N}",
                OwnerId = user.Id,
                ParentTagId = parentTagA.Id,
                Node = parentTagA.Node.GetDescendant(null, null)
            };
            db.Tags.Add(childTagA1);

            var otherParentB = new Tag
            {
                Name = $"OtherParentB_{Guid.NewGuid():N}",
                OwnerId = user.Id,
                ParentTagId = rootTag.Id,
                Node = rootTag.Node.GetDescendant(parentTagA.Node, null)
            };
            db.Tags.Add(otherParentB);
            await db.SaveChangesAsync();

            parentTagAId = parentTagA.Id;
            childTagA1Id = childTagA1.Id;
            otherTagBId = otherParentB.Id;

            var itemA = new Item
            {
                Content = $"ItemA_Content_{Guid.NewGuid():N}",
                OwnerId = user.Id
            };
            var itemB = new Item
            {
                Content = $"ItemB_Content_{Guid.NewGuid():N}",
                OwnerId = user.Id
            };
            db.Items.AddRange(itemA, itemB);
            await db.SaveChangesAsync();

            itemAId = itemA.Id;
            itemBId = itemB.Id;

            db.TagRelations.AddRange(
                new TagRelation { ItemId = itemA.Id, TagId = childTagA1.Id, OwnerId = user.Id, Weight = 1 },
                new TagRelation { ItemId = itemB.Id, TagId = otherParentB.Id, OwnerId = user.Id, Weight = 1 }
            );
            await db.SaveChangesAsync();
        }

        // URL に f={parentTagAId}@{email} を指定してページに遷移
        string targetUrl = $"{_serverAddress}/Item/ItemList?f={parentTagAId}%40{Uri.EscapeDataString(email)}";
        await Page.GotoAsync(targetUrl);

        // Blazor InteractiveServer の初期化とデータロードを待機
        await Page.WaitForSelectorAsync($"#item-card-{itemAId}", new PageWaitForSelectorOptions { Timeout = 15000 });

        // 1. アイテムの検証
        ILocator itemCardA = Page.Locator($"#item-card-{itemAId}");
        await Expect(itemCardA).ToBeVisibleAsync();

        ILocator itemCardB = Page.Locator($"#item-card-{itemBId}");
        await Expect(itemCardB).ToHaveCountAsync(0);

        // 2. タグの検証（parentTagA とその子孫 childTagA1 のみ表示され、root tag や別ツリーのタグは表示されないこと）
        ILocator tagCardParentA = Page.Locator($"#tag-card-{parentTagAId}");
        await Expect(tagCardParentA).ToBeVisibleAsync();

        ILocator tagCardChildA1 = Page.Locator($"#tag-card-{childTagA1Id}");
        await Expect(tagCardChildA1).ToBeVisibleAsync();

        // ルートタグ（全て∀、Id=1）が表示されないこと
        ILocator rootTagCard = Page.Locator("#tag-card-1");
        await Expect(rootTagCard).ToHaveCountAsync(0);

        // 別の親タグBが表示されないこと
        ILocator tagCardOtherB = Page.Locator($"#tag-card-{otherTagBId}");
        await Expect(tagCardOtherB).ToHaveCountAsync(0);
    }
}