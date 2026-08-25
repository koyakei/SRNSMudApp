using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Bunit;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using SRNSMudApp.Components.Tag;
using SRNSMudApp.Data;
using SRNSMudApp.Tests.TestSupport;

using Xunit;
using Xunit.Abstractions;

namespace SRNSMudApp.Tests.Components.Tag;

/// <summary>
///     TagTree コンポーネントのテスト。
/// </summary>
/// <remarks>
///     このクラスは元々20件のテストを持っていたが、純粋ロジック部分は
///     <see cref="SRNSMudApp.Tests.Components.Tag.TagTreeViewModelTests" /> に、
///     IsSystem除外のDB依存部分は
///     <see cref="SRNSMudApp.Tests.Services.TagTreeDataProviderTests" /> に移行した。
///     ここにはコンポーネントの実際の描画配線（OnAfterRenderAsync → jqTreeInterop.init 呼び出し）を
///     検証するスモークテストのみを残す。
/// </remarks>
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
        _ = await dbContext.SaveChangesAsync();
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
            _ = dbContext.Tags.Add(rootTag);
            _ = await dbContext.SaveChangesAsync();

            child1 = new SRNSMudApp.Data.Tag
            {
                Name = "Child1",
                ParentTagId = rootTag.Id,
                IsSystem = false,
                OwnerId = "test-user-id"
            };
            child2 = new SRNSMudApp.Data.Tag
            {
                Name = "Child2",
                ParentTagId = rootTag.Id,
                IsSystem = false,
                OwnerId = "test-user-id"
            };

            dbContext.Tags.AddRange(child1, child2);
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
}