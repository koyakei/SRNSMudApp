#region

using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Services;

#endregion

namespace SRNSMudApp.Tests.Services;

/// <summary>
/// <see cref="TagDetailDataProvider" /> のタグ履歴取得ロジックの単体テスト (MSSQL Testcontainers)。
/// </summary>
public class TagDetailDataProviderTests : IAsyncLifetime
{
    private MsSqlTestDatabase _sharedDb = null!;

    public async Task InitializeAsync()
    {
        _sharedDb = await SharedMsSqlTestDatabase.GetInstanceAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(ApplicationDbContext db, TagDetailDataProvider sut, ItemTagService itemTagService, string userId, int parentTagId, int childTagId, string tid)> CreateScopeAsync()
    {
        var tid = Guid.NewGuid().ToString("N")[..8];
        var db = new ApplicationDbContext(_sharedDb.Options);
        var stubFactory = new DbContextFactoryStub(_sharedDb.Options);
        var sut = new TagDetailDataProvider(stubFactory);
        var itemTagService = new ItemTagService(stubFactory);

        var userId = $"user_{tid}";
        await db.SeedUsersAsync(userId);

        var parentTag = new Tag { Name = $"ParentTag_{tid}", IsSystem = true, OwnerId = userId, CachedWeight = 0 };
        var childTag = new Tag { Name = $"ChildTag_{tid}", IsSystem = true, OwnerId = userId, CachedWeight = 5 };

        db.Tags.AddRange(parentTag, childTag);
        await db.SaveChangesAsync();

        return (db, sut, itemTagService, userId, parentTag.Id, childTag.Id, tid);
    }

    [Fact]
    public async Task GetTagDetailAsync_WhenTagRelationToTagIsDeleted_StillReturnsTagWeightLedgers()
    {
        var (db, sut, itemTagService, userId, parentTagId, childTagId, tid) = await CreateScopeAsync();
        await using (db)
        {
            // 1. タグにタグを関連付け (Ledger 1件生成: TagRelationToTagInsert)
            var addError = await itemTagService.AddTagToTagAsync(parentTagId, childTagId, userId);
            Assert.Null(addError);

            TagRelationToTag relation = await db.TagRelationToTags.SingleAsync(tr => tr.TargetTagId == parentTagId && tr.TagId == childTagId);

            // 2. タグ同士の関連付けを削除 (Ledger 2件目生成: TagRelationToTagDelete & TagRelationToTag 削除)
            var removeError = await itemTagService.RemoveTagToTagRelationAsync(relation.Id, userId);
            Assert.Null(removeError);

            // TagRelationToTag が削除されていることを確認
            Assert.False(await db.TagRelationToTags.AnyAsync(tr => tr.Id == relation.Id));

            // 3. 親タグの TagDetailDataProvider から詳細を取得
            TagDetailPageData parentResult = await sut.GetTagDetailAsync(parentTagId, userId);

            Assert.NotNull(parentResult.Tag);
            // 親タグ (TargetTagId) に紐づく履歴 (Insert, Delete) が保持されていること
            Assert.Equal(2, parentResult.WeightLedgers.Count);
            Assert.All(parentResult.WeightLedgers, l => Assert.Equal(parentTagId, l.TargetTagId));
            Assert.All(parentResult.WeightLedgers, l => Assert.Equal(childTagId, l.TagId));
            Assert.All(parentResult.WeightLedgers, l => Assert.Equal($"ChildTag_{tid}", l.TagNameSnapshot));

            // 4. 子タグ自身の TagDetailDataProvider から詳細を取得
            TagDetailPageData childResult = await sut.GetTagDetailAsync(childTagId, userId);

            Assert.NotNull(childResult.Tag);
            // 子タグ (TagId) に紐づく履歴 (Insert, Delete) も取得できること
            Assert.Equal(2, childResult.WeightLedgers.Count);
            Assert.All(childResult.WeightLedgers, l => Assert.Equal(childTagId, l.TagId));
        }
    }

    private sealed class DbContextFactoryStub(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);

        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }
}