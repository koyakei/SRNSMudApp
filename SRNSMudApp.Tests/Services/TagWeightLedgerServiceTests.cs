using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Services;
using SRNSMudApp.Tests.TestSupport;

namespace SRNSMudApp.Tests.Services;

/// <summary>
///     TagWeightLedgerService の単体テスト。
///     CachedWeight の加減算および TagWeightLedger 履歴レコードの作成が正しく行われることを検証する。
/// </summary>
public class TagWeightLedgerServiceTests : IAsyncLifetime
{
    private MsSqlTestDatabase _sharedDb = null!;
    private readonly TagWeightLedgerService _service = new();

    public async Task InitializeAsync()
    {
        _sharedDb = await SharedMsSqlTestDatabase.GetInstanceAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task RecordItemTagWeightChange_UpdatesCachedWeight_AndAddsLedgerEntry()
    {
        var tid = Guid.NewGuid().ToString("N")[..8];
        await using var context = new ApplicationDbContext(_sharedDb.Options);

        var tag = new Tag
        {
            Name = $"LedgerTag_{tid}",
            OwnerId = "system_root",
            CachedWeight = 10
        };
        context.Tags.Add(tag);
        var item = new Item
        {
            Content = $"Ledger item {tid}",
            OwnerId = "system_root"
        };
        context.Items.Add(item);

        var relation = new TagRelation
        {
            Item = item,
            Tag = tag,
            Weight = 1,
            OwnerId = "system_root"
        };
        context.TagRelations.Add(relation);
        _ = await context.SaveChangesAsync();

        _service.RecordItemTagWeightChange(
            context,
            tag,
            itemId: item.Id,
            sourceType: "TagRelationInsert",
            sourceId: relation.Id,
            delta: 3,
            reason: "テスト加算",
            userId: "system_root");

        _ = await context.SaveChangesAsync();

        Assert.Equal(13, tag.CachedWeight);

        var ledger = await context.TagWeightLedgers
            .FirstOrDefaultAsync(l => l.TagId == tag.Id && l.SourceType == "TagRelationInsert");

        Assert.NotNull(ledger);
        Assert.Equal(10, ledger.PreviousWeight);
        Assert.Equal(13, ledger.NewWeight);
        Assert.Equal(3, ledger.Delta);
        Assert.Equal(item.Id, ledger.ItemId);
        Assert.Equal(relation.Id, ledger.SourceId);
        Assert.Equal(tag.Name, ledger.TagNameSnapshot);
        Assert.True(ledger.IsOwnerAction);
        Assert.Equal("テスト加算", ledger.Reason);
    }

    [Fact]
    public async Task RecordTagToTagWeightChange_UpdatesCachedWeight_AndAddsLedgerEntry()
    {
        var tid = Guid.NewGuid().ToString("N")[..8];
        await using var context = new ApplicationDbContext(_sharedDb.Options);

        var tag = new Tag
        {
            Name = $"TagToTagLedger_{tid}",
            OwnerId = "system_root",
            CachedWeight = 8
        };
        context.Tags.Add(tag);
        _ = await context.SaveChangesAsync();

        _service.RecordTagToTagWeightChange(
            context,
            tag,
            targetTagId: 99,
            sourceType: "TagRelationToTagDelete",
            sourceId: null,
            delta: -2,
            reason: "タグ間解除",
            userId: "system_root");

        _ = await context.SaveChangesAsync();

        Assert.Equal(6, tag.CachedWeight);

        var ledger = await context.TagWeightLedgers
            .FirstOrDefaultAsync(l => l.TagId == tag.Id && l.SourceType == "TagRelationToTagDelete");

        Assert.NotNull(ledger);
        Assert.Equal(8, ledger.PreviousWeight);
        Assert.Equal(6, ledger.NewWeight);
        Assert.Equal(-2, ledger.Delta);
        Assert.Equal(99, ledger.TargetTagId);
        Assert.Null(ledger.SourceId);
        Assert.Equal("タグ間解除", ledger.Reason);
    }

    [Fact]
    public void ThrowsArgumentNullException_WhenArgumentsNull()
    {
        var tag = new Tag { Name = "test", OwnerId = "user" };

        Assert.Throws<ArgumentNullException>(() =>
            _service.RecordItemTagWeightChange(null!, tag, 1, "test", null, 1, "reason", "user"));
        Assert.Throws<ArgumentNullException>(() =>
            _service.RecordItemTagWeightChange(new ApplicationDbContext(new DbContextOptions<ApplicationDbContext>()), null!, 1, "test", null, 1, "reason", "user"));

        Assert.Throws<ArgumentNullException>(() =>
            _service.RecordTagToTagWeightChange(null!, tag, 1, "test", null, 1, "reason", "user"));
        Assert.Throws<ArgumentNullException>(() =>
            _service.RecordTagToTagWeightChange(new ApplicationDbContext(new DbContextOptions<ApplicationDbContext>()), null!, 1, "test", null, 1, "reason", "user"));
    }

    [Fact]
    public async Task RecordItemTagWeightChange_WhenSourceRelationHasCommentItem_CopiesCommentItemToLedger()
    {
        var tid = Guid.NewGuid().ToString("N")[..8];
        await using var context = new ApplicationDbContext(_sharedDb.Options);

        var tag = new Tag { Name = $"LedgerTagWithComment_{tid}", OwnerId = "system_root", CachedWeight = 5 };
        var item = new Item { Content = $"Item {tid}", OwnerId = "system_root" };
        var commentItem = new Item { Content = "初回コメント", OwnerId = "system_root" };
        context.Tags.Add(tag);
        context.Items.AddRange(item, commentItem);
        await context.SaveChangesAsync();

        var relation = new TagRelation
        {
            ItemId = item.Id,
            TagId = tag.Id,
            Weight = 1,
            OwnerId = "system_root",
            CommentItemId = commentItem.Id,
            CommentItem = commentItem
        };
        context.TagRelations.Add(relation);
        await context.SaveChangesAsync();

        _service.RecordItemTagWeightChange(
            context,
            tag,
            itemId: item.Id,
            sourceType: "TagRelationInsert",
            sourceId: relation.Id,
            delta: 1,
            reason: "コメント付きタグ追加",
            userId: "system_root",
            sourceRelation: relation);

        await context.SaveChangesAsync();

        var ledger = await context.TagWeightLedgers
            .FirstOrDefaultAsync(l => l.TagId == tag.Id && l.SourceType == "TagRelationInsert");

        Assert.NotNull(ledger);
        Assert.Equal(commentItem.Id, ledger.CommentItemId);
        Assert.NotNull(ledger.EffectiveCommentItem);
        Assert.Equal("初回コメント", ledger.EffectiveCommentItem.Content);
    }

    [Fact]
    public async Task AddCommentAsync_WhenOwnerComments_CreatesInitialCommentItemDirectly()
    {
        var tid = Guid.NewGuid().ToString("N")[..8];
        var stubFactory = new DbContextFactoryStub(_sharedDb.Options);
        var service = new TagWeightLedgerService(stubFactory);

        await using var context = new ApplicationDbContext(_sharedDb.Options);
        var user1 = $"user1_{tid}";
        await context.SeedUsersAsync(user1);

        var tag = new Tag { Name = $"CommentTestTag_{tid}", OwnerId = user1, CachedWeight = 1 };
        var item = new Item { Content = "Target item", OwnerId = user1 };
        context.Tags.Add(tag);
        context.Items.Add(item);
        await context.SaveChangesAsync();

        var ledger = new TagWeightLedger
        {
            TagId = tag.Id,
            ItemId = item.Id,
            Delta = 1,
            PreviousWeight = 0,
            NewWeight = 1,
            OwnerId = user1,
            Reason = "initial",
            SourceType = "Test"
        };
        context.TagWeightLedgers.Add(ledger);
        await context.SaveChangesAsync();

        // 台帳の Owner 本人がコメントを追加
        var resultItem = await service.AddCommentAsync(ledger.Id, "Owner 本人のコメントです", user1);

        Assert.NotNull(resultItem);
        Assert.Equal("Owner 本人のコメントです", resultItem.Content);
        Assert.Equal(user1, resultItem.OwnerId);
        Assert.Null(resultItem.ParentItemId);

        // 台帳レコードに CommentItemId が直接セットされていること
        var updatedLedger = await context.TagWeightLedgers
            .AsNoTracking()
            .Include(l => l.CommentItem)
            .FirstOrDefaultAsync(l => l.Id == ledger.Id);

        Assert.NotNull(updatedLedger);
        Assert.Equal(resultItem.Id, updatedLedger.CommentItemId);
        Assert.Equal("Owner 本人のコメントです", updatedLedger.EffectiveCommentItem?.Content);
    }

    [Fact]
    public async Task AddCommentAsync_WhenNonOwnerComments_CreatesBaseItemForOwnerAndReplyForNonOwner()
    {
        var tid = Guid.NewGuid().ToString("N")[..8];
        var stubFactory = new DbContextFactoryStub(_sharedDb.Options);
        var service = new TagWeightLedgerService(stubFactory);

        await using var context = new ApplicationDbContext(_sharedDb.Options);
        var user1 = $"user1_{tid}";
        var user2 = $"user2_{tid}";
        await context.SeedUsersAsync(user1, user2);

        var tag = new Tag { Name = $"NonOwnerCommentTag_{tid}", OwnerId = user1, CachedWeight = 1 };
        var item = new Item { Content = "Target item", OwnerId = user1 };
        context.Tags.Add(tag);
        context.Items.Add(item);
        await context.SaveChangesAsync();

        var ledger = new TagWeightLedger
        {
            TagId = tag.Id,
            ItemId = item.Id,
            Delta = 1,
            PreviousWeight = 0,
            NewWeight = 1,
            OwnerId = user1,
            Reason = "initial",
            SourceType = "Test"
        };
        context.TagWeightLedgers.Add(ledger);
        await context.SaveChangesAsync();

        // 台帳の Owner 以外のユーザーがコメントを追加
        var resultItem = await service.AddCommentAsync(ledger.Id, "他のユーザーからのコメントです", user2);

        Assert.NotNull(resultItem);
        Assert.Equal("他のユーザーからのコメントです", resultItem.Content);
        Assert.Equal(user2, resultItem.OwnerId);

        // 台帳レコードの CommentItem は Owner (user1) のベースアイテムであり、
        // resultItem はそのベースアイテムに対するリプライとなっていること
        var updatedLedger = await context.TagWeightLedgers
            .AsNoTracking()
            .Include(l => l.CommentItem)
            .FirstOrDefaultAsync(l => l.Id == ledger.Id);

        Assert.NotNull(updatedLedger);
        Assert.NotNull(updatedLedger.CommentItemId);
        Assert.Equal(updatedLedger.CommentItemId, resultItem.ParentItemId);
        Assert.Equal(user1, updatedLedger.EffectiveCommentItem?.OwnerId);
    }

    [Fact]
    public async Task AddCommentAsync_WhenCommentItemAlreadyExists_CreatesReplyItem()
    {
        var tid = Guid.NewGuid().ToString("N")[..8];
        var stubFactory = new DbContextFactoryStub(_sharedDb.Options);
        var service = new TagWeightLedgerService(stubFactory);

        await using var context = new ApplicationDbContext(_sharedDb.Options);
        var user1 = $"user1_{tid}";
        var user2 = $"user2_{tid}";
        await context.SeedUsersAsync(user1, user2);

        var tag = new Tag { Name = $"ReplyTestTag_{tid}", OwnerId = user1, CachedWeight = 1 };
        var existingCommentItem = new Item { Content = "元のコメント", OwnerId = user1 };
        context.Tags.Add(tag);
        context.Items.Add(existingCommentItem);
        await context.SaveChangesAsync();

        var ledger = new TagWeightLedger
        {
            TagId = tag.Id,
            Delta = 1,
            PreviousWeight = 0,
            NewWeight = 1,
            OwnerId = user1,
            Reason = "initial",
            SourceType = "Test",
            CommentItemId = existingCommentItem.Id
        };
        context.TagWeightLedgers.Add(ledger);
        await context.SaveChangesAsync();

        // 別のユーザーがコメント（返信）を追加
        var replyItem = await service.AddCommentAsync(ledger.Id, "返信コメント", user2);

        Assert.NotNull(replyItem);
        Assert.Equal("返信コメント", replyItem.Content);
        Assert.Equal(user2, replyItem.OwnerId);
        Assert.Equal(existingCommentItem.Id, replyItem.ParentItemId);
    }

    [Fact]
    public async Task AddTagAsync_WhenNoCommentItemExists_CreatesBaseItemAndAddsTagRelation()
    {
        var tid = Guid.NewGuid().ToString("N")[..8];
        var stubFactory = new DbContextFactoryStub(_sharedDb.Options);
        var service = new TagWeightLedgerService(stubFactory);

        await using var context = new ApplicationDbContext(_sharedDb.Options);
        var user1 = $"user1_{tid}";
        var user2 = $"user2_{tid}";
        await context.SeedUsersAsync(user1, user2);

        var tag = new Tag { Name = $"TagForLedger_{tid}", OwnerId = user1, CachedWeight = 1 };
        var tagToAttach = new Tag { Name = $"AttachedTag_{tid}", OwnerId = user1, CachedWeight = 0 };
        context.Tags.AddRange(tag, tagToAttach);
        await context.SaveChangesAsync();

        var ledger = new TagWeightLedger
        {
            TagId = tag.Id,
            Delta = 1,
            PreviousWeight = 0,
            NewWeight = 1,
            OwnerId = user1,
            Reason = "initial",
            SourceType = "Test"
        };
        context.TagWeightLedgers.Add(ledger);
        await context.SaveChangesAsync();

        // 他のユーザーがタグ履歴に対してタグを付与
        var relation = await service.AddTagAsync(ledger.Id, tagToAttach.Id, user2);

        Assert.NotNull(relation);
        Assert.Equal(tagToAttach.Id, relation.TagId);
        Assert.Equal(user2, relation.OwnerId);
        Assert.Equal(1, relation.Weight);

        // 台帳レコードにベースアイテムが紐づいていること
        var updatedLedger = await context.TagWeightLedgers
            .AsNoTracking()
            .Include(l => l.CommentItem)
            .FirstOrDefaultAsync(l => l.Id == ledger.Id);

        Assert.NotNull(updatedLedger);
        Assert.NotNull(updatedLedger.CommentItemId);
        Assert.Equal(relation.ItemId, updatedLedger.CommentItemId);
    }

    [Fact]
    public async Task AddCommentAsync_WhenLedgerIdIsZero_PersistsLedgerFirstAndAddsComment()
    {
        var tid = Guid.NewGuid().ToString("N")[..8];
        var stubFactory = new DbContextFactoryStub(_sharedDb.Options);
        var service = new TagWeightLedgerService(stubFactory);

        await using var context = new ApplicationDbContext(_sharedDb.Options);
        var user1 = $"user1_{tid}";
        var user2 = $"user2_{tid}";
        await context.SeedUsersAsync(user1, user2);

        var tag = new Tag { Name = $"ZeroIdTag_{tid}", OwnerId = user1, CachedWeight = 1 };
        var item = new Item { Content = "Target item", OwnerId = user1 };
        context.Tags.Add(tag);
        context.Items.Add(item);
        await context.SaveChangesAsync();

        var relation = new TagRelation
        {
            ItemId = item.Id,
            TagId = tag.Id,
            Weight = 1,
            OwnerId = user1
        };
        context.TagRelations.Add(relation);
        await context.SaveChangesAsync();

        // Id == 0 の未永続化台帳オブジェクト（ItemDetailDataProvider 等で補完される仮想レコード相当）
        var unpersistedLedger = new TagWeightLedger
        {
            Id = 0,
            TagId = tag.Id,
            TagNameSnapshot = tag.Name,
            ItemId = item.Id,
            SourceType = "TagRelationInsert",
            SourceId = relation.Id,
            Delta = 1,
            PreviousWeight = 0,
            NewWeight = 1,
            IsOwnerAction = true,
            Reason = "タグの新規追加",
            OwnerId = user1
        };

        // 他のユーザーが Id == 0 の台帳レコードに対してコメント
        var replyItem = await service.AddCommentAsync(unpersistedLedger, "未永続化台帳へのコメント", user2);

        Assert.NotNull(replyItem);
        Assert.Equal("未永続化台帳へのコメント", replyItem.Content);
        Assert.Equal(user2, replyItem.OwnerId);

        // DB に台帳レコードが自動永続化され、CommentItem が紐づいていること
        var persistedLedger = await context.TagWeightLedgers
            .AsNoTracking()
            .Include(l => l.CommentItem)
            .FirstOrDefaultAsync(l => l.SourceId == relation.Id && l.SourceType == "TagRelationInsert");

        Assert.NotNull(persistedLedger);
        Assert.True(persistedLedger.Id > 0);
        Assert.NotNull(persistedLedger.CommentItemId);
        Assert.Equal(persistedLedger.CommentItemId, replyItem.ParentItemId);
    }

    private sealed class DbContextFactoryStub(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);

        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}