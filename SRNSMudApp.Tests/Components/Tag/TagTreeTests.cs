using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Bunit;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using MudBlazor.Services;

using SRNSMudApp.Components.Tag;
using SRNSMudApp.Data;
using SRNSMudApp.Tests.TestSupport;

using Xunit;
using Xunit.Abstractions;

namespace SRNSMudApp.Tests.Components.Tag;

[Collection(MsSqlCollection.Name)]
public class TagTreeTests(MsSqlContainerFixture fixture, ITestOutputHelper output) : IAsyncLifetime
{
    private readonly ITestOutputHelper _output = output;
    private readonly BunitContext _ctx = new();
    private MsSqlTestDatabase _testDb = null!;

    public async Task InitializeAsync()
    {
        _testDb = await MsSqlTestDatabase.CreateAsync(fixture.ConnectionString, nameof(TagTreeTests));

        _ = _ctx.Services.AddAuth("test-user-id");
        _ = _ctx.Services.AddSrnsComponentServices();
        _ctx.Services.AddMsSqlDbFactory(_testDb.ConnectionString);

        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        BunitContext.DefaultWaitTimeout = TimeSpan.FromSeconds(15);

        IDbContextFactory<ApplicationDbContext> dbFactory =
            _ctx.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        await using ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync();
        dbContext.Users.AddRange(
            new ApplicationUser { Id = "test-user-id", UserName = "test-user-id" },
            new ApplicationUser { Id = "system", UserName = "system" },
            new ApplicationUser { Id = "other-user-id", UserName = "other-user-id" },
            new ApplicationUser { Id = "other-user", UserName = "other-user" },
            new ApplicationUser { Id = "someone-else", UserName = "someone-else" }
        );
        await dbContext.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
        await _testDb.DisposeAsync();
    }

    [Fact]
    public async Task JqTree_InitializesWithCorrectJson_WhenSingleRootNodeHasMultipleChildren()
    {
        // Arrange
        IDbContextFactory<ApplicationDbContext> dbFactory =
            _ctx.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        SRNSMudApp.Data.Tag rootTag;
        SRNSMudApp.Data.Tag child1;
        SRNSMudApp.Data.Tag child2;
        await using (ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync())
        {
            rootTag = new SRNSMudApp.Data.Tag { Name = "Root", IsSystem = false, OwnerId = "test-user-id" };
            child1 = new SRNSMudApp.Data.Tag
            {
                Name = "Child1",
                ParentTag = rootTag,
                IsSystem = false,
                OwnerId = "test-user-id"
            };
            child2 = new SRNSMudApp.Data.Tag
            {
                Name = "Child2",
                ParentTag = rootTag,
                IsSystem = false,
                OwnerId = "test-user-id"
            };

            dbContext.Tags.AddRange(rootTag, child1, child2);
            _ = await dbContext.SaveChangesAsync();
        }

        var jsInteropInvocations = new List<JSRuntimeInvocation>();
        _ = _ctx.JSInterop.SetupVoid("jqTreeInterop.init", invocation =>
        {
            jsInteropInvocations.Add(invocation);
            return true;
        });

        // Act
        IRenderedComponent<TagTree> component = _ctx.Render<TagTree>();

        // Assert
        component.WaitForAssertion(() => Assert.NotEmpty(jsInteropInvocations));

        JSRuntimeInvocation invocation = jsInteropInvocations.First(i => i.Identifier == "jqTreeInterop.init");
        var treeDataJson = invocation.Arguments[1] as string;
        _output.WriteLine("JSON Output: " + treeDataJson);

        // Verify the JSON structure
        Assert.NotNull(treeDataJson);
        Assert.Contains($"\"id\":{rootTag.Id}", treeDataJson);
        Assert.Contains("\"children\":", treeDataJson);
        Assert.Contains($"\"id\":{child1.Id}", treeDataJson);
        Assert.Contains($"\"id\":{child2.Id}", treeDataJson);
    }

