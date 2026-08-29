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

    [Test]
    public async Task GivenTagNameFilterWithUserName_WhenNavigatingToItemList_ThenItemsAreFilteredByUserName()
    {
        string email1 = $"user1-{Guid.NewGuid():N}@example.com";
        string userName1 = email1.Split('@')[0];
        string email2 = $"user2-{Guid.NewGuid():N}@example.com";
        string userName2 = email2.Split('@')[0];
        string sharedTagName = $"SharedTag_{Guid.NewGuid():N}";
        int item1Id;
        int item2Id;

        using (IServiceScope scope = _factory.AppServices.CreateScope())
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var user1 = new ApplicationUser
            {
                Id = Guid.NewGuid().ToString(),
                UserName = userName1,
                NormalizedUserName = userName1.ToUpperInvariant(),
                Email = email1,
                NormalizedEmail = email1.ToUpperInvariant()
            };
            var user2 = new ApplicationUser
            {
                Id = Guid.NewGuid().ToString(),
                UserName = userName2,
                NormalizedUserName = userName2.ToUpperInvariant(),
                Email = email2,
                NormalizedEmail = email2.ToUpperInvariant()
            };
            db.Users.AddRange(user1, user2);
            await db.SaveChangesAsync();

            Tag rootTag = await db.Tags.FirstAsync(t => t.Name == Tag.RootTagName);

            HierarchyId? lastChild = await db.Tags
                .Where(t => t.ParentTagId == rootTag.Id || (t.Node != null && t.Node.GetAncestor(1) == rootTag.Node))
                .OrderByDescending(t => t.Node)
                .Select(t => (HierarchyId?)t.Node)
                .FirstOrDefaultAsync();

            var tag1 = new Tag
            {
                Name = sharedTagName,
                OwnerId = user1.Id,
                ParentTagId = rootTag.Id,
                Node = rootTag.Node.GetDescendant(lastChild, null)
            };
            db.Tags.Add(tag1);
            await db.SaveChangesAsync();

            var item1 = new Item { Content = $"ItemUser1_{Guid.NewGuid():N}", OwnerId = user1.Id };
            var item2 = new Item { Content = $"ItemUser2_{Guid.NewGuid():N}", OwnerId = user2.Id };
            db.Items.AddRange(item1, item2);
            await db.SaveChangesAsync();

            item1Id = item1.Id;
            item2Id = item2.Id;

            // user1 と user2 がそれぞれタグ付け
            db.TagRelations.AddRange(
                new TagRelation { ItemId = item1.Id, TagId = tag1.Id, OwnerId = user1.Id, Weight = 1 },
                new TagRelation { ItemId = item2.Id, TagId = tag1.Id, OwnerId = user2.Id, Weight = 1 }
            );
            await db.SaveChangesAsync();
        }

        // f=name:{sharedTagName}@{userName1} でナビゲート -> user1 のアイテムのみヒットし、user2 はヒットしないこと
        string targetUrl = $"{_serverAddress}/Item/ItemList?f=name%3A{Uri.EscapeDataString(sharedTagName)}%40{Uri.EscapeDataString(userName1)}";
        await Page.GotoAsync(targetUrl);

        await Page.WaitForSelectorAsync($"#item-card-{item1Id}", new PageWaitForSelectorOptions { Timeout = 15000 });

        ILocator itemCard1 = Page.Locator($"#item-card-{item1Id}");
        await Expect(itemCard1).ToBeVisibleAsync();

        ILocator itemCard2 = Page.Locator($"#item-card-{item2Id}");
        await Expect(itemCard2).ToHaveCountAsync(0);
    }

    [Test]
    public async Task GivenMultipleFilters_WhenNavigatingToItemList_ThenItemsMatchingAllFiltersAreDisplayed()
    {
        string email = $"multifilter-{Guid.NewGuid():N}@example.com";
        string userName = email.Split('@')[0];
        int tag1Id;
        int tag2Id;
        int itemBothId;
        int itemTag1OnlyId;
        int itemTag2OnlyId;

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

            Tag rootTag = await db.Tags.FirstAsync(t => t.Name == Tag.RootTagName);

            HierarchyId? lastChild = await db.Tags
                .Where(t => t.ParentTagId == rootTag.Id || (t.Node != null && t.Node.GetAncestor(1) == rootTag.Node))
                .OrderByDescending(t => t.Node)
                .Select(t => (HierarchyId?)t.Node)
                .FirstOrDefaultAsync();

            var tag1 = new Tag
            {
                Name = $"MultiTag1_{Guid.NewGuid():N}",
                OwnerId = user.Id,
                ParentTagId = rootTag.Id,
                Node = rootTag.Node.GetDescendant(lastChild, null)
            };
            db.Tags.Add(tag1);
            await db.SaveChangesAsync();

            var tag2 = new Tag
            {
                Name = $"MultiTag2_{Guid.NewGuid():N}",
                OwnerId = user.Id,
                ParentTagId = rootTag.Id,
                Node = rootTag.Node.GetDescendant(tag1.Node, null)
            };
            db.Tags.Add(tag2);
            await db.SaveChangesAsync();

            tag1Id = tag1.Id;
            tag2Id = tag2.Id;

            var itemBoth = new Item { Content = $"ItemBoth_{Guid.NewGuid():N}", OwnerId = user.Id };
            var itemTag1Only = new Item { Content = $"ItemTag1_{Guid.NewGuid():N}", OwnerId = user.Id };
            var itemTag2Only = new Item { Content = $"ItemTag2_{Guid.NewGuid():N}", OwnerId = user.Id };
            db.Items.AddRange(itemBoth, itemTag1Only, itemTag2Only);
            await db.SaveChangesAsync();

            itemBothId = itemBoth.Id;
            itemTag1OnlyId = itemTag1Only.Id;
            itemTag2OnlyId = itemTag2Only.Id;

            db.TagRelations.AddRange(
                new TagRelation { ItemId = itemBoth.Id, TagId = tag1.Id, OwnerId = user.Id, Weight = 1 },
                new TagRelation { ItemId = itemBoth.Id, TagId = tag2.Id, OwnerId = user.Id, Weight = 1 },
                new TagRelation { ItemId = itemTag1Only.Id, TagId = tag1.Id, OwnerId = user.Id, Weight = 1 },
                new TagRelation { ItemId = itemTag2Only.Id, TagId = tag2.Id, OwnerId = user.Id, Weight = 1 }
            );
            await db.SaveChangesAsync();
        }

        // f={tag1Id}&f={tag2Id} でナビゲート -> itemBoth のみ表示され、itemTag1Only, itemTag2Only は表示されないこと
        string targetUrl = $"{_serverAddress}/Item/ItemList?f={tag1Id}&f={tag2Id}";
        await Page.GotoAsync(targetUrl);

        await Page.WaitForSelectorAsync($"#item-card-{itemBothId}", new PageWaitForSelectorOptions { Timeout = 15000 });

        ILocator itemCardBoth = Page.Locator($"#item-card-{itemBothId}");
        await Expect(itemCardBoth).ToBeVisibleAsync();

        ILocator itemCardTag1Only = Page.Locator($"#item-card-{itemTag1OnlyId}");
        await Expect(itemCardTag1Only).ToHaveCountAsync(0);

        ILocator itemCardTag2Only = Page.Locator($"#item-card-{itemTag2OnlyId}");
        await Expect(itemCardTag2Only).ToHaveCountAsync(0);
    }

    [Test]
    public async Task GivenDeepHierarchyTagFilter_WhenNavigatingToItemList_ThenDeepestItemsAreDisplayed()
    {
        string email = $"deeptree-{Guid.NewGuid():N}@example.com";
        string userName = email.Split('@')[0];
        int level1TagId;
        int level3TagId;
        int deepestItemId;

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

            Tag rootTag = await db.Tags.FirstAsync(t => t.Name == Tag.RootTagName);

            HierarchyId? lastChild = await db.Tags
                .Where(t => t.ParentTagId == rootTag.Id || (t.Node != null && t.Node.GetAncestor(1) == rootTag.Node))
                .OrderByDescending(t => t.Node)
                .Select(t => (HierarchyId?)t.Node)
                .FirstOrDefaultAsync();

            // Level 1
            var level1Tag = new Tag
            {
                Name = $"L1_{Guid.NewGuid():N}",
                OwnerId = user.Id,
                ParentTagId = rootTag.Id,
                Node = rootTag.Node.GetDescendant(lastChild, null)
            };
            db.Tags.Add(level1Tag);
            await db.SaveChangesAsync();

            // Level 2
            var level2Tag = new Tag
            {
                Name = $"L2_{Guid.NewGuid():N}",
                OwnerId = user.Id,
                ParentTagId = level1Tag.Id,
                Node = level1Tag.Node.GetDescendant(null, null)
            };
            db.Tags.Add(level2Tag);
            await db.SaveChangesAsync();

            // Level 3 (最深階層)
            var level3Tag = new Tag
            {
                Name = $"L3_{Guid.NewGuid():N}",
                OwnerId = user.Id,
                ParentTagId = level2Tag.Id,
                Node = level2Tag.Node.GetDescendant(null, null)
            };
            db.Tags.Add(level3Tag);
            await db.SaveChangesAsync();

            level1TagId = level1Tag.Id;
            level3TagId = level3Tag.Id;

            var deepestItem = new Item
            {
                Content = $"DeepestItem_{Guid.NewGuid():N}",
                OwnerId = user.Id
            };
            db.Items.Add(deepestItem);
            await db.SaveChangesAsync();

            deepestItemId = deepestItem.Id;

            db.TagRelations.Add(new TagRelation
            {
                ItemId = deepestItem.Id,
                TagId = level3Tag.Id,
                OwnerId = user.Id,
                Weight = 1
            });
            await db.SaveChangesAsync();
        }

        // Level 1 のタグ ID でフィルタ -> Level 3 に紐づくアイテムが検索結果に含まれること
        string targetUrl = $"{_serverAddress}/Item/ItemList?f={level1TagId}";
        await Page.GotoAsync(targetUrl);

        await Page.WaitForSelectorAsync($"#item-card-{deepestItemId}", new PageWaitForSelectorOptions { Timeout = 15000 });

        ILocator itemCard = Page.Locator($"#item-card-{deepestItemId}");
        await Expect(itemCard).ToBeVisibleAsync();

        // タグカードにも Level 3 が表示されること
        ILocator tagCardLevel3 = Page.Locator($"#tag-card-{level3TagId}");
        await Expect(tagCardLevel3).ToBeVisibleAsync();
    }
}