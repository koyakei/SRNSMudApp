#region

using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Services;

#endregion

namespace SRNSMudApp.Tests.Services;

/// <summary>
///     <see cref="ContentReportService" /> の単体テスト (MSSQL Testcontainers)。
///     通報の作成、スナップショット記録、ステータス更新、対象コンテンツ削除を伴う処置を検証する。
/// </summary>
public class ContentReportServiceTests : IAsyncLifetime
{
    private MsSqlTestDatabase _sharedDb = null!;

    public async Task InitializeAsync()
    {
        _sharedDb = await SharedMsSqlTestDatabase.GetInstanceAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(ApplicationDbContext db, ContentReportService sut, string reporterId, string authorId, string adminId, string tid)> CreateScopeAsync()
    {
        var tid = Guid.NewGuid().ToString("N")[..8];
        var db = new ApplicationDbContext(_sharedDb.Options);
        var sut = new ContentReportService(new DbContextFactoryStub(_sharedDb.Options));

        var reporterId = $"reporter_{tid}";
        var authorId = $"author_{tid}";
        var adminId = $"admin_{tid}";

        await db.SeedUsersAsync(reporterId, authorId, adminId);

        return (db, sut, reporterId, authorId, adminId, tid);
    }

    [Fact]
    public async Task CreateReportAsync_ForItem_SucceedsAndCapturesSnapshot()
    {
        var (db, sut, reporterId, authorId, _, tid) = await CreateScopeAsync();
        await using (db)
        {
            var item = new Item
            {
                Content = $"Inappropriate item content {tid}",
                OwnerId = authorId
            };
            db.Items.Add(item);
            await db.SaveChangesAsync();

            var dto = new CreateContentReportDto
            {
                TargetType = ReportTargetType.Item,
                ItemId = item.Id,
                Reason = "スパム・宣伝目的",
                Detail = "広告リンクが大量に含まれています"
            };

            ContentReport report = await sut.CreateReportAsync(dto, reporterId);

            Assert.NotNull(report);
            Assert.NotEqual(0, report.Id);
            Assert.Equal(ReportTargetType.Item, report.TargetType);
            Assert.Equal(item.Id, report.ItemId);
            Assert.Equal(reporterId, report.OwnerId);
            Assert.Equal(ReportStatus.Pending, report.Status);
            Assert.Equal("スパム・宣伝目的", report.Reason);
            Assert.Equal("広告リンクが大量に含まれています", report.Detail);
            Assert.Contains(item.Content, report.TargetContentSnapshot);
            Assert.Contains(authorId, report.TargetContentSnapshot);
        }
    }

    [Fact]
    public async Task CreateReportAsync_ForTag_SucceedsAndCapturesSnapshot()
    {
        var (db, sut, reporterId, authorId, _, tid) = await CreateScopeAsync();
        await using (db)
        {
            var tag = new Tag
            {
                Name = $"BadTag_{tid}",
                Content = $"Offensive tag description {tid}",
                OwnerId = authorId
            };
            db.Tags.Add(tag);
            await db.SaveChangesAsync();

            var dto = new CreateContentReportDto
            {
                TargetType = ReportTargetType.Tag,
                TagId = tag.Id,
                Reason = "誹謗中傷・嫌がらせ",
                Detail = "特定の個人を攻撃する内容です"
            };

            ContentReport report = await sut.CreateReportAsync(dto, reporterId);

            Assert.NotNull(report);
            Assert.NotEqual(0, report.Id);
            Assert.Equal(ReportTargetType.Tag, report.TargetType);
            Assert.Equal(tag.Id, report.TagId);
            Assert.Equal(reporterId, report.OwnerId);
            Assert.Equal(ReportStatus.Pending, report.Status);
            Assert.Contains(tag.Name, report.TargetContentSnapshot);
            Assert.Contains(tag.Content, report.TargetContentSnapshot);
        }
    }

    [Fact]
    public async Task CreateReportAsync_ThrowsInvalidOperationException_WhenTargetItemNotFound()
    {
        var (db, sut, reporterId, _, _, _) = await CreateScopeAsync();
        await using (db)
        {
            var dto = new CreateContentReportDto
            {
                TargetType = ReportTargetType.Item,
                ItemId = 999999,
                Reason = "その他"
            };

            await Assert.ThrowsAsync<InvalidOperationException>(() => sut.CreateReportAsync(dto, reporterId));
        }
    }

    [Fact]
    public async Task CreateReportAsync_ThrowsInvalidOperationException_WhenTargetTagNotFound()
    {
        var (db, sut, reporterId, _, _, _) = await CreateScopeAsync();
        await using (db)
        {
            var dto = new CreateContentReportDto
            {
                TargetType = ReportTargetType.Tag,
                TagId = 999999,
                Reason = "その他"
            };

            await Assert.ThrowsAsync<InvalidOperationException>(() => sut.CreateReportAsync(dto, reporterId));
        }
    }

    [Fact]
    public async Task GetReportsAsync_FiltersByStatusAndTargetType()
    {
        var (db, sut, reporterId, authorId, _, tid) = await CreateScopeAsync();
        await using (db)
        {
            var item = new Item { Content = $"Item {tid}", OwnerId = authorId };
            var tag = new Tag { Name = $"Tag_{tid}", OwnerId = authorId };
            db.Items.Add(item);
            db.Tags.Add(tag);
            await db.SaveChangesAsync();

            ContentReport itemReport = await sut.CreateReportAsync(new CreateContentReportDto
            {
                TargetType = ReportTargetType.Item,
                ItemId = item.Id,
                Reason = "スパム"
            }, reporterId);

            ContentReport tagReport = await sut.CreateReportAsync(new CreateContentReportDto
            {
                TargetType = ReportTargetType.Tag,
                TagId = tag.Id,
                Reason = "不適切なコンテンツ"
            }, reporterId);

            List<ContentReport> allPending = await sut.GetReportsAsync(ReportStatus.Pending);
            Assert.Contains(allPending, r => r.Id == itemReport.Id);
            Assert.Contains(allPending, r => r.Id == tagReport.Id);

            List<ContentReport> itemsOnly = await sut.GetReportsAsync(targetType: ReportTargetType.Item);
            Assert.Contains(itemsOnly, r => r.Id == itemReport.Id);
            Assert.DoesNotContain(itemsOnly, r => r.Id == tagReport.Id);

            List<ContentReport> tagsOnly = await sut.GetReportsAsync(targetType: ReportTargetType.Tag);
            Assert.Contains(tagsOnly, r => r.Id == tagReport.Id);
            Assert.DoesNotContain(tagsOnly, r => r.Id == itemReport.Id);
        }
    }

    [Fact]
    public async Task UpdateReportStatusAsync_UpdatesStatusAndResolutionNote()
    {
        var (db, sut, reporterId, authorId, adminId, tid) = await CreateScopeAsync();
        await using (db)
        {
            var item = new Item { Content = $"Item {tid}", OwnerId = authorId };
            db.Items.Add(item);
            await db.SaveChangesAsync();

            ContentReport report = await sut.CreateReportAsync(new CreateContentReportDto
            {
                TargetType = ReportTargetType.Item,
                ItemId = item.Id,
                Reason = "スパム"
            }, reporterId);

            var success = await sut.UpdateReportStatusAsync(report.Id, ReportStatus.Reviewed, "確認しました。問題ありません。", adminId);
            Assert.True(success);

            ContentReport? updated = await sut.GetReportByIdAsync(report.Id);
            Assert.NotNull(updated);
            Assert.Equal(ReportStatus.Reviewed, updated.Status);
            Assert.Equal("確認しました。問題ありません。", updated.ResolutionNote);
            Assert.Equal(adminId, updated.HandledByAdminId);
            Assert.NotNull(updated.HandledDate);
        }
    }

    [Fact]
    public async Task ResolveReportWithActionAsync_DeletesItem_WhenDeleteTargetIsTrue()
    {
        var (db, sut, reporterId, authorId, adminId, tid) = await CreateScopeAsync();
        await using (db)
        {
            var item = new Item { Content = $"Item to delete {tid}", OwnerId = authorId };
            db.Items.Add(item);
            await db.SaveChangesAsync();

            ContentReport report = await sut.CreateReportAsync(new CreateContentReportDto
            {
                TargetType = ReportTargetType.Item,
                ItemId = item.Id,
                Reason = "誹謗中傷"
            }, reporterId);

            var success = await sut.ResolveReportWithActionAsync(report.Id, deleteTarget: true, "利用規約違反のため削除", adminId);
            Assert.True(success);

            // 対象アイテムが削除されていることを検証
            Item? deletedItem = await db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == item.Id);
            Assert.Null(deletedItem);

            // 通報レコードはステータス ActionTaken で保持されていることを検証
            ContentReport? updated = await sut.GetReportByIdAsync(report.Id);
            Assert.NotNull(updated);
            Assert.Equal(ReportStatus.ActionTaken, updated.Status);
            Assert.Equal("利用規約違反のため削除", updated.ResolutionNote);
            Assert.Equal(adminId, updated.HandledByAdminId);
            Assert.Contains("Item to delete", updated.TargetContentSnapshot);
        }
    }