    [Fact]
    public async Task JqTree_DisplaysChildren_WhenRootNodeIsSystemTag()
    {
        // Arrange
        IDbContextFactory<ApplicationDbContext> dbFactory =
            _ctx.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        SRNSMudApp.Data.Tag child1;
        SRNSMudApp.Data.Tag child2;
        await using (ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync())
        {
            // System tag should be filtered out
            var rootTag = new SRNSMudApp.Data.Tag { Name = "SystemRoot", IsSystem = true, OwnerId = "system" };
            // Children are not system tags, so they should be displayed as root nodes
            child1 = new SRNSMudApp.Data.Tag
            {
                Name = "UserChild1",
                ParentTag = rootTag,
                IsSystem = false,
                OwnerId = "test-user-id"
            };
            child2 = new SRNSMudApp.Data.Tag
            {
                Name = "UserChild2",
                ParentTag = rootTag,
                IsSystem = false,
                OwnerId = "test-user-id"
            };

            dbContext.Tags.AddRange(rootTag, child1, child2);
            _ = await dbContext.SaveChangesAsync();
        }

        var jsInteropInvocations = new List<JSRuntimeInvocation>();
        _ = _ctx.JSInterop.SetupVoid("jqTreeInterop.init", invocation =>
        {
            jsInteropInvocations.Add(invocation);
            return true;
        });

        // Act
        IRenderedComponent<TagTree> component = _ctx.Render<TagTree>();

        // Assert
        component.WaitForAssertion(() => Assert.NotEmpty(jsInteropInvocations));

        JSRuntimeInvocation invocation = jsInteropInvocations.First(i => i.Identifier == "jqTreeInterop.init");
        var treeDataJson = invocation.Arguments[1] as string;

        // Verify that the children are displayed as root nodes
        Assert.NotNull(treeDataJson);
        Assert.Contains($"\"id\":{child1.Id}", treeDataJson);
        Assert.Contains($"\"id\":{child2.Id}", treeDataJson);
    }

    [Fact]
    public async Task JqTree_DisplaysCreatedTagTree_WhenSearchTextIsEmpty()
    {
        // Arrange
        IDbContextFactory<ApplicationDbContext> dbFactory =
            _ctx.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        SRNSMudApp.Data.Tag rootTag;
        SRNSMudApp.Data.Tag childTag;
        SRNSMudApp.Data.Tag otherUserTag;
        await using (ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync())
        {
            // Seed a tree structure for the current user
            rootTag =
                new SRNSMudApp.Data.Tag { Name = "MyRoot", IsSystem = false, OwnerId = "test-user-id" };
            childTag = new SRNSMudApp.Data.Tag
            {
                Name = "MyChild",
                ParentTag = rootTag,
                IsSystem = false,
                OwnerId = "test-user-id"
            };

            // Seed a tag for another user to ensure sorting/prioritization works (though we won't hit the 2000 limit)
            otherUserTag = new SRNSMudApp.Data.Tag
            {
                Name = "OtherRoot",
                IsSystem = false,
                OwnerId = "other-user-id"
            };

            dbContext.Tags.AddRange(rootTag, childTag, otherUserTag);
            _ = await dbContext.SaveChangesAsync();
        }

        var jsInteropInvocations = new List<JSRuntimeInvocation>();
        _ = _ctx.JSInterop.SetupVoid("jqTreeInterop.init", invocation =>
        {
            jsInteropInvocations.Add(invocation);
            return true;
        });

        // Act - Rendering the component will trigger LoadDataAsync with an empty search text
        IRenderedComponent<TagTree> component = _ctx.Render<TagTree>();

        // Assert
        component.WaitForAssertion(() => Assert.NotEmpty(jsInteropInvocations));

        JSRuntimeInvocation invocation = jsInteropInvocations.First(i => i.Identifier == "jqTreeInterop.init");
        var treeDataJson = invocation.Arguments[1] as string;

        // Verify that the created tag tree (MyRoot and MyChild) is displayed correctly in the JSON
        Assert.NotNull(treeDataJson);
        Assert.Contains($"\"id\":{rootTag.Id}", treeDataJson);
        Assert.Contains("\"name\":\"MyRoot\"", treeDataJson);
        Assert.Contains("\"children\":", treeDataJson);
        Assert.Contains($"\"id\":{childTag.Id}", treeDataJson);
        Assert.Contains("\"name\":\"MyChild\"", treeDataJson);

        // Also verify the other user's tag is included since we are under the 2000 limit
        Assert.Contains($"\"id\":{otherUserTag.Id}", treeDataJson);
    }

