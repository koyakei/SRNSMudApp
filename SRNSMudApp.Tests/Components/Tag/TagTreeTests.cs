#region

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

using Bunit;

using SRNSMudApp.Tests.TestSupport;
using Moq;
using Bunit.TestDoubles;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using MudBlazor.Services;

using SRNSMudApp.Components.Tag;
using SRNSMudApp.Data;
using Xunit;
using Xunit.Abstractions;

#endregion

namespace SRNSMudApp.Tests.Components.Tag;

// TestContextの継承をやめ、IAsyncDisposableを実装します
public class TagTreeTests : IAsyncDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly TestContext _ctx;

    public TagTreeTests(ITestOutputHelper output)
    {
        _output = output;
        _ctx = new TestContext();
        
        // 継承元のプロパティではなく、_ctx のプロパティを使用するように変更
        _ = _ctx.Services.AddMudServices().AddSrnsComponentServices();
        var claims = new[] { new Claim(ClaimTypes.Name, "test-user-id"), new Claim(ClaimTypes.NameIdentifier, "test-user-id") };
        var identity = new ClaimsIdentity(claims, "TestAuthType");
        var claimsPrincipal = new ClaimsPrincipal(identity);
        var authState = new Microsoft.AspNetCore.Components.Authorization.AuthenticationState(claimsPrincipal);

        var authMock = new Moq.Mock<Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider>();
        authMock.Setup(p => p.GetAuthenticationStateAsync()).ReturnsAsync(authState);
        _ctx.Services.AddScoped<Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider>(_ => authMock.Object);
        _ctx.Services.AddAuthorizationCore();

        var dbName = Guid.NewGuid().ToString();
        _ = _ctx.Services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(dbName), ServiceLifetime.Scoped, ServiceLifetime.Singleton);
        _ = _ctx.Services.AddDbContextFactory<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(dbName));

        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
    }

    // 非同期でTestContextを破棄し、MudBlazorの非同期サービスの例外を防ぐ
    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }

    [Fact]
    public async Task JqTree_InitializesWithCorrectJson_WhenSingleRootNodeHasMultipleChildren()
    {
        // Arrange
        IDbContextFactory<ApplicationDbContext> dbFactory =
            _ctx.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        await using (ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync())
        {
            var rootTag = new SRNSMudApp.Data.Tag { Id = 1, Name = "Root", IsSystem = false, OwnerId = "test-user-id" };
            var child1 = new SRNSMudApp.Data.Tag
            {
                Id = 2,
                Name = "Child1",
                ParentTagId = 1,
                IsSystem = false,
                OwnerId = "test-user-id"
            };
            var child2 = new SRNSMudApp.Data.Tag
            {
                Id = 3,
                Name = "Child2",
                ParentTagId = 1,
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
        Assert.Contains("\"id\":1", treeDataJson);
        Assert.Contains("\"children\":", treeDataJson);
        Assert.Contains("\"id\":2", treeDataJson);
        Assert.Contains("\"id\":3", treeDataJson);
    }

    [Fact]
    public async Task JqTree_DisplaysChildren_WhenRootNodeIsSystemTag()
    {
        // Arrange
        IDbContextFactory<ApplicationDbContext> dbFactory =
            _ctx.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        await using (ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync())
        {
            // System tag should be filtered out
            var rootTag = new SRNSMudApp.Data.Tag { Id = 10, Name = "SystemRoot", IsSystem = true, OwnerId = "system" };
            // Children are not system tags, so they should be displayed as root nodes
            var child1 = new SRNSMudApp.Data.Tag
            {
                Id = 11,
                Name = "UserChild1",
                ParentTagId = 10,
                IsSystem = false,
                OwnerId = "test-user-id"
            };
            var child2 = new SRNSMudApp.Data.Tag
            {
                Id = 12,
                Name = "UserChild2",
                ParentTagId = 10,
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
        Assert.Contains("\"id\":11", treeDataJson);
        Assert.Contains("\"id\":12", treeDataJson);
    }

    [Fact]
    public async Task JqTree_DisplaysCreatedTagTree_WhenSearchTextIsEmpty()
    {
        // Arrange
        IDbContextFactory<ApplicationDbContext> dbFactory =
            _ctx.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        await using (ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync())
        {
            // Seed a tree structure for the current user
            var rootTag =
                new SRNSMudApp.Data.Tag { Id = 20, Name = "MyRoot", IsSystem = false, OwnerId = "test-user-id" };
            var childTag = new SRNSMudApp.Data.Tag
            {
                Id = 21,
                Name = "MyChild",
                ParentTagId = 20,
                IsSystem = false,
                OwnerId = "test-user-id"
            };

            // Seed a tag for another user to ensure sorting/prioritization works (though we won't hit the 2000 limit)
            var otherUserTag = new SRNSMudApp.Data.Tag
            {
                Id = 22, Name = "OtherRoot", IsSystem = false, OwnerId = "other-user-id"
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
        Assert.Contains("\"id\":20", treeDataJson);
        Assert.Contains("\"name\":\"MyRoot\"", treeDataJson);
        Assert.Contains("\"children\":", treeDataJson);
        Assert.Contains("\"id\":21", treeDataJson);
        Assert.Contains("\"name\":\"MyChild\"", treeDataJson);

        // Also verify the other user's tag is included since we are under the 2000 limit
        Assert.Contains("\"id\":22", treeDataJson);
    }

    [Fact]
    public async Task JqTree_DoesNotCrash_WhenCircularReferenceExists()
    {
        // Arrange
        IDbContextFactory<ApplicationDbContext> dbFactory =
            _ctx.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        await using (ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync())
        {
            // Seed a circular reference tree structure
            var tag1 = new SRNSMudApp.Data.Tag
            {
                Id = 30,
                Name = "Tag1",
                ParentTagId = 32,
                IsSystem = false,
                OwnerId = "test-user-id"
            };
            var tag2 = new SRNSMudApp.Data.Tag
            {
                Id = 31,
                Name = "Tag2",
                ParentTagId = 30,
                IsSystem = false,
                OwnerId = "test-user-id"
            };
            var tag3 = new SRNSMudApp.Data.Tag
            {
                Id = 32,
                Name = "Tag3",
                ParentTagId = 31,
                IsSystem = false,
                OwnerId = "test-user-id"
            };

            // Tag1's parent is Tag3. Tag3's parent is Tag2. Tag2's parent is Tag1. Circular!
            dbContext.Tags.AddRange(tag1, tag2, tag3);
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

        // Assert
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
                    Id = 10000 + i, Name = $"Tag {i}", IsSystem = false, OwnerId = "test-user-id"
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
        await using (ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync())
        {
            var tag = new SRNSMudApp.Data.Tag
            {
                Id = 40,
                Name = "Orphan",
                ParentTagId = 9999,
                IsSystem = false,
                OwnerId = "test-user-id"
            };
            _ = dbContext.Tags.Add(tag);
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
        Assert.Contains("\"id\":40", treeDataJson);
    }

    [Fact]
    public async Task JqTree_DisplaysTag_WhenSelfReferencing()
    {
        IDbContextFactory<ApplicationDbContext> dbFactory =
            _ctx.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        await using (ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync())
        {
            var tag = new SRNSMudApp.Data.Tag
            {
                Id = 50,
                Name = "SelfRef",
                ParentTagId = 50,
                IsSystem = false,
                OwnerId = "test-user-id"
            };
            _ = dbContext.Tags.Add(tag);
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
        Assert.Contains("\"id\":50", treeDataJson);
    }

    [Fact]
    public async Task JqTree_DisplaysTags_WhenDeeplyNested()
    {
        IDbContextFactory<ApplicationDbContext> dbFactory =
            _ctx.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        await using (ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync())
        {
            var tags = new List<SRNSMudApp.Data.Tag>();
            // Max depth in JsonSerializer is 64 by default, we set it to 128. Let's try 130 to see if it fails.
            for (var i = 1; i <= 130; i++)
            {
                tags.Add(new SRNSMudApp.Data.Tag
                {
                    Id = 1000 + i,
                    Name = $"Deep{i}",
                    ParentTagId = i == 1 ? null : 1000 + i - 1,
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

        // This will FAIL if JsonException is thrown due to MaxDepth limit
        IRenderedComponent<TagTree> component = _ctx.Render<TagTree>();
        component.WaitForAssertion(() => Assert.NotEmpty(jsInteropInvocations));
        var treeDataJson = jsInteropInvocations.First(i => i.Identifier == "jqTreeInterop.init").Arguments[1] as string;

        Assert.Contains("\"id\":1130", treeDataJson);
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
        await using (ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync())
        {
            _ = dbContext.Tags.Add(new SRNSMudApp.Data.Tag
            {
                Id = 100, Name = "SoloTag", IsSystem = false, OwnerId = "test-user-id"
            });
            _ = await dbContext.SaveChangesAsync();
        }

        var json = await RenderAndGetTreeJson();

        Assert.NotNull(json);
        Assert.NotEqual("[]", json);
        Assert.Contains("\"id\":100", json);
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
        await using (ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync())
        {
            dbContext.Tags.AddRange(
                new SRNSMudApp.Data.Tag { Id = 200, Name = "FlatA", IsSystem = false, OwnerId = "test-user-id" },
                new SRNSMudApp.Data.Tag { Id = 201, Name = "FlatB", IsSystem = false, OwnerId = "test-user-id" },
                new SRNSMudApp.Data.Tag { Id = 202, Name = "FlatC", IsSystem = false, OwnerId = "test-user-id" }
            );
            _ = await dbContext.SaveChangesAsync();
        }

        var json = await RenderAndGetTreeJson();

        Assert.NotNull(json);
        Assert.NotEqual("[]", json);
        Assert.Contains("\"id\":200", json);
        Assert.Contains("\"id\":201", json);
        Assert.Contains("\"id\":202", json);
    }

    /// <summary>
    ///     フラットなタグとツリー構造のタグが混在する場合
    /// </summary>
    [Fact]
    public async Task EmptySearch_DisplaysMixedFlatAndTreeTags()
    {
        IDbContextFactory<ApplicationDbContext> dbFactory =
            _ctx.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        await using (ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync())
        {
            dbContext.Tags.AddRange(
                new SRNSMudApp.Data.Tag { Id = 300, Name = "FlatOnly", IsSystem = false, OwnerId = "test-user-id" },
                new SRNSMudApp.Data.Tag { Id = 301, Name = "TreeRoot", IsSystem = false, OwnerId = "test-user-id" },
                new SRNSMudApp.Data.Tag
                {
                    Id = 302,
                    Name = "TreeChild",
                    ParentTagId = 301,
                    IsSystem = false,
                    OwnerId = "test-user-id"
                }
            );
            _ = await dbContext.SaveChangesAsync();
        }

        var json = await RenderAndGetTreeJson();

        Assert.NotNull(json);
        Assert.NotEqual("[]", json);
        Assert.Contains("\"id\":300", json);
        Assert.Contains("\"id\":301", json);
        Assert.Contains("\"id\":302", json);
    }

    /// <summary>
    ///     他ユーザーのタグのみが存在する場合でも表示される
    /// </summary>
    [Fact]
    public async Task EmptySearch_DisplaysOtherUserTagsWhenNoOwnTags()
    {
        IDbContextFactory<ApplicationDbContext> dbFactory =
            _ctx.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        await using (ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync())
        {
            dbContext.Tags.AddRange(
                new SRNSMudApp.Data.Tag { Id = 400, Name = "OtherUserTag1", IsSystem = false, OwnerId = "other-user" },
                new SRNSMudApp.Data.Tag { Id = 401, Name = "OtherUserTag2", IsSystem = false, OwnerId = "other-user" }
            );
            _ = await dbContext.SaveChangesAsync();
        }

        var json = await RenderAndGetTreeJson();

        Assert.NotNull(json);
        Assert.NotEqual("[]", json);
        Assert.Contains("\"id\":400", json);
        Assert.Contains("\"id\":401", json);
    }

    /// <summary>
    ///     自分のタグと他ユーザーのタグが混在する場合、両方表示される
    /// </summary>
    [Fact]
    public async Task EmptySearch_DisplaysBothOwnAndOtherUserTags()
    {
        IDbContextFactory<ApplicationDbContext> dbFactory =
            _ctx.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        await using (ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync())
        {
            dbContext.Tags.AddRange(
                new SRNSMudApp.Data.Tag { Id = 500, Name = "MyTag", IsSystem = false, OwnerId = "test-user-id" },
                new SRNSMudApp.Data.Tag { Id = 501, Name = "TheirTag", IsSystem = false, OwnerId = "someone-else" }
            );
            _ = await dbContext.SaveChangesAsync();
        }

        var json = await RenderAndGetTreeJson();

        Assert.NotNull(json);
        Assert.NotEqual("[]", json);
        Assert.Contains("\"id\":500", json);
        Assert.Contains("\"id\":501", json);
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
                Id = 600, Name = "LonelyRoot", IsSystem = false, OwnerId = "test-user-id"
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
        await using (ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync())
        {
            dbContext.Tags.AddRange(
                new SRNSMudApp.Data.Tag { Id = 700, Name = "RootA", IsSystem = false, OwnerId = "test-user-id" },
                new SRNSMudApp.Data.Tag
                {
                    Id = 701,
                    Name = "ChildA1",
                    ParentTagId = 700,
                    IsSystem = false,
                    OwnerId = "test-user-id"
                },
                new SRNSMudApp.Data.Tag
                {
                    Id = 702,
                    Name = "ChildA2",
                    ParentTagId = 700,
                    IsSystem = false,
                    OwnerId = "test-user-id"
                },
                new SRNSMudApp.Data.Tag { Id = 710, Name = "RootB", IsSystem = false, OwnerId = "test-user-id" },
                new SRNSMudApp.Data.Tag
                {
                    Id = 711,
                    Name = "ChildB1",
                    ParentTagId = 710,
                    IsSystem = false,
                    OwnerId = "test-user-id"
                }
            );
            _ = await dbContext.SaveChangesAsync();
        }

        var json = await RenderAndGetTreeJson();

        Assert.NotNull(json);
        Assert.NotEqual("[]", json);
        // すべてのタグが出力に含まれている
        Assert.Contains("\"id\":700", json);
        Assert.Contains("\"id\":701", json);
        Assert.Contains("\"id\":702", json);
        Assert.Contains("\"id\":710", json);
        Assert.Contains("\"id\":711", json);
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
                    Id = 800 + i, Name = $"CountTag{i}", IsSystem = false, OwnerId = "test-user-id"
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
        await using (ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync())
        {
            dbContext.Tags.AddRange(
                new SRNSMudApp.Data.Tag { Id = 900, Name = "Grandparent", IsSystem = false, OwnerId = "test-user-id" },
                new SRNSMudApp.Data.Tag
                {
                    Id = 901,
                    Name = "Parent",
                    ParentTagId = 900,
                    IsSystem = false,
                    OwnerId = "test-user-id"
                },
                new SRNSMudApp.Data.Tag
                {
                    Id = 902,
                    Name = "Child",
                    ParentTagId = 901,
                    IsSystem = false,
                    OwnerId = "test-user-id"
                }
            );
            _ = await dbContext.SaveChangesAsync();
        }

        var json = await RenderAndGetTreeJson();

        Assert.NotNull(json);
        Assert.NotEqual("[]", json);
        Assert.Contains("\"id\":900", json);
        Assert.Contains("\"id\":901", json);
        Assert.Contains("\"id\":902", json);
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
        await using (ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync())
        {
            dbContext.Tags.AddRange(
                new SRNSMudApp.Data.Tag { Id = 950, Name = "SystemOnly", IsSystem = true, OwnerId = "system" },
                new SRNSMudApp.Data.Tag { Id = 951, Name = "UserVisible", IsSystem = false, OwnerId = "test-user-id" }
            );
            _ = await dbContext.SaveChangesAsync();
        }

        var json = await RenderAndGetTreeJson();

        Assert.NotNull(json);
        Assert.NotEqual("[]", json);
        Assert.Contains("\"id\":951", json);
        Assert.DoesNotContain("\"id\":950", json);
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
        await using (ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync())
        {
            dbContext.Tags.AddRange(
                new SRNSMudApp.Data.Tag { Id = 2100, Name = "BugRoot", IsSystem = false, OwnerId = "test-user-id" },
                new SRNSMudApp.Data.Tag
                {
                    Id = 2101,
                    Name = "Child1",
                    ParentTagId = 2100,
                    IsSystem = false,
                    OwnerId = "test-user-id"
                },
                new SRNSMudApp.Data.Tag
                {
                    Id = 2102,
                    Name = "Child2",
                    ParentTagId = 2100,
                    IsSystem = false,
                    OwnerId = "test-user-id"
                },
                new SRNSMudApp.Data.Tag
                {
                    Id = 2103,
                    Name = "Child3",
                    ParentTagId = 2100,
                    IsSystem = false,
                    OwnerId = "test-user-id"
                }
            );
            _ = await dbContext.SaveChangesAsync();
        }

        var json = await RenderAndGetTreeJson();

        Assert.NotNull(json);
        Assert.Contains("\"name\":\"BugRoot\"", json);
        Assert.Contains("\"id\":2100", json);
        Assert.Contains("\"id\":2101", json);
        Assert.Contains("\"id\":2102", json);
        Assert.Contains("\"id\":2103", json);
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
                    Id = 2200 + i, Name = $"EmptySearchTag_{i}", IsSystem = false, OwnerId = "test-user-id"
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