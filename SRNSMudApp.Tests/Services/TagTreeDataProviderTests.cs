#region

using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Services;
using SRNSMudApp.Tests.TestSupport;

#endregion

namespace SRNSMudApp.Tests.Services;

/// <summary>
///     TagTreeDataProvider.LoadTagsAsync の IsSystem 除外フィルタに対する統合テスト。
///     元 TagTreeTests.cs (bUnit コンポーネントレンダリング＋JS interop モック経由) で
///     検証していたテストのうち、実DBのフィルタ条件（!IsSystem）に依存する2件をここに移行した。
///     bUnitレンダリング・JS interopモックを排除し、TagTreeDataProvider を直接呼び出すことで
///     高速化する。アサーション内容は元のテストと同一。
/// </summary>
[Collection(MsSqlCollection.Name)]
public class TagTreeDataProviderTests(MsSqlContainerFixture fixture) : IAsyncLifetime
{
    private MsSqlTestDatabase _testDb = null!;
    private ApplicationDbContext _context = null!;
    private TagTreeDataProvider _provider = null!;

    public async Task InitializeAsync()
    {
        _testDb = await MsSqlTestDatabase.CreateAsync(fixture.ConnectionString, nameof(TagTreeDataProviderTests));
        DbContextOptions<ApplicationDbContext> options = _testDb.Options;

        _context = new ApplicationDbContext(options);
        _provider = new TagTreeDataProvider(new SingleContextDbFactory(options));

        _context.Users.AddRange(
            new ApplicationUser { Id = "test-user-id", UserName = "test-user-id" },
            new ApplicationUser { Id = "system", UserName = "system" }
        );
        _ = await _context.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _testDb.DisposeAsync();
    }

    /// <summary>元テスト: JqTree_DisplaysChildren_WhenRootNodeIsSystemTag（DBフィルタ部分）</summary>
    [Fact]
    public async Task LoadTagsAsync_WhenRootIsSystemTag_ExcludesSystemRootButKeepsChildren()
    {
        var rootTag = new Tag { Name = "SystemRoot", IsSystem = true, OwnerId = "system" };
        _context.Tags.Add(rootTag);
        _ = await _context.SaveChangesAsync();

        var child1 = new Tag
        {
            Name = "UserChild1",
            ParentTagId = rootTag.Id,
            IsSystem = false,
            OwnerId = "test-user-id"
        };
        var child2 = new Tag
        {
            Name = "UserChild2",
            ParentTagId = rootTag.Id,
            IsSystem = false,
            OwnerId = "test-user-id"
        };
        _context.Tags.AddRange(child1, child2);
        _ = await _context.SaveChangesAsync();

        List<Tag> tags = await _provider.LoadTagsAsync();

        Assert.DoesNotContain(tags, t => t.Id == rootTag.Id);
        Assert.Contains(tags, t => t.Id == child1.Id);
        Assert.Contains(tags, t => t.Id == child2.Id);
    }

    /// <summary>元テスト: EmptySearch_ExcludesSystemTagsButDisplaysUserTags</summary>
    [Fact]
    public async Task LoadTagsAsync_ExcludesSystemTagsButKeepsUserTags()
    {
        var systemTag = new Tag { Name = "SystemOnly", IsSystem = true, OwnerId = "system" };
        var userTag = new Tag { Name = "UserVisible", IsSystem = false, OwnerId = "test-user-id" };
        _context.Tags.AddRange(systemTag, userTag);
        _ = await _context.SaveChangesAsync();

        List<Tag> tags = await _provider.LoadTagsAsync();

        Assert.Contains(tags, t => t.Id == userTag.Id);
        Assert.DoesNotContain(tags, t => t.Id == systemTag.Id);
    }

    /// <summary>単一の DbContextOptions を使い回し、呼び出しごとに新しい DbContext を生成するテスト専用ファクトリ。</summary>
    private sealed class SingleContextDbFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);

        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ApplicationDbContext(options));
    }
}