    [Fact]
    public async Task JqTree_DoesNotCrash_WhenCircularReferenceExists()
    {
        // Arrange
        IDbContextFactory<ApplicationDbContext> dbFactory =
            _ctx.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        await using (ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync())
        {
            var tag1 = new SRNSMudApp.Data.Tag
            {
                Name = "Tag1",
                IsSystem = false,
                OwnerId = "test-user-id"
            };
            var tag2 = new SRNSMudApp.Data.Tag
            {
                Name = "Tag2",
                IsSystem = false,
                OwnerId = "test-user-id"
            };
            var tag3 = new SRNSMudApp.Data.Tag
            {
                Name = "Tag3",
                IsSystem = false,
                OwnerId = "test-user-id"
            };

            dbContext.Tags.AddRange(tag1, tag2, tag3);
            _ = await dbContext.SaveChangesAsync();

            tag1.ParentTagId = tag3.Id;
            tag2.ParentTagId = tag1.Id;
            tag3.ParentTagId = tag2.Id;
            _ = await dbContext.SaveChangesAsync();
        }

        var jsInteropInvocations = new List<JSRuntimeInvocation>();
        _ = _ctx.JSInterop.SetupVoid("jqTreeInterop.init", invocation =>
        {
            jsInteropInvocations.Add(invocation);
            return true;
        });

        // Act - Rendering the component will trigger LoadDataAsync and attempt to build the tree
        // If there's a stack overflow, this test will crash the runner.
        IRenderedComponent<TagTree> component = _ctx.Render<TagTree>();

        component.WaitForAssertion(() => Assert.NotEmpty(jsInteropInvocations));

        JSRuntimeInvocation invocation = jsInteropInvocations.First(i => i.Identifier == "jqTreeInterop.init");
        var treeDataJson = invocation.Arguments[1] as string;

        Assert.NotNull(treeDataJson);
    }

    [Fact]
    public async Task JqTree_DisplaysUpTo2000Tags_WhenDatabaseHasManyTags()
    {
        // Arrange
        IDbContextFactory<ApplicationDbContext> dbFactory =
            _ctx.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        await using (ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync())
        {
            // Seed 2500 tags
            var tags = new List<SRNSMudApp.Data.Tag>();
            for (var i = 1; i <= 2500; i++)
            {
                tags.Add(new SRNSMudApp.Data.Tag
                {
                    Name = $"Tag {i}",
                    IsSystem = false,
                    OwnerId = "test-user-id"
                });
            }

            dbContext.Tags.AddRange(tags);
            _ = await dbContext.SaveChangesAsync();
        }

        var jsInteropInvocations = new List<JSRuntimeInvocation>();
        _ = _ctx.JSInterop.SetupVoid("jqTreeInterop.init", invocation =>
        {
            jsInteropInvocations.Add(invocation);
            return true;
        });

        // Act
        IRenderedComponent<TagTree> component = _ctx.Render<TagTree>();

        // Assert
        component.WaitForAssertion(() => Assert.NotEmpty(jsInteropInvocations));

        JSRuntimeInvocation invocation = jsInteropInvocations.First(i => i.Identifier == "jqTreeInterop.init");
        var treeDataJson = invocation.Arguments[1] as string;

        Assert.NotNull(treeDataJson);

        // Check if JSON length is reasonable or if it threw any exception during serialization
        var idCount = treeDataJson.Split("\"id\":").Length - 1;
        Assert.True(idCount is >= 2000 and <= 2000, $"Expected exactly 2000 tags, but got {idCount}");
    }

