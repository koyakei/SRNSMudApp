#region

using Microsoft.EntityFrameworkCore;

using Moq;

using SRNSMudApp.Data;
using SRNSMudApp.Services;

#endregion

namespace SRNSMudApp.Tests.Services;

public class ItemListDataProviderTests : IAsyncLifetime
{
    private MsSqlTestDatabase _sharedDb = null!;

    public async Task InitializeAsync()
    {
        _sharedDb = await SharedMsSqlTestDatabase.GetInstanceAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(ApplicationDbContext db, ItemListDataProvider sut, string user1Id, string user1Name, string user2Id, string user2Name, string tid)> CreateScopeAsync()
    {
        var tid = Guid.NewGuid().ToString("N")[..8];
        var db = new ApplicationDbContext(_sharedDb.Options);
        var tagEmbeddingMock = new Mock<ITagEmbeddingService>();
        var sut = new ItemListDataProvider(new DbContextFactoryStub(_sharedDb.Options), tagEmbeddingMock.Object);

        var user1Id = $"user1_{tid}";
        var user1Name = user1Id;
        var user2Id = $"user2_{tid}";
        var user2Name = user2Id;
        var sysId = $"sys_{tid}";

        await db.SeedUsersAsync(user1Id, user2Id, sysId);

        return (db, sut, user1Id, user1Name, user2Id, user2Name, tid);
    }

    [Fact]
    public async Task LoadItemsAndTagsAsync_WithTagIdFilter_ReturnsItemsAndTagsWithDescendantTags()
    {
        var (db, sut, user1Id, _, _, _, tid) = await CreateScopeAsync();
        await using (db)
        {
            var rootTag = await db.Tags.FirstAsync(t => t.Name == Tag.RootTagName);
            var parentTagNode = rootTag.Node.GetDescendant(null, null);
            var parentTag = new Tag
            {
                Name = $"Parent_{tid}",
                OwnerId = user1Id,
                Node = parentTagNode
            };
            db.Tags.Add(parentTag);
            await db.SaveChangesAsync();

            var childTag = new Tag
            {
                Name = $"Child_{tid}",
                OwnerId = user1Id,
                ParentTagId = parentTag.Id,
                Node = parentTagNode.GetDescendant(null, null)
            };
            var otherTag = new Tag
            {
                Name = $"Other_{tid}",
                OwnerId = user1Id,
                Node = rootTag.Node.GetDescendant(parentTagNode, null)
            };
            db.Tags.AddRange(childTag, otherTag);
            await db.SaveChangesAsync();

            var itemWithParent = new Item { Content = $"ItemParent_{tid}", OwnerId = user1Id };
            var itemWithChild = new Item { Content = $"ItemChild_{tid}", OwnerId = user1Id };
            var itemWithOther = new Item { Content = $"ItemOther_{tid}", OwnerId = user1Id };
            db.Items.AddRange(itemWithParent, itemWithChild, itemWithOther);
            await db.SaveChangesAsync();

            db.TagRelations.AddRange(
                new TagRelation { ItemId = itemWithParent.Id, TagId = parentTag.Id, OwnerId = user1Id, Weight = 1 },
                new TagRelation { ItemId = itemWithChild.Id, TagId = childTag.Id, OwnerId = user1Id, Weight = 1 },
                new TagRelation { ItemId = itemWithOther.Id, TagId = otherTag.Id, OwnerId = user1Id, Weight = 1 }
            );

            // TagRelationToTag: otherTag has childTag as target tag relation
            db.TagRelationToTags.Add(
                new TagRelationToTag { TagId = childTag.Id, TargetTagId = otherTag.Id, OwnerId = user1Id, Weight = 1 }
            );
            await db.SaveChangesAsync();

            ItemListFilter[] filters = [new TagIdFilter(parentTag.Id, null)];
            ItemListPageData result = await sut.LoadItemsAndTagsAsync(filters, []);

            Assert.Contains(result.Items, i => i.Id == itemWithParent.Id);
            Assert.Contains(result.Items, i => i.Id == itemWithChild.Id);
            Assert.DoesNotContain(result.Items, i => i.Id == itemWithOther.Id);

            Assert.Contains(result.Tags, t => t.Id == parentTag.Id);
            Assert.Contains(result.Tags, t => t.Id == childTag.Id);
            Assert.Contains(result.Tags, t => t.Id == otherTag.Id); // otherTag has childTag in TargetTagRelations
        }
    }

    [Fact]
    public async Task LoadItemsAndTagsAsync_WithTagIdFilterAndUserName_IgnoresUserNameAndReturnsAllItems()
    {
        var (db, sut, user1Id, user1Name, user2Id, _, tid) = await CreateScopeAsync();
        await using (db)
        {
            var rootTag = await db.Tags.FirstAsync(t => t.Name == Tag.RootTagName);
            var parentTagNode = rootTag.Node.GetDescendant(null, null);
            var parentTag = new Tag
            {
                Name = $"Parent_{tid}",
                OwnerId = user1Id,
                Node = parentTagNode
            };
            db.Tags.Add(parentTag);
            await db.SaveChangesAsync();

            var childTag = new Tag
            {
                Name = $"Child_{tid}",
                OwnerId = user1Id,
                ParentTagId = parentTag.Id,
                Node = parentTagNode.GetDescendant(null, null)
            };
            db.Tags.Add(childTag);
            await db.SaveChangesAsync();

            var itemUser1 = new Item { Content = $"ItemU1_{tid}", OwnerId = user1Id };
            var itemUser2 = new Item { Content = $"ItemU2_{tid}", OwnerId = user2Id };
            db.Items.AddRange(itemUser1, itemUser2);
            await db.SaveChangesAsync();

            db.TagRelations.AddRange(
                new TagRelation { ItemId = itemUser1.Id, TagId = childTag.Id, OwnerId = user1Id, Weight = 1 },
                new TagRelation { ItemId = itemUser2.Id, TagId = childTag.Id, OwnerId = user2Id, Weight = 1 }
            );
            await db.SaveChangesAsync();

            // TagIdFilter で UserName を指定しても、ID 指定の場合は UserName が無視されて両方のアイテムが返る
            ItemListFilter[] filters = [new TagIdFilter(parentTag.Id, user1Name)];
            ItemListPageData result = await sut.LoadItemsAndTagsAsync(filters, []);

            Assert.Contains(result.Items, i => i.Id == itemUser1.Id);
            Assert.Contains(result.Items, i => i.Id == itemUser2.Id);
        }
    }

    [Fact]
    public async Task LoadItemsAndTagsAsync_WithTagNameFilter_ReturnsItemsAndTagsWithDescendantTags()
    {
        var (db, sut, user1Id, _, _, _, tid) = await CreateScopeAsync();
        await using (db)
        {
            var rootTag = await db.Tags.FirstAsync(t => t.Name == Tag.RootTagName);
            var parentTagNode = rootTag.Node.GetDescendant(null, null);
            var parentTagName = $"ParentName_{tid}";
            var parentTag = new Tag
            {
                Name = parentTagName,
                OwnerId = user1Id,
                Node = parentTagNode
            };
            db.Tags.Add(parentTag);
            await db.SaveChangesAsync();

            var childTag = new Tag
            {
                Name = $"ChildName_{tid}",
                OwnerId = user1Id,
                ParentTagId = parentTag.Id,
                Node = parentTagNode.GetDescendant(null, null)
            };
            var otherTag = new Tag
            {
                Name = $"OtherName_{tid}",
                OwnerId = user1Id,
                Node = rootTag.Node.GetDescendant(parentTagNode, null)
            };
            db.Tags.AddRange(childTag, otherTag);
            await db.SaveChangesAsync();

            var itemWithParent = new Item { Content = $"ItemP_{tid}", OwnerId = user1Id };
            var itemWithChild = new Item { Content = $"ItemC_{tid}", OwnerId = user1Id };
            var itemWithOther = new Item { Content = $"ItemO_{tid}", OwnerId = user1Id };
            db.Items.AddRange(itemWithParent, itemWithChild, itemWithOther);
            await db.SaveChangesAsync();

            db.TagRelations.AddRange(
                new TagRelation { ItemId = itemWithParent.Id, TagId = parentTag.Id, OwnerId = user1Id, Weight = 1 },
                new TagRelation { ItemId = itemWithChild.Id, TagId = childTag.Id, OwnerId = user1Id, Weight = 1 },
                new TagRelation { ItemId = itemWithOther.Id, TagId = otherTag.Id, OwnerId = user1Id, Weight = 1 }
            );
            await db.SaveChangesAsync();

            ItemListFilter[] filters = [new TagNameFilter(parentTagName, null)];
            ItemListPageData result = await sut.LoadItemsAndTagsAsync(filters, []);

            Assert.Contains(result.Items, i => i.Id == itemWithParent.Id);
            Assert.Contains(result.Items, i => i.Id == itemWithChild.Id);
            Assert.DoesNotContain(result.Items, i => i.Id == itemWithOther.Id);

            Assert.Contains(result.Tags, t => t.Id == parentTag.Id);
            Assert.Contains(result.Tags, t => t.Id == childTag.Id);
            Assert.DoesNotContain(result.Tags, t => t.Id == otherTag.Id);
        }
    }

    [Fact]
    public async Task LoadItemsAndTagsAsync_WithTagNameFilterAndUserName_FiltersByAuthor()
    {
        var (db, sut, user1Id, user1Name, user2Id, _, tid) = await CreateScopeAsync();
        await using (db)
        {
            var rootTag = await db.Tags.FirstAsync(t => t.Name == Tag.RootTagName);
            var parentTagNode = rootTag.Node.GetDescendant(null, null);
            var parentTagName = $"ParentNameU_{tid}";
            var parentTag = new Tag
            {
                Name = parentTagName,
                OwnerId = user1Id,
                Node = parentTagNode
            };
            db.Tags.Add(parentTag);
            await db.SaveChangesAsync();

            var childTag = new Tag
            {
                Name = $"ChildNameU_{tid}",
                OwnerId = user1Id,
                ParentTagId = parentTag.Id,
                Node = parentTagNode.GetDescendant(null, null)
            };
            db.Tags.Add(childTag);
            await db.SaveChangesAsync();

            var itemUser1 = new Item { Content = $"ItemU1_{tid}", OwnerId = user1Id };
            var itemUser2 = new Item { Content = $"ItemU2_{tid}", OwnerId = user2Id };
            db.Items.AddRange(itemUser1, itemUser2);
            await db.SaveChangesAsync();

            db.TagRelations.AddRange(
                new TagRelation { ItemId = itemUser1.Id, TagId = childTag.Id, OwnerId = user1Id, Weight = 1 },
                new TagRelation { ItemId = itemUser2.Id, TagId = childTag.Id, OwnerId = user2Id, Weight = 1 }
            );
            await db.SaveChangesAsync();

            ItemListFilter[] filters = [new TagNameFilter(parentTagName, user1Name)];
            ItemListPageData result = await sut.LoadItemsAndTagsAsync(filters, []);

            Assert.Contains(result.Items, i => i.Id == itemUser1.Id);
            Assert.DoesNotContain(result.Items, i => i.Id == itemUser2.Id);
        }
    }

    [Fact]
    public async Task SearchTagNameSuggestionsAsync_ImportedTag_ReturnsWithUsername()
    {
        var (db, sut, user1Id, user1Name, _, _, tid) = await CreateScopeAsync();
        await using (db)
        {
            var rootTag = await db.Tags.FirstAsync(t => t.Name == Tag.RootTagName);
            var tagName = $"ImportedTag_{tid}";
            var importedTag = new Tag
            {
                Name = tagName,
                OwnerId = user1Id,
                Node = rootTag.Node.GetDescendant(null, null)
            };
            db.Tags.Add(importedTag);
            await db.SaveChangesAsync();

            // TagRelation is intentionally NOT added to simulate a just-imported tag

            var suggestions = await sut.SearchTagNameSuggestionsAsync(tagName);

            Assert.Contains(suggestions, s => s.TagId == importedTag.Id && s.TagName == tagName && s.UserName == user1Name);
        }
    }

    [Fact]
    public async Task LoadItemsAndTagsAsync_WithTagIdFilter_IgnoresUserNameAndReturnsAllDescendants()
    {
        var (db, sut, user1Id, _, _, user2Name, tid) = await CreateScopeAsync();
        await using (db)
        {
            var rootTag = await db.Tags.FirstAsync(t => t.Name == Tag.RootTagName);

            // 一般タグ (ID=2 など)
            var parentTag = new Tag
            {
                Name = $"Parent_{tid}",
                OwnerId = user1Id,
                ParentTagId = rootTag.Id,
                Node = rootTag.Node.GetDescendant(null, null)
            };
            db.Tags.Add(parentTag);
            await db.SaveChangesAsync();

            var childTag = new Tag
            {
                Name = $"Child_{tid}",
                OwnerId = user1Id,
                ParentTagId = parentTag.Id,
                Node = parentTag.Node.GetDescendant(null, null)
            };
            db.Tags.Add(childTag);

            var item = new Item
            {
                Content = $"Item_{tid}",
                OwnerId = user1Id
            };
            db.Items.Add(item);
            await db.SaveChangesAsync();

            db.TagRelations.Add(new TagRelation
            {
                ItemId = item.Id,
                TagId = childTag.Id,
                OwnerId = user1Id
            });
            await db.SaveChangesAsync();

            // TagIdFilter で一般タグ ID と user2 のユーザー名を指定 (f=2@user2 相当)
            // UserName (atmark以降) は無視され、user1 所有の子孫タグおよびアイテムが返るべき
            var filters = new List<ItemListFilter>
            {
                new(new TagIdFilter(parentTag.Id, user2Name))
            };

            var result = await sut.LoadItemsAndTagsAsync(filters, []);

            Assert.Contains(result.Tags, t => t.Id == childTag.Id);
            Assert.Contains(result.Items, i => i.Id == item.Id);

            // ルートタグ (f=1@system 相当) でも同様に UserName が無視されて全子孫タグとアイテムが返るべき
            var rootFilters = new List<ItemListFilter>
            {
                new(new TagIdFilter(rootTag.Id, "system"))
            };

            var rootResult = await sut.LoadItemsAndTagsAsync(rootFilters, []);

            Assert.Contains(rootResult.Tags, t => t.Id == childTag.Id);
            Assert.Contains(rootResult.Items, i => i.Id == item.Id);
        }
    }

    [Fact]
    public async Task LoadItemsAndTagsAsync_WithTagNameFilter_EnforcesUserName()
    {
        var (db, sut, user1Id, user1Name, user2Id, user2Name, tid) = await CreateScopeAsync();
        await using (db)
        {
            var rootTag = await db.Tags.FirstAsync(t => t.Name == Tag.RootTagName);
            var tagName = $"TagName_{tid}";

            var tag1 = new Tag
            {
                Name = tagName,
                OwnerId = user1Id,
                ParentTagId = rootTag.Id,
                Node = rootTag.Node.GetDescendant(null, null)
            };
            db.Tags.Add(tag1);

            var item1 = new Item { Content = $"Item1_{tid}", OwnerId = user1Id };
            var item2 = new Item { Content = $"Item2_{tid}", OwnerId = user2Id };
            db.Items.AddRange(item1, item2);
            await db.SaveChangesAsync();

            // item1 は user1 がタグ付け、item2 は user2 がタグ付け
            db.TagRelations.Add(new TagRelation { ItemId = item1.Id, TagId = tag1.Id, OwnerId = user1Id });
            db.TagRelations.Add(new TagRelation { ItemId = item2.Id, TagId = tag1.Id, OwnerId = user2Id });
            await db.SaveChangesAsync();

            // TagNameFilter で user1 を指定 -> item1 のみヒットし、item2 は除外されるべき
            var filterUser1 = new List<ItemListFilter>
            {
                new(new TagNameFilter(tagName, user1Name))
            };
            var resultUser1 = await sut.LoadItemsAndTagsAsync(filterUser1, []);
            Assert.Contains(resultUser1.Items, i => i.Id == item1.Id);
            Assert.DoesNotContain(resultUser1.Items, i => i.Id == item2.Id);

            // TagNameFilter で user2 を指定 -> item2 のみヒットし、item1 は除外されるべき
            var filterUser2 = new List<ItemListFilter>
            {
                new(new TagNameFilter(tagName, user2Name))
            };
            var resultUser2 = await sut.LoadItemsAndTagsAsync(filterUser2, []);
            Assert.Contains(resultUser2.Items, i => i.Id == item2.Id);
            Assert.DoesNotContain(resultUser2.Items, i => i.Id == item1.Id);

            // TagNameFilter で UserName なし -> 両方ヒットすべき
            var filterNoUser = new List<ItemListFilter>
            {
                new(new TagNameFilter(tagName, null))
            };
            var resultNoUser = await sut.LoadItemsAndTagsAsync(filterNoUser, []);
            Assert.Contains(resultNoUser.Items, i => i.Id == item1.Id);
            Assert.Contains(resultNoUser.Items, i => i.Id == item2.Id);
        }
    }

    public sealed class DbContextFactoryStub(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);

        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}