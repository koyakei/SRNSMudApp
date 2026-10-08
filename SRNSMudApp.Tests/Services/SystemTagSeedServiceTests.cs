#region

using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Services;
using SRNSMudApp.Tests.TestSupport;

#endregion

namespace SRNSMudApp.Tests.Services;

/// <summary>
///     <see cref="SystemTagSeedService" /> のシステムタグシード処理に関する単体テスト。
///     テスト用 SQL Server（SharedMsSqlTestDatabase）を使用し、独立したユーザー ID 名前空間でシードと冪等性を検証する。
/// </summary>
public class SystemTagSeedServiceTests : IAsyncLifetime
{
    private MsSqlTestDatabase _sharedDb = null!;

    public async Task InitializeAsync()
    {
        _sharedDb = await SharedMsSqlTestDatabase.GetInstanceAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private ApplicationDbContext CreateDbContext() => new(_sharedDb.Options);

    [Fact]
    public async Task SeedSystemTagsAsync_WhenAlreadySeeded_ReturnsZeroImmediately()
    {
        // Arrange
        await using var db = CreateDbContext();
        var tid = Guid.NewGuid().ToString("N")[..8];
        var systemUserId = $"sys_seeded_{tid}";

        // 外部キー制約を満たすためにユーザーを登録
        await db.SeedUsersAsync(systemUserId);

        // 事前に1件システムタグを作成
        var existingTag = new Tag
        {
            Name = $"ExistingTag_{tid}",
            OwnerId = systemUserId,
            IsSystem = true,
            Node = HierarchyId.Parse($"/999{Math.Abs(tid.GetHashCode()) % 1000}/"),
            CreatedDate = DateTime.UtcNow,
            UpdatedDate = DateTime.UtcNow
        };
        db.Tags.Add(existingTag);
        await db.SaveChangesAsync();

        try
        {
            // Act
            int inserted = await SystemTagSeedService.SeedSystemTagsAsync(db, systemUserId);

            // Assert: 既に存在するのでシードはスキップされ 0 が返る
            Assert.Equal(0, inserted);
        }
        finally
        {
            try
            {
                db.Tags.Remove(existingTag);
                await db.SaveChangesAsync();
            }
            finally
            {
                var user = await db.Users.FindAsync(systemUserId);
                if (user is not null)
                {
                    db.Users.Remove(user);
                    await db.SaveChangesAsync();
                }
            }
        }
    }

    [Fact]
    public async Task SeedSystemTagsAsync_WhenNotSeeded_InsertsAllSystemTagsWithCorrectHierarchy()
    {
        // Arrange
        await using var db = CreateDbContext();
        var tid = Guid.NewGuid().ToString("N")[..8];
        var systemUserId = $"sys_seed_{tid}";

        // 外部キー制約を満たすためにユーザーを登録
        await db.SeedUsersAsync(systemUserId);

        try
        {
            // Act 1: 1回目のシード実行
            int insertedCount = await SystemTagSeedService.SeedSystemTagsAsync(db, systemUserId);

            // Assert: 1,469件のタグが正常に投入されたこと
            Assert.Equal(1469, insertedCount);

            int seededCount = await db.Tags.CountAsync(t => t.OwnerId == systemUserId && t.IsSystem);
            Assert.Equal(1469, seededCount);

            // 代表的なタグ（総記、哲学、社会科学など）が存在し、親タグがルートタグ（全て∀）に設定されていること
            Tag rootTag = await db.Tags.FirstAsync(t => t.Name == Tag.RootTagName);
            Tag? soukiTag = await db.Tags.FirstOrDefaultAsync(t => t.OwnerId == systemUserId && t.Name == "総記");
            Assert.NotNull(soukiTag);
            Assert.Equal(rootTag.Id, soukiTag.ParentTagId);
            Assert.Equal("/7/", soukiTag.Node.ToString());

            // 子タグ（例: 総記配下のタグ /7/1/）の ParentTagId が「総記」の ID に正しく設定されていること
            Tag? childTag = await db.Tags.FirstOrDefaultAsync(t => t.OwnerId == systemUserId && t.Node == HierarchyId.Parse("/7/1/"));
            if (childTag is not null)
            {
                Assert.Equal(soukiTag.Id, childTag.ParentTagId);
            }

            // Act 2: 2回目のシード実行（冪等性の検証）
            int secondRunCount = await SystemTagSeedService.SeedSystemTagsAsync(db, systemUserId);

            // Assert: 重複して登録されず 0 が返ること
            Assert.Equal(0, secondRunCount);

            int countAfterSecondRun = await db.Tags.CountAsync(t => t.OwnerId == systemUserId && t.IsSystem);
            Assert.Equal(1469, countAfterSecondRun);
        }
        finally
        {
            // 後始末: テスト用にシードしたタグとユーザーを一括削除
            try
            {
                var createdTags = await db.Tags.Where(t => t.OwnerId == systemUserId).ToListAsync();
                if (createdTags.Count > 0)
                {
                    db.Tags.RemoveRange(createdTags);
                    await db.SaveChangesAsync();
                }
            }
            finally
            {
                var user = await db.Users.FindAsync(systemUserId);
                if (user is not null)
                {
                    db.Users.Remove(user);
                    await db.SaveChangesAsync();
                }
            }
        }
    }
}