    [Fact]
    public async Task JqTree_DisplaysTag_WhenParentIsNonExistent()
    {
        IDbContextFactory<ApplicationDbContext> dbFactory =
            _ctx.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        SRNSMudApp.Data.Tag childTag;
        await using (ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync())
        {
            // System parent tag is filtered out from TagTree in memory
            var systemParent = new SRNSMudApp.Data.Tag
            {
                Name = "SystemParent",
                IsSystem = true,
                OwnerId = "system"
            };
            childTag = new SRNSMudApp.Data.Tag
            {
                Name = "Orphan",
                ParentTag = systemParent,
                IsSystem = false,
                OwnerId = "test-user-id"
            };
            dbContext.Tags.AddRange(systemParent, childTag);
            _ = await dbContext.SaveChangesAsync();
        }

        var jsInteropInvocations = new List<JSRuntimeInvocation>();
        _ = _ctx.JSInterop.SetupVoid("jqTreeInterop.init", invocation =>
        {
            jsInteropInvocations.Add(invocation);
            return true;
        });
        IRenderedComponent<TagTree> component = _ctx.Render<TagTree>();
        component.WaitForAssertion(() => Assert.NotEmpty(jsInteropInvocations));
        var treeDataJson = jsInteropInvocations.First(i => i.Identifier == "jqTreeInterop.init").Arguments[1] as string;

        // Should be treated as root node
        Assert.Contains($"\"id\":{childTag.Id}", treeDataJson);
    }

    [Fact]
    public async Task JqTree_DisplaysTag_WhenSelfReferencing()
    {
        IDbContextFactory<ApplicationDbContext> dbFactory =
            _ctx.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        SRNSMudApp.Data.Tag tag;
        await using (ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync())
        {
            tag = new SRNSMudApp.Data.Tag
            {
                Name = "SelfRef",
                IsSystem = false,
                OwnerId = "test-user-id"
            };
            _ = dbContext.Tags.Add(tag);
            _ = await dbContext.SaveChangesAsync();

            tag.ParentTagId = tag.Id;
            _ = await dbContext.SaveChangesAsync();
        }

        var jsInteropInvocations = new List<JSRuntimeInvocation>();
        _ = _ctx.JSInterop.SetupVoid("jqTreeInterop.init", invocation =>
        {
            jsInteropInvocations.Add(invocation);
            return true;
        });
        IRenderedComponent<TagTree> component = _ctx.Render<TagTree>();
        component.WaitForAssertion(() => Assert.NotEmpty(jsInteropInvocations));
        var treeDataJson = jsInteropInvocations.First(i => i.Identifier == "jqTreeInterop.init").Arguments[1] as string;

        // This will FAIL if the tree builder skips self-referencing tags
        Assert.Contains($"\"id\":{tag.Id}", treeDataJson);
    }

    [Fact]
    public async Task JqTree_DisplaysTags_WhenDeeplyNested()
    {
        IDbContextFactory<ApplicationDbContext> dbFactory =
            _ctx.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        SRNSMudApp.Data.Tag? deepestTag = null;
        await using (ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync())
        {
            var tags = new List<SRNSMudApp.Data.Tag>();
            SRNSMudApp.Data.Tag? prev = null;
            // Max depth in JsonSerializer is 64 by default, we set it to 128. Let's try 130 to see if it fails.
            for (var i = 1; i <= 130; i++)
            {
                var tag = new SRNSMudApp.Data.Tag
                {
                    Name = $"Deep{i}",
                    ParentTag = prev,
                    IsSystem = false,
                    OwnerId = "test-user-id"
                };
                tags.Add(tag);
                prev = tag;
            }

            deepestTag = prev;
            dbContext.Tags.AddRange(tags);
            _ = await dbContext.SaveChangesAsync();
        }

        var jsInteropInvocations = new List<JSRuntimeInvocation>();
        _ = _ctx.JSInterop.SetupVoid("jqTreeInterop.init", invocation =>
        {
            jsInteropInvocations.Add(invocation);
            return true;
        });

        // This will FAIL if JsonException is thrown due to MaxDepth limit
        IRenderedComponent<TagTree> component = _ctx.Render<TagTree>();
        component.WaitForAssertion(() => Assert.NotEmpty(jsInteropInvocations));
        var treeDataJson = jsInteropInvocations.First(i => i.Identifier == "jqTreeInterop.init").Arguments[1] as string;

        Assert.Contains($"\"id\":{deepestTag!.Id}", treeDataJson);
    }

    // ============================================================
    // 検索フィールド空の場合にタグが表示されることを検証するテスト群
    // ============================================================

