#region

using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Services;

#endregion

namespace SRNSMudApp.Tests.Services;

/// <summary>
/// <see cref="ItemDetailDataProvider" /> のタグ履歴取得ロジックの単体テスト (MSSQL Testcontainers)。
/// </summary>
public class ItemDetailDataProviderTests : IAsyncLifetime
{
    private MsSqlTestDatabase _sharedDb = null!;

    public async Task InitializeAsync()
    {
        _sharedDb = await SharedMsSqlTestDatabase.GetInstanceAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(ApplicationDbContext db, ItemDetailDataProvider sut, ItemTagService itemTagService, string userId, int tagId, int itemId, string tid)> CreateScopeAsync()
    {
        var tid = Guid.NewGuid().ToString("N")[..8];
        var db = new ApplicationDbContext(_sharedDb.Options);
        var stubFactory = new DbContextFactoryStub(_sharedDb.Options);
        var sut = new ItemDetailDataProvider(stubFactory);
        var itemTagService = new ItemTagService(stubFactory);

        var userId = $"user_{tid}";
        var authorId = $"author_{tid}";

        await db.SeedUsersAsync(userId, authorId);

        var tag = new Tag { Name = $"Tag_{tid}", IsSystem = true, OwnerId = userId, CachedWeight = 0 };
        var item = new SRNSMudApp.Data.Item { Content = $"item_{tid}", OwnerId = authorId };

        db.Tags.Add(tag);
        db.Items.Add(item);
        await db.SaveChangesAsync();

        return (db, sut, itemTagService, userId, tag.Id, item.Id, tid);
    }

    [Fact]
    public async Task GetItemDetailAsync_WhenTagRelationIsDeleted_StillReturnsTagWeightLedgers()
    {
        var (db, sut, itemTagService, userId, tagId, itemId, tid) = await CreateScopeAsync();
        await using (db)
        {
            // 1. タグをアイテムに追加 (Ledger 1件生成)
            var addError = await itemTagService.AddTagToItemAsync(itemId, tagId, userId);
            Assert.Null(addError);

            TagRelation relation = await db.TagRelations.SingleAsync(tr => tr.ItemId == itemId && tr.TagId == tagId);

            // 2. ウェイトを変更 (Ledger 2件目生成)
            var updateResult = await itemTagService.UpdateTagWeightAsync(relation.Id, 2, userId);
            Assert.Equal(UpdateWeightResult.Success, updateResult);

            // 3. タグの関連付けを削除 (Ledger 3件目生成 & TagRelation 削除)
            var removeError = await itemTagService.RemoveTagRelationAsync(relation.Id, userId);
            Assert.Null(removeError);

            // TagRelation が削除されていることを確認
            Assert.False(await db.TagRelations.AnyAsync(tr => tr.Id == relation.Id));

            // 4. ItemDetailDataProvider からアイテム詳細を取得
            ItemDetailPageData? result = await sut.GetItemDetailAsync(itemId);

            Assert.NotNull(result);
            // TagRelation が削除されても、3件の履歴 (Insert, Update, Delete) が保持されていること
            Assert.Equal(3, result.Ledgers.Count);

            // 履歴に TagNameSnapshot が保持されていること
            Assert.All(result.Ledgers, l => Assert.Equal($"Tag_{tid}", l.TagNameSnapshot));
        }
    }

    private sealed class DbContextFactoryStub(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);

        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }
}