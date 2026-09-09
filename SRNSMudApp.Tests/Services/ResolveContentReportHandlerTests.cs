#region

using Microsoft.EntityFrameworkCore;

using Moq;

using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;
using SRNSMudApp.Services.Commands;
using SRNSMudApp.Services.Reports;

#endregion

namespace SRNSMudApp.Tests.Services;

/// <summary>
///     <see cref="ResolveContentReportHandler" /> の単体テスト。
///     Command パターンに基づく管理者処置実行、対象削除 Strategy 呼び出し、ステータス更新、
///     および通知連携の実行を検証する。
/// </summary>
public class ResolveContentReportHandlerTests : IAsyncLifetime
{
    private MsSqlTestDatabase _sharedDb = null!;

    public async Task InitializeAsync()
    {
        _sharedDb = await SharedMsSqlTestDatabase.GetInstanceAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(ApplicationDbContext db, DbContextFactoryStub dbFactory, Mock<INotificationService> mockNotification, string reporterId, string authorId, string adminId, string tid)> CreateScopeAsync()
    {
        var tid = Guid.NewGuid().ToString("N")[..8];
        var db = new ApplicationDbContext(_sharedDb.Options);
        var dbFactory = new DbContextFactoryStub(_sharedDb.Options);
        var mockNotification = new Mock<INotificationService>();

        var reporterId = $"reporter_{tid}";
        var authorId = $"author_{tid}";
        var adminId = $"admin_{tid}";

        await db.SeedUsersAsync(reporterId, authorId, adminId);

        return (db, dbFactory, mockNotification, reporterId, authorId, adminId, tid);
    }

    [Fact]
    public async Task HandleAsync_ThrowsArgumentNullException_WhenCommandIsNull()
    {
        var (_, dbFactory, mockNotification, _, _, _, _) = await CreateScopeAsync();
        var handlerFactory = new ReportTargetHandlerFactory([]);
        var sut = new ResolveContentReportHandler(dbFactory, handlerFactory, mockNotification.Object);

        await Assert.ThrowsAsync<ArgumentNullException>(() => sut.HandleAsync(null!));
    }

    [Fact]
    public async Task HandleAsync_ThrowsArgumentException_WhenAdminUserIdIsWhiteSpace()
    {
        var (_, dbFactory, mockNotification, _, _, _, _) = await CreateScopeAsync();
        var handlerFactory = new ReportTargetHandlerFactory([]);
        var sut = new ResolveContentReportHandler(dbFactory, handlerFactory, mockNotification.Object);

        var command = new ResolveContentReportCommand(1, ReportStatus.ActionTaken, false, "Note", "   ");
        await Assert.ThrowsAsync<ArgumentException>(() => sut.HandleAsync(command));
    }

    [Fact]
    public async Task HandleAsync_ReturnsFailure_WhenReportDoesNotExist()
    {
        var (_, dbFactory, mockNotification, _, _, adminId, _) = await CreateScopeAsync();
        var handlerFactory = new ReportTargetHandlerFactory([]);
        var sut = new ResolveContentReportHandler(dbFactory, handlerFactory, mockNotification.Object);

        var command = new ResolveContentReportCommand(999999, ReportStatus.ActionTaken, false, "Note", adminId);
        Result<bool> result = await sut.HandleAsync(command);

        Assert.True(result is Failure);
    }

    [Fact]
    public async Task HandleAsync_WhenDeleteTargetIsTrue_DeletesTargetAndNotifies()
    {
        var (db, dbFactory, mockNotification, reporterId, authorId, adminId, tid) = await CreateScopeAsync();
        await using (db)
        {
            var item = new Item
            {
                Content = $"Bad item {tid}",
                OwnerId = authorId
            };
            db.Items.Add(item);
            await db.SaveChangesAsync();

            var report = new ContentReport
            {
                TargetType = ReportTargetType.Item,
                ItemId = item.Id,
                OwnerId = reporterId,
                Reason = "Spam",
                TargetContentSnapshot = "Snapshot"
            };
            db.ContentReports.Add(report);
            await db.SaveChangesAsync();

            var mockTargetHandler = new Mock<IReportTargetHandler>();
            mockTargetHandler.Setup(h => h.TargetType).Returns(ReportTargetType.Item);
            mockTargetHandler
                .Setup(h => h.DeleteTargetAsync(It.IsAny<ApplicationDbContext>(), item.Id, It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var mockFactory = new Mock<IReportTargetHandlerFactory>();
            mockFactory.Setup(f => f.GetHandler(ReportTargetType.Item)).Returns(mockTargetHandler.Object);

            var sut = new ResolveContentReportHandler(dbFactory, mockFactory.Object, mockNotification.Object);

            var command = new ResolveContentReportCommand(
                report.Id,
                ReportStatus.ActionTaken,
                DeleteTarget: true,
                ResolutionNote: "Resolved note",
                AdminUserId: adminId);

            // Act
            Result<bool> result = await sut.HandleAsync(command);

            // Assert
            Assert.True(result is Success<bool> success && success.Value);

            mockTargetHandler.Verify(h => h.DeleteTargetAsync(It.IsAny<ApplicationDbContext>(), item.Id, It.IsAny<CancellationToken>()), Times.Once);
            mockNotification.Verify(n => n.NotifyNotificationsChanged(), Times.Once);

            await using var verifyDb = new ApplicationDbContext(_sharedDb.Options);
            ContentReport? updatedReport = await verifyDb.ContentReports.FindAsync(report.Id);
            Assert.NotNull(updatedReport);
            Assert.Equal(ReportStatus.ActionTaken, updatedReport.Status);
            Assert.Equal(adminId, updatedReport.HandledByAdminId);
            Assert.Equal("Resolved note", updatedReport.ResolutionNote);
            Assert.NotNull(updatedReport.HandledDate);
        }
    }

    [Fact]
    public async Task HandleAsync_WhenDeleteTargetIsFalse_DoesNotDeleteTarget()
    {
        var (db, dbFactory, mockNotification, reporterId, authorId, adminId, tid) = await CreateScopeAsync();
        await using (db)
        {
            var item = new Item
            {
                Content = $"Safe item {tid}",
                OwnerId = authorId
            };
            db.Items.Add(item);
            await db.SaveChangesAsync();

            var report = new ContentReport
            {
                TargetType = ReportTargetType.Item,
                ItemId = item.Id,
                OwnerId = reporterId,
                Reason = "False alarm",
                TargetContentSnapshot = "Snapshot"
            };
            db.ContentReports.Add(report);
            await db.SaveChangesAsync();

            var mockTargetHandler = new Mock<IReportTargetHandler>();
            mockTargetHandler.Setup(h => h.TargetType).Returns(ReportTargetType.Item);

            var mockFactory = new Mock<IReportTargetHandlerFactory>();
            mockFactory.Setup(f => f.GetHandler(ReportTargetType.Item)).Returns(mockTargetHandler.Object);

            var sut = new ResolveContentReportHandler(dbFactory, mockFactory.Object, mockNotification.Object);

            var command = new ResolveContentReportCommand(
                report.Id,
                ReportStatus.Dismissed,
                DeleteTarget: false,
                ResolutionNote: "No issue found",
                AdminUserId: adminId);

            // Act
            Result<bool> result = await sut.HandleAsync(command);

            // Assert
            Assert.True(result is Success<bool> success && success.Value);

            mockTargetHandler.Verify(h => h.DeleteTargetAsync(It.IsAny<ApplicationDbContext>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
            mockNotification.Verify(n => n.NotifyNotificationsChanged(), Times.Once);

            await using var verifyDb = new ApplicationDbContext(_sharedDb.Options);
            ContentReport? updatedReport = await verifyDb.ContentReports.FindAsync(report.Id);
            Assert.NotNull(updatedReport);
            Assert.Equal(ReportStatus.Dismissed, updatedReport.Status);
            Assert.Equal(adminId, updatedReport.HandledByAdminId);
            Assert.Equal("No issue found", updatedReport.ResolutionNote);
        }
    }

    private sealed class DbContextFactoryStub(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
    }
}