    /// <summary>
    ///     最も単純なケース: フラットなタグが1つだけ存在する
    /// </summary>
    [Fact]
    public async Task EmptySearch_DisplaysSingleFlatTag()
    {
        IDbContextFactory<ApplicationDbContext> dbFactory =
            _ctx.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        SRNSMudApp.Data.Tag tag;
        await using (ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync())
        {
            tag = new SRNSMudApp.Data.Tag
            {
                Name = "SoloTag",
                IsSystem = false,
                OwnerId = "test-user-id"
            };
            _ = dbContext.Tags.Add(tag);
            _ = await dbContext.SaveChangesAsync();
        }

        var json = await RenderAndGetTreeJson();

        Assert.NotNull(json);
        Assert.NotEqual("[]", json);
        Assert.Contains($"\"id\":{tag.Id}", json);
        Assert.Contains("\"name\":\"SoloTag\"", json);
    }

    /// <summary>
    ///     フラットなタグが複数存在する（親子関係なし）
    /// </summary>
    [Fact]
    public async Task EmptySearch_DisplaysMultipleFlatTags()
    {
        IDbContextFactory<ApplicationDbContext> dbFactory =
            _ctx.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        SRNSMudApp.Data.Tag tagA;
        SRNSMudApp.Data.Tag tagB;
        SRNSMudApp.Data.Tag tagC;
        await using (ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync())
        {
            tagA = new SRNSMudApp.Data.Tag { Name = "FlatA", IsSystem = false, OwnerId = "test-user-id" };
            tagB = new SRNSMudApp.Data.Tag { Name = "FlatB", IsSystem = false, OwnerId = "test-user-id" };
            tagC = new SRNSMudApp.Data.Tag { Name = "FlatC", IsSystem = false, OwnerId = "test-user-id" };
            dbContext.Tags.AddRange(tagA, tagB, tagC);
            _ = await dbContext.SaveChangesAsync();
        }

        var json = await RenderAndGetTreeJson();

        Assert.NotNull(json);
        Assert.NotEqual("[]", json);
        Assert.Contains($"\"id\":{tagA.Id}", json);
        Assert.Contains($"\"id\":{tagB.Id}", json);
        Assert.Contains($"\"id\":{tagC.Id}", json);
    }

    /// <summary>
    ///     フラットなタグとツリー構造のタグが混在する場合
    /// </summary>
    [Fact]
    public async Task EmptySearch_DisplaysMixedFlatAndTreeTags()
    {
        IDbContextFactory<ApplicationDbContext> dbFactory =
            _ctx.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        SRNSMudApp.Data.Tag flatTag;
        SRNSMudApp.Data.Tag treeRoot;
        SRNSMudApp.Data.Tag treeChild;
        await using (ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync())
        {
            flatTag = new SRNSMudApp.Data.Tag { Name = "FlatOnly", IsSystem = false, OwnerId = "test-user-id" };
            treeRoot = new SRNSMudApp.Data.Tag { Name = "TreeRoot", IsSystem = false, OwnerId = "test-user-id" };
            treeChild = new SRNSMudApp.Data.Tag
            {
                Name = "TreeChild",
                ParentTag = treeRoot,
                IsSystem = false,
                OwnerId = "test-user-id"
            };
            dbContext.Tags.AddRange(flatTag, treeRoot, treeChild);
            _ = await dbContext.SaveChangesAsync();
        }

        var json = await RenderAndGetTreeJson();

        Assert.NotNull(json);
        Assert.NotEqual("[]", json);
        Assert.Contains($"\"id\":{flatTag.Id}", json);
        Assert.Contains($"\"id\":{treeRoot.Id}", json);
        Assert.Contains($"\"id\":{treeChild.Id}", json);
    }

