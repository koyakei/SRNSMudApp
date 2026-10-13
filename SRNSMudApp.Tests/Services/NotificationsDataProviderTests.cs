using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;
using SRNSMudApp.Tests.TestSupport;

namespace SRNSMudApp.Tests.Services;

/// <summary>
/// <see cref="NotificationsDataProvider"/> の通知生データ取得ロジックの単体テスト (MSSQL Testcontainers)。
/// LINQ クエリが EF Core によって正常に SQL 変換され、CommentItem を含む TagRelation コメントが正しく抽出されるかを検証する。
/// </summary>
public class NotificationsDataProviderTests : IAsyncLifetime
{
    private MsSqlTestDatabase _sharedDb = null!;

    public async Task InitializeAsync()
    {
        _sharedDb = await SharedMsSqlTestDatabase.GetInstanceAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(ApplicationDbContext db, NotificationsDataProvider sut, string itemOwnerId, string commenterId, int itemId, int tagId)> CreateScopeAsync()
    {
        var tid = Guid.NewGuid().ToString("N")[..8];
        var db = new ApplicationDbContext(_sharedDb.Options);
        var sut = new NotificationsDataProvider(new DbContextFactoryStub(_sharedDb.Options));

        var itemOwnerId = $"item_owner_{tid}";
        var commenterId = $"commenter_{tid}";

        await db.SeedUsersAsync(itemOwnerId, commenterId);

        var tag = new Tag { Name = $"Tag_{tid}", IsSystem = true, OwnerId = itemOwnerId, CachedWeight = 0 };
        var item = new Item { Content = $"TargetItem_{tid}", OwnerId = itemOwnerId };

        db.Tags.Add(tag);
        db.Items.Add(item);
        await db.SaveChangesAsync();

        return (db, sut, itemOwnerId, commenterId, item.Id, tag.Id);
    }

    [Fact]
    public async Task GetNotificationRawDataAsync_TranslatesTagRelationCommentsQuery_AndRetrievesCommentItem()
    {
        // Arrange
        var (db, sut, itemOwnerId, commenterId, itemId, tagId) = await CreateScopeAsync();
        await using (db)
        {
            var commentItem = new Item
            {
                OwnerId = commenterId,
                Content = "素晴らしい記事です！",
                ItemKindJson = System.Text.Json.JsonSerializer.Serialize(new TagCommentItem(itemId, tagId))
            };
            db.Items.Add(commentItem);
            await db.SaveChangesAsync();

            var tagRelation = new TagRelation
            {
                ItemId = itemId,
                TagId = tagId,
                OwnerId = commenterId,
                Weight = 1,
                CommentItemId = commentItem.Id
            };
            db.TagRelations.Add(tagRelation);
            await db.SaveChangesAsync();

            // Act: itemOwnerId 宛ての通知を取得
            NotificationRawData rawData = await sut.GetNotificationRawDataAsync(itemOwnerId);

            // Assert
            Assert.NotNull(rawData.TagRelationComments);
            TagRelation? targetCommentRelation = rawData.TagRelationComments.FirstOrDefault(tr => tr.Id == tagRelation.Id);
            Assert.NotNull(targetCommentRelation);
            Assert.Equal("素晴らしい記事です！", targetCommentRelation.Comment);
            Assert.NotNull(targetCommentRelation.CommentItem);
            Assert.Equal("素晴らしい記事です！", targetCommentRelation.CommentItem.Content);
        }
    }

    [Fact]
    public async Task GetNotificationRawDataAsync_ExcludesOwnCommentOrEmptyComment()
    {
        // Arrange
        var (db, sut, itemOwnerId, commenterId, itemId, tagId) = await CreateScopeAsync();
        await using (db)
        {
            // 1. 自身のコメント（通知対象外）
            var ownCommentItem = new Item
            {
                OwnerId = itemOwnerId,
                Content = "自己コメント",
                ItemKindJson = System.Text.Json.JsonSerializer.Serialize(new TagCommentItem(itemId, tagId))
            };
            db.Items.Add(ownCommentItem);
            await db.SaveChangesAsync();

            var ownRelation = new TagRelation
            {
                ItemId = itemId,
                TagId = tagId,
                OwnerId = itemOwnerId,
                Weight = 1,
                CommentItemId = ownCommentItem.Id
            };
            db.TagRelations.Add(ownRelation);

            // 2. コメント無しの他者 Relation（通知対象外）
            var noCommentRelation = new TagRelation
            {
                ItemId = itemId,
                TagId = tagId,
                OwnerId = commenterId,
                Weight = 1,
                CommentItemId = null
            };
            db.TagRelations.Add(noCommentRelation);
            await db.SaveChangesAsync();

            // Act: itemOwnerId 宛ての通知を取得
            NotificationRawData rawData = await sut.GetNotificationRawDataAsync(itemOwnerId);

            // Assert
            Assert.DoesNotContain(rawData.TagRelationComments, tr => tr.Id == ownRelation.Id);
            Assert.DoesNotContain(rawData.TagRelationComments, tr => tr.Id == noCommentRelation.Id);
        }
    }

    [Fact]
    public async Task GetNotificationRawDataAsync_RetrievesSplitRequestsAndProposals_WithoutExceptions()
    {
        // Arrange
        var (db, sut, itemOwnerId, commenterId, itemId, tagId) = await CreateScopeAsync();
        await using (db)
        {
            var splitRequest = new ItemSplitRequest
            {
                OwnerId = commenterId,
                OriginalItemId = itemId,
                RequesterUserId = commenterId,
                OwnerUserId = itemOwnerId,
                SelectedText = "分割対象テキスト",
                Status = TradeStatus.Proposed
            };
            db.ItemSplitRequests.Add(splitRequest);

            var proposal = new TagContentProposal
            {
                OwnerId = commenterId,
                TagId = tagId,
                RequesterUserId = commenterId,
                OwnerUserId = itemOwnerId,
                ProposedContent = "新しいタグ説明",
                Status = TradeStatus.Proposed
            };
            db.TagContentProposals.Add(proposal);
            await db.SaveChangesAsync();

            // Act
            NotificationRawData rawData = await sut.GetNotificationRawDataAsync(itemOwnerId);

            // Assert
            Assert.NotNull(rawData.SplitRequests);
            Assert.Contains(rawData.SplitRequests, r => r.Id == splitRequest.Id);
            Assert.NotNull(rawData.TagContentProposals);
            Assert.Contains(rawData.TagContentProposals, p => p.Id == proposal.Id);
        }
    }

    [Fact]
    public async Task GetNotificationRawDataAsync_WhenMultipleTagsHaveSameName_DoesNotThrowDuplicateKeyExceptionAndDoesNotSubstituteTagId()
    {
        var (db, sut, itemOwnerId, commenterId, _, _) = await CreateScopeAsync();
        await using (db)
        {
            // 同一名のタグが複数存在する場合 (例: "真実")
            var dupName = "真実";
            var tag1 = new Tag { Name = dupName, OwnerId = commenterId };
            var tag2 = new Tag { Name = dupName, OwnerId = itemOwnerId };
            db.Tags.AddRange(tag1, tag2);

            // ItemKindJson なし（古いリクエストなど）で、本文に「真実」が含まれるリクエスト
            var requestItem = new Item
            {
                OwnerId = commenterId,
                Content = $"【タグ操作権限リクエスト】\nタグ「{dupName}」の操作権限 1 をリクエストしました。（無償リクエスト）",
                NotificationRecipients =
                [
                    new ItemReplyNotificationRecipient
                    {
                        RecipientUserId = itemOwnerId
                    }
                ]
            };
            db.Items.Add(requestItem);
            await db.SaveChangesAsync();

            // Act: 通知生データを取得
            NotificationRawData rawData = await sut.GetNotificationRawDataAsync(itemOwnerId);

            // Assert: 重複キー例外が発生せず、タグIDのすり替え・解決も行われないこと
            Assert.NotNull(rawData.TagPermissionRequests);
            var item = Assert.Single(rawData.TagPermissionRequests, i => i.Id == requestItem.Id);
            var payload = RightAssetDataProvider.ParsePermissionPayload(item);
            Assert.NotNull(payload);
            Assert.Equal(0, payload.RequestedTagId);
        }
    }

    /// <summary>
    /// クエリ実行中に SQL Server のリソースプールメモリが枯渇し、
    /// 'There is insufficient system memory in resource pool 'default' to run this query.' (SqlException 701)
    /// が発生するシナリオを検証する再現テスト。
    /// DbCommandInterceptor を用いて行 116 の requestReplies クエリ ([TaggingRequestEntityId] != 0) 実行時に
    /// エラー 701 の SqlException を発生させ、期待通り SqlException がスローされることを検証する。
    /// </summary>
    [Fact]
    public async Task GetNotificationRawDataAsync_WhenInsufficientSystemMemoryInResourcePool_ThrowsSqlException()
    {
        // Arrange
        var (db, _, itemOwnerId, _, _, _) = await CreateScopeAsync();
        await using (db)
        {
            var interceptor = new SqlMemoryExhaustionInterceptor("TaggingRequestEntityId");
            var options = SqlMemoryExhaustionInterceptor.CreateOptions(_sharedDb.ConnectionString, interceptor);
            var sut = new NotificationsDataProvider(new DbContextFactoryStub(options));

            // Act & Assert
            var ex = await Assert.ThrowsAsync<SqlException>(() =>
                sut.GetNotificationRawDataAsync(itemOwnerId));

            Assert.Equal(701, ex.Number);
            Assert.Contains("There is insufficient system memory in resource pool 'default' to run this query.", ex.Message);
        }
    }

    /// <summary>
    /// NotificationsDataProvider.GetAssociatedItemsAsync がアイテム・タグ階層 Include クエリを実行する際、
    /// SQL Server のリソースプールメモリ不足 (SqlException 701) が発生するシナリオを検証するテスト。
    /// </summary>
    [Fact]
    public async Task GetAssociatedItemsAsync_WhenInsufficientSystemMemoryInResourcePool_ThrowsSqlException()
    {
        // Arrange
        var (db, _, _, _, itemId, _) = await CreateScopeAsync();
        await using (db)
        {
            var interceptor = new SqlMemoryExhaustionInterceptor();
            var options = SqlMemoryExhaustionInterceptor.CreateOptions(_sharedDb.ConnectionString, interceptor);
            var sut = new NotificationsDataProvider(new DbContextFactoryStub(options));

            // Act & Assert
            var ex = await Assert.ThrowsAsync<SqlException>(() =>
                sut.GetAssociatedItemsAsync([itemId]));

            Assert.Equal(701, ex.Number);
            Assert.Contains("There is insufficient system memory in resource pool 'default' to run this query.", ex.Message);
        }
    }

    /// <summary>
    /// NotificationsDataProvider.MarkAllAsReadAsync で一括既読更新を実行する際、
    /// SQL Server のリソースプールメモリ不足 (SqlException 701) が発生するシナリオを検証するテスト。
    /// </summary>
    [Fact]
    public async Task MarkAllAsReadAsync_WhenInsufficientSystemMemoryInResourcePool_ThrowsSqlException()
    {
        // Arrange
        var (db, _, itemOwnerId, _, _, _) = await CreateScopeAsync();
        await using (db)
        {
            var interceptor = new SqlMemoryExhaustionInterceptor();
            var options = SqlMemoryExhaustionInterceptor.CreateOptions(_sharedDb.ConnectionString, interceptor);
            var sut = new NotificationsDataProvider(new DbContextFactoryStub(options));

            // Act & Assert
            var ex = await Assert.ThrowsAsync<SqlException>(() =>
                sut.MarkAllAsReadAsync(itemOwnerId, [(1, "TagRequest")]));

            Assert.Equal(701, ex.Number);
            Assert.Contains("There is insufficient system memory in resource pool 'default' to run this query.", ex.Message);
        }
    }

    private sealed class DbContextFactoryStub(DbContextOptions<ApplicationDbContext> options) : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
    }
}