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

        IReadOnlyList<Tag> results = await _provider.SearchUserTagsAsync(testUser, tid);

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

    [Fact]
    public async Task ImportCsvTagsAsync_WithThreeOrMoreColumns_SetsLastTwoColumnsAsLeafTagContent()
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

        // 4列: タグ2階層 + 2列Content
        var csvContent = $"Animal_{tid},Dog_{tid},イヌ科の哺乳類,ペットとして飼育される";

        _ = await _provider.ImportCsvTagsAsync(testUser, rootTag.Name, csvContent, false);

        await using (var dbContext = await _dbFactory.CreateDbContextAsync())
        {
            List<Tag> tags = await dbContext.Tags
                .Where(t => t.OwnerId == testUser && t.Name.Contains(tid))
                .ToListAsync();

            Tag animal = tags.Single(t => t.Name == $"Animal_{tid}");
            Tag dog = tags.Single(t => t.Name == $"Dog_{tid}");

            // 親タグ階層の確認
            Assert.Equal(rootTag.Id, animal.ParentTagId);
            Assert.Equal(animal.Id, dog.ParentTagId);

            // 中間タグの Content は空、最下層タグの Content は末尾2列が改行結合されていること
            Assert.Equal(string.Empty, animal.Content);
            Assert.Equal("イヌ科の哺乳類\nペットとして飼育される", dog.Content);
        }
    }

    [Fact]
    public async Task ImportCsvTagsAsync_WithPartialContentColumns_SetsLeafTagContentCorrectly()
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

        // 行1: 2列目Contentのみ存在、行2: 1列目Contentのみ存在
        var csvContent = $"Animal_{tid},Cat_{tid},,ネコ科の動物\nAnimal_{tid},Fox_{tid},キツネ属,";

        _ = await _provider.ImportCsvTagsAsync(testUser, rootTag.Name, csvContent, false);

        await using (var dbContext = await _dbFactory.CreateDbContextAsync())
        {
            List<Tag> tags = await dbContext.Tags
                .Where(t => t.OwnerId == testUser && t.Name.Contains(tid))
                .ToListAsync();

            Tag cat = tags.Single(t => t.Name == $"Cat_{tid}");
            Tag fox = tags.Single(t => t.Name == $"Fox_{tid}");

            Assert.Equal("ネコ科の動物", cat.Content);
            Assert.Equal("キツネ属", fox.Content);
        }
    }

    [Fact]
    public async Task ImportCsvTagsAsync_WithExistingTag_UpdatesContentWhenSpecified()
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

            var existingDog = new Tag
            {
                Name = $"Dog_{tid}",
                OwnerId = testUser,
                ParentTagId = rootTag.Id,
                Content = "古い内容"
            };
            _ = dbContext.Tags.Add(existingDog);
            _ = await dbContext.SaveChangesAsync();
        }

        var csvContent = $"Dog_{tid},新しい概要,新しい詳細";

        _ = await _provider.ImportCsvTagsAsync(testUser, rootTag.Name, csvContent, false);

        await using (var dbContext = await _dbFactory.CreateDbContextAsync())
        {
            Tag dog = await dbContext.Tags.SingleAsync(t => t.OwnerId == testUser && t.Name == $"Dog_{tid}");
            Assert.Equal("新しい概要\n新しい詳細", dog.Content);
        }
    }

    [Fact]
    public async Task ImportCsvTagsAsync_WithQuotedCommaContent_ParsesCorrectly()
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

        var csvContent = $"Bird_{tid},\"鳥類, 脊椎動物\",\"飛翔能力を持つ\"";

        _ = await _provider.ImportCsvTagsAsync(testUser, rootTag.Name, csvContent, false);

        await using (var dbContext = await _dbFactory.CreateDbContextAsync())
        {
            Tag bird = await dbContext.Tags.SingleAsync(t => t.OwnerId == testUser && t.Name == $"Bird_{tid}");
            Assert.Equal("鳥類, 脊椎動物\n飛翔能力を持つ", bird.Content);
        }
    }

    [Fact]
    public async Task ImportCsvTagsAsync_UnderRootTag_CreatesMultiLevelHierarchyWithContent_AndSkipsEmptyLines()
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
            rootTag = await dbContext.Tags.FirstOrDefaultAsync(t => t.Name == Tag.RootTagName)
                      ?? new Tag { Name = Tag.RootTagName, OwnerId = "system", IsSystem = true, Node = Microsoft.EntityFrameworkCore.HierarchyId.GetRoot() };
            if (rootTag.Id == 0)
            {
                _ = dbContext.Tags.Add(rootTag);
                _ = await dbContext.SaveChangesAsync();
            }
        }

        // 実際の「体関係量.csv」と「表1.csv」と同様の形式（5階層タグ + 2列Content + 空カンマ行）
        var csvContent = $"体_{tid},関係_{tid},量_{tid},群・組・対_{tid},ワンセット_{tid},ワンセット,わんせっと\n,,,,,\n\n";

        _ = await _provider.ImportCsvTagsAsync(testUser, Tag.RootTagName, csvContent, false);

        await using (var dbContext = await _dbFactory.CreateDbContextAsync())
        {
            List<Tag> tags = await dbContext.Tags
                .Where(t => t.OwnerId == testUser && t.Name.Contains(tid))
                .ToListAsync();

            Assert.Equal(5, tags.Count);

            Tag tai = tags.Single(t => t.Name == $"体_{tid}");
            Tag kankei = tags.Single(t => t.Name == $"関係_{tid}");
            Tag ryou = tags.Single(t => t.Name == $"量_{tid}");
            Tag gun = tags.Single(t => t.Name == $"群・組・対_{tid}");
            Tag oneSet = tags.Single(t => t.Name == $"ワンセット_{tid}");

            Assert.Equal(rootTag.Id, tai.ParentTagId);
            Assert.Equal(tai.Id, kankei.ParentTagId);
            Assert.Equal(kankei.Id, ryou.ParentTagId);
            Assert.Equal(ryou.Id, gun.ParentTagId);
            Assert.Equal(gun.Id, oneSet.ParentTagId);

            Assert.Equal(string.Empty, tai.Content);
            Assert.Equal(string.Empty, gun.Content);
            Assert.Equal("ワンセット\nわんせっと", oneSet.Content);
        }
    }

    [Fact]
    public async Task ImportCsvTagsAsync_WhenParentTagIncludedInFirstColumnOfCsv_DoesNotDuplicateParent()
    {
        var tid = Guid.NewGuid().ToString("N")[..8];
        var testUser = $"testuser_{tid}";

        await using (var dbContext = await _dbFactory.CreateDbContextAsync())
        {
            await dbContext.SeedUsersAsync(testUser);
        }

        Tag parentTag;
        await using (var dbContext = await _dbFactory.CreateDbContextAsync())
        {
            parentTag = new Tag { Name = $"Parent_{tid}", OwnerId = testUser };
            _ = dbContext.Tags.Add(parentTag);
            _ = await dbContext.SaveChangesAsync();
        }

        // 親タグ "Parent_{tid}" を親として選択し、CSV の先頭列にも "Parent_{tid}" がある場合
        var csvContent = $"Parent_{tid},Child_{tid},説明,詳細";

        _ = await _provider.ImportCsvTagsAsync(testUser, parentTag.Name, csvContent, false);

        await using (var dbContext = await _dbFactory.CreateDbContextAsync())
        {
            List<Tag> tags = await dbContext.Tags
                .Where(t => t.OwnerId == testUser && t.Name.Contains(tid))
                .ToListAsync();

            // 親タグと子タグの計2つだけが存在すること（親タグが重複作成されていないこと）
            Assert.Equal(2, tags.Count);

            Tag child = tags.Single(t => t.Name == $"Child_{tid}");
            Assert.Equal(parentTag.Id, child.ParentTagId);
            Assert.Equal("説明\n詳細", child.Content);
        }
    }

    [Fact]
    public async Task ImportCsvTagsAsync_WhenTagAlreadyExistsInDb_DoesNotThrowDuplicateKeyException()
    {
        var tid = Guid.NewGuid().ToString("N")[..8];
        var testUser = $"testuser_{tid}";

        await using (var dbContext = await _dbFactory.CreateDbContextAsync())
        {
            await dbContext.SeedUsersAsync(testUser);
        }

        Tag rootTag;
        Tag existingTag;
        await using (var dbContext = await _dbFactory.CreateDbContextAsync())
        {
            rootTag = new Tag { Name = $"Root_{tid}", OwnerId = testUser };
            _ = dbContext.Tags.Add(rootTag);
            _ = await dbContext.SaveChangesAsync();

            existingTag = new Tag
            {
                Name = $"Existing_{tid}",
                OwnerId = testUser,
                ParentTagId = rootTag.Id,
                Content = "既存内容"
            };
            _ = dbContext.Tags.Add(existingTag);
            _ = await dbContext.SaveChangesAsync();
        }

        // 既にDBに存在するタグ名 "Existing_{tid}" を含むCSVをインポート
        var csvContent = $"Existing_{tid},NewChild_{tid},概要,詳細";

        _ = await _provider.ImportCsvTagsAsync(testUser, rootTag.Name, csvContent, false);

        await using (var dbContext = await _dbFactory.CreateDbContextAsync())
        {
            List<Tag> tags = await dbContext.Tags
                .Where(t => t.OwnerId == testUser && t.Name.Contains(tid))
                .ToListAsync();

            // Root, Existing, NewChild の3つ
            Assert.Equal(3, tags.Count);

            Tag newChild = tags.Single(t => t.Name == $"NewChild_{tid}");
            Assert.Equal(existingTag.Id, newChild.ParentTagId);
        }
    }

    [Fact]
    public async Task ImportCsvTagsAsync_WithTabSeparatedValues_ImportsHierarchyAndContentCorrectly()
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
            rootTag = new Tag { Name = $"Root_{tid}", OwnerId = testUser };
            _ = dbContext.Tags.Add(rootTag);
            _ = await dbContext.SaveChangesAsync();
        }

        // ユーザーが提示したタブ区切り形式（相、活動、言語、評判、名高い、高名）
        var tsvContent = $"相_{tid}\t活動_{tid}\t言語_{tid}\t評判_{tid}\t名高い_{tid}\t名高い\tなだかい\n" +
                         $"相_{tid}\t活動_{tid}\t言語_{tid}\t評判_{tid}\t高名_{tid}\t高名\tこうめい";

        TagImportResult result = await _provider.ImportCsvTagsAsync(testUser, rootTag.Name, tsvContent, false);

        // 新規作成されたタグ: 相, 活動, 言語, 評判, 名高い, 高名 の計6件
        Assert.Equal(6, result.CreatedCount);

        await using (var dbContext = await _dbFactory.CreateDbContextAsync())
        {
            List<Tag> tags = await dbContext.Tags
                .Where(t => t.OwnerId == testUser && t.Name.Contains(tid))
                .ToListAsync();

            // Root + 6 = 7
            Assert.Equal(7, tags.Count);

            Tag nadakai = tags.Single(t => t.Name == $"名高い_{tid}");
            Assert.Equal("名高い\nなだかい", nadakai.Content);

            Tag koumei = tags.Single(t => t.Name == $"高名_{tid}");
            Assert.Equal("高名\nこうめい", koumei.Content);

            Tag hyouban = tags.Single(t => t.Name == $"評判_{tid}");
            Assert.Equal(hyouban.Id, nadakai.ParentTagId);
            Assert.Equal(hyouban.Id, koumei.ParentTagId);
        }
    }

    [Fact]
    public void GetSafeChildNode_WhenLastChildIsDescendant_ReturnsValidChild()
    {
        var parentNode = HierarchyId.Parse("/23/1/1/");
        var validChild = HierarchyId.Parse("/23/1/1/1/");

        var newChild = ImportTagDataProvider.GetSafeChildNode(parentNode, validChild);

        Assert.NotNull(newChild);
        Assert.True(newChild.IsDescendantOf(parentNode));
        Assert.Equal(parentNode, newChild.GetAncestor(1));
    }

    [Fact]
    public void GetSafeChildNode_WhenLastChildIsUnrelated_DoesNotThrow24008_AndFallsBackSafely()
    {
        // エラー 24008 のケース: child1 was '/15/13/' and 'this' was '/23/1/1/'
        var parentNode = HierarchyId.Parse("/23/1/1/");
        var unrelatedNode = HierarchyId.Parse("/15/13/");

        // 普通に parentNode.GetDescendant(unrelatedNode, null) を呼ぶと 24008 エラー（HierarchyIdException）になる
        Assert.Throws<Microsoft.SqlServer.Types.HierarchyIdException>(() => parentNode.GetDescendant(unrelatedNode, null));

        // GetSafeChildNode では例外をスローせず安全にフォールバックして子ノードを生成する
        var safeChild = ImportTagDataProvider.GetSafeChildNode(parentNode, unrelatedNode);

        Assert.NotNull(safeChild);
        Assert.True(safeChild.IsDescendantOf(parentNode));
        Assert.Equal(parentNode, safeChild.GetAncestor(1));
    }

    [Fact]
    public async Task ImportCsvTagsAsync_WhenExistingTagHasInconsistentNode_ImportsSuccessfullyWithout24008Error()
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
            rootTag = new Tag
            {
                Name = $"Root_{tid}",
                OwnerId = testUser,
                Node = HierarchyId.Parse("/23/1/1/")
            };
            _ = dbContext.Tags.Add(rootTag);
            _ = await dbContext.SaveChangesAsync();

            // 不整合データ: ParentTagId は rootTag だが、Node は無関係な /15/13/ になっている
            var inconsistentTag = new Tag
            {
                Name = $"BrokenTag_{tid}",
                OwnerId = testUser,
                ParentTagId = rootTag.Id,
                Node = HierarchyId.Parse("/15/13/")
            };
            _ = dbContext.Tags.Add(inconsistentTag);
            _ = await dbContext.SaveChangesAsync();
        }

        // rootTag の下に新しい子タグを追加する CSV をインポート
        var csvContent = $"NewTag_{tid},Value1,Value2";

        TagImportResult result = await _provider.ImportCsvTagsAsync(testUser, rootTag.Name, csvContent, false);

        Assert.Equal(1, result.CreatedCount);

        await using (var dbContext = await _dbFactory.CreateDbContextAsync())
        {
            Tag newTag = await dbContext.Tags.SingleAsync(t => t.OwnerId == testUser && t.Name == $"NewTag_{tid}");
            Assert.Equal(rootTag.Id, newTag.ParentTagId);
            Assert.True(newTag.Node.IsDescendantOf(rootTag.Node));
            Assert.Equal(rootTag.Node, newTag.Node.GetAncestor(1));
        }
    }
}