    /// <summary>
    ///     他ユーザーのタグのみが存在する場合でも表示される
    /// </summary>
    [Fact]
    public async Task EmptySearch_DisplaysOtherUserTagsWhenNoOwnTags()
    {
        IDbContextFactory<ApplicationDbContext> dbFactory =
            _ctx.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        SRNSMudApp.Data.Tag tag1;
        SRNSMudApp.Data.Tag tag2;
        await using (ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync())
        {
            tag1 = new SRNSMudApp.Data.Tag { Name = "OtherUserTag1", IsSystem = false, OwnerId = "other-user" };
            tag2 = new SRNSMudApp.Data.Tag { Name = "OtherUserTag2", IsSystem = false, OwnerId = "other-user" };
            dbContext.Tags.AddRange(tag1, tag2);
            _ = await dbContext.SaveChangesAsync();
        }

        var json = await RenderAndGetTreeJson();

        Assert.NotNull(json);
        Assert.NotEqual("[]", json);
        Assert.Contains($"\"id\":{tag1.Id}", json);
        Assert.Contains($"\"id\":{tag2.Id}", json);
    }

    /// <summary>
    ///     自分のタグと他ユーザーのタグが混在する場合、両方表示される
    /// </summary>
    [Fact]
    public async Task EmptySearch_DisplaysBothOwnAndOtherUserTags()
    {
        IDbContextFactory<ApplicationDbContext> dbFactory =
            _ctx.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        SRNSMudApp.Data.Tag myTag;
        SRNSMudApp.Data.Tag theirTag;
        await using (ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync())
        {
            myTag = new SRNSMudApp.Data.Tag { Name = "MyTag", IsSystem = false, OwnerId = "test-user-id" };
            theirTag = new SRNSMudApp.Data.Tag { Name = "TheirTag", IsSystem = false, OwnerId = "someone-else" };
            dbContext.Tags.AddRange(myTag, theirTag);
            _ = await dbContext.SaveChangesAsync();
        }

        var json = await RenderAndGetTreeJson();

        Assert.NotNull(json);
        Assert.NotEqual("[]", json);
        Assert.Contains($"\"id\":{myTag.Id}", json);
        Assert.Contains($"\"id\":{theirTag.Id}", json);
    }

    /// <summary>
    ///     ルートタグ1つだけ（子なし）の場合
    /// </summary>
    [Fact]
    public async Task EmptySearch_DisplaysSingleRootNoChildren()
    {
        IDbContextFactory<ApplicationDbContext> dbFactory =
            _ctx.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        await using (ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync())
        {
            _ = dbContext.Tags.Add(new SRNSMudApp.Data.Tag
            {
                Name = "LonelyRoot",
                IsSystem = false,
                OwnerId = "test-user-id"
            });
            _ = await dbContext.SaveChangesAsync();
        }

        var json = await RenderAndGetTreeJson();

        Assert.NotNull(json);
        Assert.NotEqual("[]", json);
        Assert.Contains("\"name\":\"LonelyRoot\"", json);
    }

    /// <summary>
    ///     複数のルートタグがそれぞれ子を持つ場合
    /// </summary>
    [Fact]
    public async Task EmptySearch_DisplaysMultipleRootsWithChildren()
    {
        IDbContextFactory<ApplicationDbContext> dbFactory =
            _ctx.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        SRNSMudApp.Data.Tag rootA;
        SRNSMudApp.Data.Tag childA1;
        SRNSMudApp.Data.Tag childA2;
        SRNSMudApp.Data.Tag rootB;
        SRNSMudApp.Data.Tag childB1;
        await using (ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync())
        {
            rootA = new SRNSMudApp.Data.Tag { Name = "RootA", IsSystem = false, OwnerId = "test-user-id" };
            childA1 = new SRNSMudApp.Data.Tag
            {
                Name = "ChildA1",
                ParentTag = rootA,
                IsSystem = false,
                OwnerId = "test-user-id"
            };
            childA2 = new SRNSMudApp.Data.Tag
            {
                Name = "ChildA2",
                ParentTag = rootA,
                IsSystem = false,
                OwnerId = "test-user-id"
            };
            rootB = new SRNSMudApp.Data.Tag { Name = "RootB", IsSystem = false, OwnerId = "test-user-id" };
            childB1 = new SRNSMudApp.Data.Tag
            {
                Name = "ChildB1",
                ParentTag = rootB,
                IsSystem = false,
                OwnerId = "test-user-id"
            };
            dbContext.Tags.AddRange(rootA, childA1, childA2, rootB, childB1);
            _ = await dbContext.SaveChangesAsync();
        }

        var json = await RenderAndGetTreeJson();

        Assert.NotNull(json);
        Assert.NotEqual("[]", json);
        // すべてのタグが出力に含まれている
        Assert.Contains($"\"id\":{rootA.Id}", json);
        Assert.Contains($"\"id\":{childA1.Id}", json);
        Assert.Contains($"\"id\":{childA2.Id}", json);
        Assert.Contains($"\"id\":{rootB.Id}", json);
        Assert.Contains($"\"id\":{childB1.Id}", json);
    }

