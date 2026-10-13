#region

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;
using SRNSMudApp.Tests.TestSupport;

#endregion

namespace SRNSMudApp.Tests.Services;

/// <summary>
///     <see cref="HomeDataProvider" /> のタイムライン取得（フォロータグ + 自身の投稿）の結合テスト (MSSQL Testcontainers)。
/// </summary>
public class HomeDataProviderTests : IAsyncLifetime
{
    private MsSqlTestDatabase _sharedDb = null!;

    public async Task InitializeAsync()
    {
        _sharedDb = await SharedMsSqlTestDatabase.GetInstanceAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(ApplicationDbContext db, HomeDataProvider sut, string userId, string tid)> CreateScopeAsync()
    {
        var tid = Guid.NewGuid().ToString("N")[..8];
        var db = new ApplicationDbContext(_sharedDb.Options);
        var sut = new HomeDataProvider(new DbContextFactoryStub(_sharedDb.Options));

        var userId = $"user_{tid}";
        await db.SeedUsersAsync(userId);

        return (db, sut, userId, tid);
    }

    [Fact]
    public async Task LoadTimelineAsync_WhenUserHasOwnPostsAndNoFollowedTags_ReturnsOwnPostsOrderedByCreatedDateDesc()
    {
        var (db, sut, userId, tid) = await CreateScopeAsync();
        await using (db)
        {
            var itemOld = new Item
            {
                Content = $"Old post {tid}",
                OwnerId = userId
            };
            var itemNew = new Item
            {
                Content = $"New post {tid}",
                OwnerId = userId
            };
            db.Items.AddRange(itemOld, itemNew);
            await db.SaveChangesAsync();

            // Interceptor をバイパスして CreatedDate に差分を設定
            var oldDate = DateTime.UtcNow.AddMinutes(-30);
            var newDate = DateTime.UtcNow.AddMinutes(-5);
            await db.Items.Where(i => i.Id == itemOld.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.CreatedDate, oldDate));
            await db.Items.Where(i => i.Id == itemNew.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.CreatedDate, newDate));

            // Act: フォロータグ0件でも自分の投稿が流れること
            var page = await sut.LoadTimelineAsync([], 0, 10, userId);

            // Assert
            Assert.Equal(2, page.TotalCount);
            Assert.Equal(2, page.Groups.Count);

            // 新しい投稿が先頭
            Assert.Equal(itemNew.Id, page.Groups[0].Item?.Id);
            Assert.Equal(itemOld.Id, page.Groups[1].Item?.Id);
            Assert.Empty(page.Groups[0].Events);
            Assert.Empty(page.Groups[1].Events);
        }
    }

    [Fact]
    public async Task LoadTimelineAsync_WhenUserHasFollowedTagsAndOwnPosts_MergesAndOrdersByLatestEventDate()
    {
        var (db, sut, userId, tid) = await CreateScopeAsync();
        await using (db)
        {
            var otherUserId = $"other_{tid}";
            await db.SeedUsersAsync(otherUserId);

            var tag = new Tag { Name = $"tag_{tid}", OwnerId = otherUserId };
            db.Tags.Add(tag);
            await db.SaveChangesAsync();

            // 他人の投稿（フォロータグのイベントあり）: イベント日時 10分前
            var otherItem = new Item
            {
                Content = $"Other item {tid}",
                OwnerId = otherUserId
            };
            db.Items.Add(otherItem);
            await db.SaveChangesAsync();

            var tagEvent = new TimelineEvent
            {
                OwnerId = otherUserId,
                Target = new ItemTarget(otherItem.Id),
                FollowedTagId = tag.Id,
                EventType = "Insert"
            };
            db.TimelineEvents.Add(tagEvent);
            await db.SaveChangesAsync();

            // 自分の投稿: 5分前（より新しい）
            var myItem = new Item
            {
                Content = $"My own item {tid}",
                OwnerId = userId
            };
            db.Items.Add(myItem);
            await db.SaveChangesAsync();

            // 日時を調整
            var otherItemDate = DateTime.UtcNow.AddMinutes(-20);
            var tagEventDate = DateTime.UtcNow.AddMinutes(-10);
            var myItemDate = DateTime.UtcNow.AddMinutes(-5);

            await db.Items.Where(i => i.Id == otherItem.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.CreatedDate, otherItemDate));
            await db.TimelineEvents.Where(e => e.Id == tagEvent.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.CreatedDate, tagEventDate));
            await db.Items.Where(i => i.Id == myItem.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.CreatedDate, myItemDate));

            // Act
            var page = await sut.LoadTimelineAsync([tag.Id], 0, 10, userId);

            // Assert: 自分の投稿(5分前) -> 他人の投稿(10分前)の順でマージされること
            Assert.Equal(2, page.TotalCount);
            Assert.Equal(myItem.Id, page.Groups[0].Item?.Id);
            Assert.Equal(otherItem.Id, page.Groups[1].Item?.Id);
            Assert.Single(page.Groups[1].Events);
        }
    }

    [Fact]
    public async Task LoadTimelineAsync_WhenOwnPostAlsoHasFollowedTagEvent_ReturnsSingleGroupWithLatestDateAndEvents()
    {
        var (db, sut, userId, tid) = await CreateScopeAsync();
        await using (db)
        {
            var otherUserId = $"other_{tid}";
            await db.SeedUsersAsync(otherUserId);

            var tag = new Tag { Name = $"tag_{tid}", OwnerId = otherUserId };
            db.Tags.Add(tag);
            await db.SaveChangesAsync();

            // 自分の投稿: 30分前
            var myItem = new Item
            {
                Content = $"My tagged item {tid}",
                OwnerId = userId
            };
            db.Items.Add(myItem);
            await db.SaveChangesAsync();

            // 他人がその投稿にフォロータグを付けたイベント: 5分前
            var tagEvent = new TimelineEvent
            {
                OwnerId = otherUserId,
                Target = new ItemTarget(myItem.Id),
                FollowedTagId = tag.Id,
                EventType = "Insert"
            };
            db.TimelineEvents.Add(tagEvent);
            await db.SaveChangesAsync();

            var myItemDate = DateTime.UtcNow.AddMinutes(-30);
            var tagEventDate = DateTime.UtcNow.AddMinutes(-5);
            await db.Items.Where(i => i.Id == myItem.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.CreatedDate, myItemDate));
            await db.TimelineEvents.Where(e => e.Id == tagEvent.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.CreatedDate, tagEventDate));

            // Act
            var page = await sut.LoadTimelineAsync([tag.Id], 0, 10, userId);

            // Assert: 重複せず1件のみ、最新日時はタグイベントの日時（5分前）
            Assert.Equal(1, page.TotalCount);
            Assert.Single(page.Groups);
            Assert.Equal(myItem.Id, page.Groups[0].Item?.Id);
            Assert.Equal(tagEventDate.ToString("yyyyMMddHHmmss"), page.Groups[0].LatestEventDate.ToString("yyyyMMddHHmmss"));
            Assert.Single(page.Groups[0].Events);
        }
    }

    [Fact]
    public async Task LoadTimelineAsync_WhenAdminHidden_FiltersOutHiddenPostsForNonAdmin()
    {
        var (db, sut, userId, tid) = await CreateScopeAsync();
        await using (db)
        {
            var normalItem = new Item
            {
                Content = $"Normal item {tid}",
                OwnerId = userId
            };
            var hiddenItem = new Item
            {
                Content = $"Hidden item {tid}",
                OwnerId = userId,
                IsAdminHidden = true
            };
            db.Items.AddRange(normalItem, hiddenItem);
            await db.SaveChangesAsync();

            // Act: 非管理者ユーザーでタイムライン読み込み
            var page = await sut.LoadTimelineAsync([], 0, 10, userId);

            // Assert: IsAdminHidden の投稿は除外されること
            Assert.Equal(1, page.TotalCount);
            Assert.Single(page.Groups);
            Assert.Equal(normalItem.Id, page.Groups[0].Item?.Id);
        }
    }

    [Fact]
    public async Task LoadTimelineAsync_Pagination_PagesCorrectly()
    {
        var (db, sut, userId, tid) = await CreateScopeAsync();
        await using (db)
        {
            var createdItems = new List<Item>();
            for (var i = 1; i <= 5; i++)
            {
                var item = new Item
                {
                    Content = $"Item {i} {tid}",
                    OwnerId = userId
                };
                db.Items.Add(item);
                createdItems.Add(item);
            }
            await db.SaveChangesAsync();

            for (var i = 0; i < createdItems.Count; i++)
            {
                var item = createdItems[i];
                var date = DateTime.UtcNow.AddMinutes(-(i + 1));
                await db.Items.Where(it => it.Id == item.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(it => it.CreatedDate, date));
            }

            // Act: ページサイズ 2 で取得
            var page1 = await sut.LoadTimelineAsync([], 0, 2, userId);
            var page2 = await sut.LoadTimelineAsync([], 2, 2, userId);
            var page3 = await sut.LoadTimelineAsync([], 4, 2, userId);

            // Assert
            Assert.Equal(5, page1.TotalCount);
            Assert.Equal(2, page1.Groups.Count);
            Assert.Equal(2, page2.Groups.Count);
            Assert.Single(page3.Groups);

            // 重複がないこと
            var allIds = page1.Groups.Concat(page2.Groups).Concat(page3.Groups).Select(g => g.Item?.Id).ToList();
            Assert.Equal(5, allIds.Distinct().Count());
        }
    }

    /// <summary>
    /// HomeDataProvider.GetTagsAndRelationsAsync (全タグおよびリレーション一括取得) 実行時に
    /// SQL Server のリソースプールメモリ枯渇 (SqlException 701) が発生するシナリオを検証するテスト。
    /// </summary>
    [Fact]
    public async Task GetTagsAndRelationsAsync_WhenInsufficientSystemMemoryInResourcePool_ThrowsSqlException()
    {
        // Arrange
        var (db, _, _, _) = await CreateScopeAsync();
        await using (db)
        {
            var interceptor = new SqlMemoryExhaustionInterceptor();
            var options = SqlMemoryExhaustionInterceptor.CreateOptions(_sharedDb.ConnectionString, interceptor);
            var sut = new HomeDataProvider(new DbContextFactoryStub(options));

            // Act & Assert
            var ex = await Assert.ThrowsAsync<SqlException>(() =>
                sut.GetTagsAndRelationsAsync());

            Assert.Equal(701, ex.Number);
            Assert.Contains("There is insufficient system memory in resource pool 'default' to run this query.", ex.Message);
        }
    }

    /// <summary>
    /// HomeDataProvider.LoadTimelineAsync (タイムラインフィードクエリ) 実行時に
    /// SQL Server のリソースプールメモリ枯渇 (SqlException 701) が発生するシナリオを検証するテスト。
    /// </summary>
    [Fact]
    public async Task LoadTimelineAsync_WhenInsufficientSystemMemoryInResourcePool_ThrowsSqlException()
    {
        // Arrange
        var (db, _, userId, _) = await CreateScopeAsync();
        await using (db)
        {
            var interceptor = new SqlMemoryExhaustionInterceptor();
            var options = SqlMemoryExhaustionInterceptor.CreateOptions(_sharedDb.ConnectionString, interceptor);
            var sut = new HomeDataProvider(new DbContextFactoryStub(options));

            // Act & Assert
            var ex = await Assert.ThrowsAsync<SqlException>(() =>
                sut.LoadTimelineAsync([], 0, 10, userId));

            Assert.Equal(701, ex.Number);
            Assert.Contains("There is insufficient system memory in resource pool 'default' to run this query.", ex.Message);
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