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
    public async Task LoadItemsAndTagsAsync_WithTagIdFilterAndUserName_FiltersByAuthor()
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

            ItemListFilter[] filters = [new TagIdFilter(parentTag.Id, user1Name)];
            ItemListPageData result = await sut.LoadItemsAndTagsAsync(filters, []);

            Assert.Contains(result.Items, i => i.Id == itemUser1.Id);
            Assert.DoesNotContain(result.Items, i => i.Id == itemUser2.Id);
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

    private sealed class DbContextFactoryStub(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);

        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}