    /// <summary>
    ///     JSON出力が空配列 "[]" でないことを検証する（タグ数によるカウント検証）
    /// </summary>
    [Fact]
    public async Task EmptySearch_JsonIsNotEmptyArray()
    {
        IDbContextFactory<ApplicationDbContext> dbFactory =
            _ctx.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        await using (ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync())
        {
            for (var i = 0; i < 10; i++)
            {
                _ = dbContext.Tags.Add(new SRNSMudApp.Data.Tag
                {
                    Name = $"CountTag{i}",
                    IsSystem = false,
                    OwnerId = "test-user-id"
                });
            }

            _ = await dbContext.SaveChangesAsync();
        }

        var json = await RenderAndGetTreeJson();

        Assert.NotNull(json);
        Assert.NotEqual("[]", json);

        // "id": が10回出現する
        var idCount = json.Split("\"id\":").Length - 1;
        Assert.Equal(10, idCount);
    }

    /// <summary>
    ///     3階層のツリーが空検索で正しく表示される
    /// </summary>
    [Fact]
    public async Task EmptySearch_DisplaysThreeLevelTree()
    {
        IDbContextFactory<ApplicationDbContext> dbFactory =
            _ctx.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        SRNSMudApp.Data.Tag grandparent;
        SRNSMudApp.Data.Tag parent;
        SRNSMudApp.Data.Tag child;
        await using (ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync())
        {
            grandparent = new SRNSMudApp.Data.Tag { Name = "Grandparent", IsSystem = false, OwnerId = "test-user-id" };
            parent = new SRNSMudApp.Data.Tag
            {
                Name = "Parent",
                ParentTag = grandparent,
                IsSystem = false,
                OwnerId = "test-user-id"
            };
            child = new SRNSMudApp.Data.Tag
            {
                Name = "Child",
                ParentTag = parent,
                IsSystem = false,
                OwnerId = "test-user-id"
            };
            dbContext.Tags.AddRange(grandparent, parent, child);
            _ = await dbContext.SaveChangesAsync();
        }

        var json = await RenderAndGetTreeJson();

        Assert.NotNull(json);
        Assert.NotEqual("[]", json);
        Assert.Contains($"\"id\":{grandparent.Id}", json);
        Assert.Contains($"\"id\":{parent.Id}", json);
        Assert.Contains($"\"id\":{child.Id}", json);
        // ネスト構造が保持されていることを確認
        Assert.Contains("\"children\":", json);
    }

    /// <summary>
    ///     システムタグを除外し、ユーザータグのみが表示される
    /// </summary>
    [Fact]
    public async Task EmptySearch_ExcludesSystemTagsButDisplaysUserTags()
    {
        IDbContextFactory<ApplicationDbContext> dbFactory =
            _ctx.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        SRNSMudApp.Data.Tag userTag;
        SRNSMudApp.Data.Tag systemTag;
        await using (ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync())
        {
            systemTag = new SRNSMudApp.Data.Tag { Name = "SystemOnly", IsSystem = true, OwnerId = "system" };
            userTag = new SRNSMudApp.Data.Tag { Name = "UserVisible", IsSystem = false, OwnerId = "test-user-id" };
            dbContext.Tags.AddRange(systemTag, userTag);
            _ = await dbContext.SaveChangesAsync();
        }

        var json = await RenderAndGetTreeJson();

        Assert.NotNull(json);
        Assert.NotEqual("[]", json);
        Assert.Contains($"\"id\":{userTag.Id}", json);
        Assert.DoesNotContain($"\"id\":{systemTag.Id}", json);
    }

