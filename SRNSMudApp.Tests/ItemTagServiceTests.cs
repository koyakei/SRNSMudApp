using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

using Moq;

using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;

namespace SRNSMudApp.Tests.Services;

public class ItemTagServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ItemTagService _service;

    public ItemTagServiceTests()
    {
        DbContextOptions<ApplicationDbContext> options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _dbContext = new ApplicationDbContext(options);

        var mockDbFactory = new Mock<IDbContextFactory<ApplicationDbContext>>();
        mockDbFactory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new ApplicationDbContext(options));

        _service = new ItemTagService(mockDbFactory.Object);
    }

    public void Dispose()
    {
        _dbContext.Database.EnsureDeleted();
        _dbContext.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task AddReplyToRequestAsync_ShouldCreateItemAndReturnItWithRelations()
    {
        // Arrange
        var userId = "TestUser";
        var user = new ApplicationUser { Id = userId, UserName = "TestUser" };
        _dbContext.Users.Add(user);

        var request = new TaggingRequestEntity
        {
            ContractType = "Gratis",
            OwnerId = userId,
            TargetItemId = 1,
            RequestedTagId = 1,
            RequesterUserId = userId,
            TagOwnerUserId = userId
        };
        _dbContext.TaggingRequestEntities.Add(request);
        await _dbContext.SaveChangesAsync();

        var message = "This is a test reply";

        // Act
        Item? replyItem = await _service.AddReplyToRequestAsync(request.Id, userId, message);

        // Assert
        Assert.NotNull(replyItem);
        Assert.Equal(request.Id, replyItem.TaggingRequestEntityId);
        Assert.Equal(userId, replyItem.OwnerId);
        Assert.Equal(message, replyItem.Content);
        Assert.NotNull(replyItem.Owner);
        Assert.Equal(userId, replyItem.Owner.Id);

        // Ensure it's saved in the DB
        Item? savedItem = await _dbContext.Items.FirstOrDefaultAsync(i => i.Id == replyItem.Id);
        Assert.NotNull(savedItem);
        Assert.Equal(request.Id, savedItem.TaggingRequestEntityId);
        Assert.Equal(message, savedItem.Content);
    }

    [Fact]
    public async Task AddTagToItemAsync_ShouldIncreaseCachedWeightAndAddLedger()
    {
        var userId = "TestUser";
        _dbContext.Users.Add(new ApplicationUser { Id = userId, UserName = "TestUser" });
        var item = new Item { Content = "TestItem", OwnerId = userId };
        _dbContext.Items.Add(item);
        var tag = new Tag { Name = "TestTag", OwnerId = userId, CachedWeight = 5 };
        _dbContext.Tags.Add(tag);
        await _dbContext.SaveChangesAsync();

        var result = await _service.AddTagToItemAsync(item.Id, tag.Id, userId);

        Assert.Null(result);
        Tag? updatedTag = await _dbContext.Tags.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tag.Id);
        Assert.Equal(6, updatedTag!.CachedWeight);

        TagWeightLedger? ledger =
            await _dbContext.TagWeightLedgers!.SingleOrDefaultAsync(l => l.SourceType == "TagRelationInsert");
        Assert.NotNull(ledger);
        Assert.Equal(tag.Id, ledger.TagId);
        Assert.Equal(5, ledger.PreviousWeight);
        Assert.Equal(6, ledger.NewWeight);
        Assert.Equal(1, ledger.Delta);
    }

    [Fact]
    public async Task RemoveTagRelationAsync_ShouldDecreaseCachedWeightAndAddLedger()
    {
        var userId = "TestUser";
        _dbContext.Users.Add(new ApplicationUser { Id = userId, UserName = "TestUser" });
        var item = new Item { Content = "TestItem", OwnerId = userId };
        _dbContext.Items.Add(item);
        var tag = new Tag { Name = "TestTag", OwnerId = userId, CachedWeight = 5 };
        _dbContext.Tags.Add(tag);
        var relation = new TagRelation { ItemId = item.Id, TagId = tag.Id, OwnerId = userId, Weight = 2 };
        _dbContext.TagRelations.Add(relation);
        await _dbContext.SaveChangesAsync();

        var result = await _service.RemoveTagRelationAsync(relation.Id, userId);

        Assert.Null(result);
        Tag? updatedTag = await _dbContext.Tags.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tag.Id);
        Assert.Equal(3, updatedTag!.CachedWeight);

        TagWeightLedger? ledger =
            await _dbContext.TagWeightLedgers!.SingleOrDefaultAsync(l => l.SourceType == "TagRelationDelete");
        Assert.NotNull(ledger);
        Assert.Equal(tag.Id, ledger.TagId);
        Assert.Equal(5, ledger.PreviousWeight);
        Assert.Equal(3, ledger.NewWeight);
        Assert.Equal(-2, ledger.Delta);
    }

    [Fact]
    public async Task AddTagToTagAsync_ShouldIncreaseCachedWeightAndAddLedger()
    {
        var userId = "TestUser";
        _dbContext.Users.Add(new ApplicationUser { Id = userId, UserName = "TestUser" });
        var targetTag = new Tag { Name = "TargetTag", OwnerId = userId };
        var childTag = new Tag { Name = "ChildTag", OwnerId = userId, CachedWeight = 10 };
        _dbContext.Tags.AddRange(targetTag, childTag);
        await _dbContext.SaveChangesAsync();

        var result = await _service.AddTagToTagAsync(targetTag.Id, childTag.Id, userId);

        Assert.Null(result);
        Tag? updatedTag = await _dbContext.Tags.AsNoTracking().FirstOrDefaultAsync(t => t.Id == childTag.Id);
        Assert.Equal(11, updatedTag!.CachedWeight);

        TagWeightLedger? ledger =
            await _dbContext.TagWeightLedgers!.SingleOrDefaultAsync(l => l.SourceType == "TagRelationToTagInsert");
        Assert.NotNull(ledger);
        Assert.Equal(childTag.Id, ledger.TagId);
        Assert.Equal(10, ledger.PreviousWeight);
        Assert.Equal(11, ledger.NewWeight);
        Assert.Equal(1, ledger.Delta);
    }

    [Fact]
    public async Task RemoveTagToTagRelationAsync_ShouldDecreaseCachedWeightAndAddLedger()
    {
        var userId = "TestUser";
        _dbContext.Users.Add(new ApplicationUser { Id = userId, UserName = "TestUser" });
        var targetTag = new Tag { Name = "TargetTag", OwnerId = userId };
        var childTag = new Tag { Name = "ChildTag", OwnerId = userId, CachedWeight = 10 };
        _dbContext.Tags.AddRange(targetTag, childTag);
        var relation = new TagRelationToTag
        {
            TargetTagId = targetTag.Id,
            TagId = childTag.Id,
            OwnerId = userId,
            Weight = 3
        };
        _dbContext.TagRelationToTags.Add(relation);
        await _dbContext.SaveChangesAsync();

        var result = await _service.RemoveTagToTagRelationAsync(relation.Id, userId);

        Assert.Null(result);
        Tag? updatedTag = await _dbContext.Tags.AsNoTracking().FirstOrDefaultAsync(t => t.Id == childTag.Id);
        Assert.Equal(7, updatedTag!.CachedWeight);

        TagWeightLedger? ledger =
            await _dbContext.TagWeightLedgers!.SingleOrDefaultAsync(l => l.SourceType == "TagRelationToTagDelete");
        Assert.NotNull(ledger);
        Assert.Equal(childTag.Id, ledger.TagId);
        Assert.Equal(10, ledger.PreviousWeight);
        Assert.Equal(7, ledger.NewWeight);
        Assert.Equal(-3, ledger.Delta);
    }

    [Fact]
    public async Task AddTagToItemAsync_ShouldReturnErrorIfTagNotOwnedByUser()
    {
        var ownerId = "OwnerUser";
        var otherUserId = "OtherUser";
        _dbContext.Users.Add(new ApplicationUser { Id = ownerId, UserName = "Owner" });
        _dbContext.Users.Add(new ApplicationUser { Id = otherUserId, UserName = "Other" });
        var item = new Item { Content = "TestItem", OwnerId = ownerId };
        _dbContext.Items.Add(item);
        var tag = new Tag { Name = "TestTag", OwnerId = ownerId };
        _dbContext.Tags.Add(tag);
        await _dbContext.SaveChangesAsync();

        var result = await _service.AddTagToItemAsync(item.Id, tag.Id, otherUserId);

        Assert.Equal("タグの作成者ではないため、追加する権限がありません。", result);
    }

    /// <summary>
    ///     既存の Item に対してタグを追加しても、Item が新規エンティティとして
    ///     再追加され主キー重複例外が起きないことを検証する。
    ///     （ItemTaggingE2ETests/AddTagToItem_FailsIfItemIsAddedAsNewEntity の移行テスト。
    ///     ダイアログUI経由ではなくサービス層で直接検証する）
    /// </summary>
    [Fact]
    public async Task AddTagToItemAsync_WithExistingItem_DoesNotDuplicateItemEntity()
    {
        // Arrange: 既存ユーザー・既存アイテム・既存タグを事前投入
        var userId = "TestUser";
        _dbContext.Users.Add(new ApplicationUser { Id = userId, UserName = "TestUser" });
        var item = new Item { Content = "Existing Item", OwnerId = userId };
        _dbContext.Items.Add(item);
        await _dbContext.SaveChangesAsync();
        var savedItemId = item.Id;

        var tag = new Tag { Name = "TestTag", Content = "Test content", OwnerId = userId, CachedWeight = 0 };
        _dbContext.Tags.Add(tag);
        await _dbContext.SaveChangesAsync();

        // Act: 既存アイテムへタグ追加（Item を Attach/Add し直すとここで主キー重複例外が発生する）
        var result = await _service.AddTagToItemAsync(savedItemId, tag.Id, userId);

        // Assert: 成功（エラーメッセージなし）
        Assert.Null(result);

        // アイテムは重複していない（1件のまま）
        Assert.Equal(1, await _dbContext.Items.CountAsync(i => i.Id == savedItemId));

        // タグリレーションが作成されている
        TagRelation? relation =
            await _dbContext.TagRelations.SingleOrDefaultAsync(tr => tr.ItemId == savedItemId && tr.TagId == tag.Id);
        Assert.NotNull(relation);
        Assert.Equal(userId, relation!.OwnerId);
    }
}