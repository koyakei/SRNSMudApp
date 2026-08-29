using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using SRNSMudApp.Data;
using SRNSMudApp.Services;

namespace SRNSMudApp.Tests.Services;

public class ImportTagDataProviderTests : IAsyncLifetime
{
    private MsSqlTestDatabase _sharedDb = null!;
    private IDbContextFactory<ApplicationDbContext> _dbFactory = null!;
    private ImportTagDataProvider _provider = null!;

    public async Task InitializeAsync()
    {
        _sharedDb = await SharedMsSqlTestDatabase.GetInstanceAsync();
        var services = new ServiceCollection();
        _ = services.AddMsSqlDbFactory(_sharedDb.ConnectionString);
        _ = services.AddScoped<IImportTagDataProvider, ImportTagDataProvider>();
        _ = services.AddScoped(_ => new Mock<ITagEmbeddingService>().Object);

        var sp = services.BuildServiceProvider();
        _dbFactory = sp.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        _provider = (ImportTagDataProvider)sp.GetRequiredService<IImportTagDataProvider>();

        await using var dbContext = await _dbFactory.CreateDbContextAsync();
        await dbContext.SeedUsersAsync("system");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ImportCsvTagsAsync_UnderSelectedParent_CreatesTwoLevelHierarchy()
    {
        var tid = Guid.NewGuid().ToString("N")[..8];
        var testUser = $"testuser_{tid}";

        await using (var dbContext = await _dbFactory.CreateDbContextAsync())
        {
            await dbContext.SeedUsersAsync(testUser);
        }

        Tag rootTag;
        await using (var dbContext = await _dbFactory.CreateDbContextAsync())
        {
            rootTag = new Tag { Name = $"RootTag_{tid}", OwnerId = testUser };
            _ = dbContext.Tags.Add(rootTag);
            _ = await dbContext.SaveChangesAsync();
        }

        var csvContent = $"Animal_{tid},Dog_{tid}\nAnimal_{tid},Cat_{tid}";

        _ = await _provider.ImportCsvTagsAsync(testUser, rootTag.Name, csvContent, false);

        await using (var dbContext = await _dbFactory.CreateDbContextAsync())
        {
            List<Tag> tags = await dbContext.Tags.Where(t => t.OwnerId == testUser).ToListAsync();

            Tag animal = tags.Single(t => t.Name == $"Animal_{tid}");
            Assert.Equal(rootTag.Id, animal.ParentTagId);

            Tag dog = tags.Single(t => t.Name == $"Dog_{tid}");
            Assert.Equal(animal.Id, dog.ParentTagId);

            Tag cat = tags.Single(t => t.Name == $"Cat_{tid}");
            Assert.Equal(animal.Id, cat.ParentTagId);
        }
    }

    [Fact]
    public async Task SearchUserTagsAsync_ShouldIncludeSystemTags()
    {
        var tid = Guid.NewGuid().ToString("N")[..8];
        var testUser = $"testuser_{tid}";
        var otherUser = $"otheruser_{tid}";

        await using (var dbContext = await _dbFactory.CreateDbContextAsync())
        {
            await dbContext.SeedUsersAsync(testUser, otherUser);
            dbContext.Tags.AddRange(
                new Tag { Name = $"UserTag1_{tid}", OwnerId = testUser },
                new Tag { Name = $"SystemRootTag_{tid}", IsSystem = true, OwnerId = "system" },
                new Tag { Name = $"OtherUserTag_{tid}", OwnerId = otherUser }
            );
            _ = await dbContext.SaveChangesAsync();
        }

        IReadOnlyList<Tag> results = await _provider.SearchUserTagsAsync(testUser, "");

        Assert.Contains(results, t => t.Name == $"UserTag1_{tid}");
        Assert.Contains(results, t => t.Name == $"SystemRootTag_{tid}");
        Assert.DoesNotContain(results, t => t.Name == $"OtherUserTag_{tid}");
    }

    [Fact]
    public async Task ImportCsvTagsAsync_AsSystem_CreatesHierarchyUnderSystemOwner()
    {
        var tid = Guid.NewGuid().ToString("N")[..8];
        var adminUser = $"admin_{tid}";

        await using (var dbContext = await _dbFactory.CreateDbContextAsync())
        {
            await dbContext.SeedUsersAsync(adminUser);
        }

        Tag systemRootTag;
        await using (var dbContext = await _dbFactory.CreateDbContextAsync())
        {
            systemRootTag = new Tag { Name = $"SystemCategory_{tid}", IsSystem = true, OwnerId = "system" };
            _ = dbContext.Tags.Add(systemRootTag);
            _ = await dbContext.SaveChangesAsync();
        }

        var csvContent = $"Science_{tid},Physics_{tid}";

        _ = await _provider.ImportCsvTagsAsync(adminUser, systemRootTag.Name, csvContent, true);

        await using (var dbContext = await _dbFactory.CreateDbContextAsync())
        {
            List<Tag> systemTags = await dbContext.Tags.Where(t => t.OwnerId == "system" && t.Name.EndsWith(tid)).ToListAsync();

            Tag science = systemTags.Single(t => t.Name == $"Science_{tid}");
            Assert.True(science.IsSystem);
            Assert.Equal("system", science.OwnerId);
            Assert.Equal(systemRootTag.Id, science.ParentTagId);

            Tag physics = systemTags.Single(t => t.Name == $"Physics_{tid}");
            Assert.True(physics.IsSystem);
            Assert.Equal("system", physics.OwnerId);
            Assert.Equal(science.Id, physics.ParentTagId);
        }
    }

    [Fact]
    public async Task ImportCsvTagsAsync_WithMultipleSiblings_AssignsUniqueHierarchyIds()
    {
        var tid = Guid.NewGuid().ToString("N")[..8];
        var testUser = $"testuser_{tid}";

        await using (var dbContext = await _dbFactory.CreateDbContextAsync())
        {
            await dbContext.SeedUsersAsync(testUser);
        }

        Tag rootTag;
        await using (var dbContext = await _dbFactory.CreateDbContextAsync())
        {
            rootTag = new Tag { Name = $"RootTag_{tid}", OwnerId = testUser };
            _ = dbContext.Tags.Add(rootTag);
            _ = await dbContext.SaveChangesAsync();
        }

        // Parentの下に複数の子タグ（兄弟タグ）をインポート
        var csvContent = $"Parent_{tid},Child1_{tid}\nParent_{tid},Child2_{tid}\nParent_{tid},Child3_{tid}";

        _ = await _provider.ImportCsvTagsAsync(testUser, rootTag.Name, csvContent, false);

        await using (var dbContext = await _dbFactory.CreateDbContextAsync())
        {
            List<Tag> tags = await dbContext.Tags
                .Where(t => t.OwnerId == testUser && t.Name.Contains(tid))
                .ToListAsync();

            Tag parent = tags.Single(t => t.Name == $"Parent_{tid}");
            Tag child1 = tags.Single(t => t.Name == $"Child1_{tid}");
            Tag child2 = tags.Single(t => t.Name == $"Child2_{tid}");
            Tag child3 = tags.Single(t => t.Name == $"Child3_{tid}");

            Assert.Equal(rootTag.Id, parent.ParentTagId);
            Assert.Equal(parent.Id, child1.ParentTagId);
            Assert.Equal(parent.Id, child2.ParentTagId);
            Assert.Equal(parent.Id, child3.ParentTagId);

            // すべてのノードが存在し、互いに異なる（重複していない）ことを検証
            Assert.NotNull(child1.Node);
            Assert.NotNull(child2.Node);
            Assert.NotNull(child3.Node);

            Assert.NotEqual(child1.Node, child2.Node);
            Assert.NotEqual(child2.Node, child3.Node);
            Assert.NotEqual(child1.Node, child3.Node);

            // それぞれが parent.Node の子孫であることを検証
            Assert.True(child1.Node.IsDescendantOf(parent.Node));
            Assert.True(child2.Node.IsDescendantOf(parent.Node));
            Assert.True(child3.Node.IsDescendantOf(parent.Node));
        }
    }
}