    // ============================================================
    // E2Eテスト(TagTreeBugE2ETests)から移行した回帰テスト
    // ============================================================

    /// <summary>
    ///     単一ルートに複数の子がぶら下がるツリーが正しく描画される
    /// </summary>
    [Fact]
    public async Task JqTree_DisplaysCorrectly_WhenSingleRootNodeHasMultipleChildren()
    {
        IDbContextFactory<ApplicationDbContext> dbFactory =
            _ctx.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        SRNSMudApp.Data.Tag root;
        SRNSMudApp.Data.Tag child1;
        SRNSMudApp.Data.Tag child2;
        SRNSMudApp.Data.Tag child3;
        await using (ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync())
        {
            root = new SRNSMudApp.Data.Tag { Name = "BugRoot", IsSystem = false, OwnerId = "test-user-id" };
            child1 = new SRNSMudApp.Data.Tag
            {
                Name = "Child1",
                ParentTag = root,
                IsSystem = false,
                OwnerId = "test-user-id"
            };
            child2 = new SRNSMudApp.Data.Tag
            {
                Name = "Child2",
                ParentTag = root,
                IsSystem = false,
                OwnerId = "test-user-id"
            };
            child3 = new SRNSMudApp.Data.Tag
            {
                Name = "Child3",
                ParentTag = root,
                IsSystem = false,
                OwnerId = "test-user-id"
            };
            dbContext.Tags.AddRange(root, child1, child2, child3);
            _ = await dbContext.SaveChangesAsync();
        }

        var json = await RenderAndGetTreeJson();

        Assert.NotNull(json);
        Assert.Contains("\"name\":\"BugRoot\"", json);
        Assert.Contains($"\"id\":{root.Id}", json);
        Assert.Contains($"\"id\":{child1.Id}", json);
        Assert.Contains($"\"id\":{child2.Id}", json);
        Assert.Contains($"\"id\":{child3.Id}", json);
        // 親子構造（ネスト）が保持されている
        Assert.Contains("\"children\":", json);
    }

    /// <summary>
    ///     検索フィールドが空のとき全タグ(15件)が表示される
    /// </summary>
    [Fact]
    public async Task JqTree_DisplaysTags_WhenSearchFieldIsEmpty()
    {
        IDbContextFactory<ApplicationDbContext> dbFactory =
            _ctx.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        await using (ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync())
        {
            var tags = new List<SRNSMudApp.Data.Tag>();
            for (var i = 0; i < 15; i++)
            {
                tags.Add(new SRNSMudApp.Data.Tag
                {
                    Name = $"EmptySearchTag_{i}",
                    IsSystem = false,
                    OwnerId = "test-user-id"
                });
            }

            dbContext.Tags.AddRange(tags);
            _ = await dbContext.SaveChangesAsync();
        }

        var json = await RenderAndGetTreeJson();

        Assert.NotNull(json);
        Assert.NotEqual("[]", json);

        var idCount = json.Split("\"id\":").Length - 1;
        Assert.Equal(15, idCount);
        Assert.Contains("\"name\":\"EmptySearchTag_0\"", json);
        Assert.Contains("\"name\":\"EmptySearchTag_14\"", json);
    }

    // ============================================================
    // ヘルパーメソッド
    // ============================================================

    /// <summary>
    ///     TagTreeコンポーネントをレンダリングし、jqTreeInterop.init に渡されたJSON文字列を返す
    /// </summary>
    private Task<string?> RenderAndGetTreeJson()
    {
        var jsInteropInvocations = new List<JSRuntimeInvocation>();
        _ = _ctx.JSInterop.SetupVoid("jqTreeInterop.init", invocation =>
        {
            jsInteropInvocations.Add(invocation);
            return true;
        });

        // Render ではなく _ctx.Render<T>() に変更
        IRenderedComponent<TagTree> component = _ctx.Render<TagTree>();

        component.WaitForAssertion(() => Assert.NotEmpty(jsInteropInvocations));

        JSRuntimeInvocation invocation = jsInteropInvocations.First(i => i.Identifier == "jqTreeInterop.init");
        return Task.FromResult(invocation.Arguments[1] as string);
    }
}