    [Fact]
    public async Task ResolveReportWithActionAsync_DeletesTag_WhenDeleteTargetIsTrue()
    {
        var (db, sut, reporterId, authorId, adminId, tid) = await CreateScopeAsync();
        await using (db)
        {
            var tag = new Tag { Name = $"TagToDelete_{tid}", Content = "Content", OwnerId = authorId };
            db.Tags.Add(tag);
            await db.SaveChangesAsync();

            ContentReport report = await sut.CreateReportAsync(new CreateContentReportDto
            {
                TargetType = ReportTargetType.Tag,
                TagId = tag.Id,
                Reason = "スパム"
            }, reporterId);

            var success = await sut.ResolveReportWithActionAsync(report.Id, deleteTarget: true, "スパムタグのため削除", adminId);
            Assert.True(success);

            // 対象タグが削除されていることを検証
            Tag? deletedTag = await db.Tags.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tag.Id);
            Assert.Null(deletedTag);

            ContentReport? updated = await sut.GetReportByIdAsync(report.Id);
            Assert.NotNull(updated);
            Assert.Equal(ReportStatus.ActionTaken, updated.Status);
        }
    }

    [Fact]
    public async Task DeleteReportAsync_RemovesReportSuccessfully()
    {
        var (db, sut, reporterId, authorId, _, tid) = await CreateScopeAsync();
        await using (db)
        {
            var item = new Item { Content = $"Item {tid}", OwnerId = authorId };
            db.Items.Add(item);
            await db.SaveChangesAsync();

            ContentReport report = await sut.CreateReportAsync(new CreateContentReportDto
            {
                TargetType = ReportTargetType.Item,
                ItemId = item.Id,
                Reason = "誤報テスト"
            }, reporterId);

            var deleteSuccess = await sut.DeleteReportAsync(report.Id);
            Assert.True(deleteSuccess);

            ContentReport? deleted = await sut.GetReportByIdAsync(report.Id);
            Assert.Null(deleted);
        }
    }

    private sealed class DbContextFactoryStub(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
    }
}