#region

using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services.Reports;

#endregion

namespace SRNSMudApp.Tests.Services;

/// <summary>
///     通報対象 Strategy (<see cref="ItemReportTargetHandler" />, <see cref="TagReportTargetHandler" />)
///     および <see cref="ReportTargetHandlerFactory" /> の単体テスト。
/// </summary>
public class ReportTargetHandlerTests : IAsyncLifetime
{
    private MsSqlTestDatabase _sharedDb = null!;

    public async Task InitializeAsync()
    {
        _sharedDb = await SharedMsSqlTestDatabase.GetInstanceAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(ApplicationDbContext db, string authorId, string tid)> CreateScopeAsync()
    {
        var tid = Guid.NewGuid().ToString("N")[..8];
        var db = new ApplicationDbContext(_sharedDb.Options);
        var authorId = $"author_{tid}";
        await db.SeedUsersAsync(authorId);
        return (db, authorId, tid);
    }

    [Fact]
    public async Task ItemReportTargetHandler_CreateSnapshotAsync_ReturnsFormattedContent()
    {
        var (db, authorId, tid) = await CreateScopeAsync();
        await using (db)
        {
            var item = new Item
            {
                Content = $"Test item content {tid}",
                OwnerId = authorId
            };
            db.Items.Add(item);
            await db.SaveChangesAsync();

            var handler = new ItemReportTargetHandler();
            var snapshot = await handler.CaptureSnapshotAsync(db, item.Id);

            Assert.Contains($"Test item content {tid}", snapshot);
        }
    }

    [Fact]
    public async Task ItemReportTargetHandler_CaptureSnapshotAsync_WhenItemNotFound_ThrowsInvalidOperationException()
    {
        var (db, _, _) = await CreateScopeAsync();
        await using (db)
        {
            var handler = new ItemReportTargetHandler();
            await Assert.ThrowsAsync<InvalidOperationException>(() => handler.CaptureSnapshotAsync(db, 999999));
        }
    }

    [Fact]
    public async Task ItemReportTargetHandler_DeleteTargetAsync_RemovesItem()
    {
        var (db, authorId, tid) = await CreateScopeAsync();
        await using (db)
        {
            var item = new Item
            {
                Content = $"To be deleted {tid}",
                OwnerId = authorId
            };
            db.Items.Add(item);
            await db.SaveChangesAsync();

            var handler = new ItemReportTargetHandler();
            await handler.DeleteTargetAsync(db, item.Id);
            await db.SaveChangesAsync();

            Item? found = await db.Items.FindAsync(item.Id);
            Assert.Null(found);
        }
    }

    [Fact]
    public async Task TagReportTargetHandler_CaptureSnapshotAsync_ReturnsFormattedContent()
    {
        var (db, authorId, tid) = await CreateScopeAsync();
        await using (db)
        {
            var tag = new Tag
            {
                Name = $"TestTag_{tid}",
                OwnerId = authorId
            };
            db.Tags.Add(tag);
            await db.SaveChangesAsync();

            var handler = new TagReportTargetHandler();
            var snapshot = await handler.CaptureSnapshotAsync(db, tag.Id);

            Assert.Contains($"TestTag_{tid}", snapshot);
        }
    }

    [Fact]
    public async Task TagReportTargetHandler_CaptureSnapshotAsync_WhenTagNotFound_ThrowsInvalidOperationException()
    {
        var (db, _, _) = await CreateScopeAsync();
        await using (db)
        {
            var handler = new TagReportTargetHandler();
            await Assert.ThrowsAsync<InvalidOperationException>(() => handler.CaptureSnapshotAsync(db, 999999));
        }
    }

    [Fact]
    public async Task TagReportTargetHandler_DeleteTargetAsync_RemovesTag()
    {
        var (db, authorId, tid) = await CreateScopeAsync();
        await using (db)
        {
            var tag = new Tag
            {
                Name = $"DeleteTag_{tid}",
                OwnerId = authorId
            };
            db.Tags.Add(tag);
            await db.SaveChangesAsync();

            var handler = new TagReportTargetHandler();
            await handler.DeleteTargetAsync(db, tag.Id);
            await db.SaveChangesAsync();

            Tag? found = await db.Tags.FindAsync(tag.Id);
            Assert.Null(found);
        }
    }

    [Fact]
    public void ReportTargetHandlerFactory_ResolvesRegisteredHandlers()
    {
        var itemHandler = new ItemReportTargetHandler();
        var tagHandler = new TagReportTargetHandler();
        var factory = new ReportTargetHandlerFactory([itemHandler, tagHandler]);

        Assert.Same(itemHandler, factory.GetHandler(ReportTargetType.Item));
        Assert.Same(tagHandler, factory.GetHandler(ReportTargetType.Tag));
    }

    [Fact]
    public void ReportTargetHandlerFactory_ThrowsNotSupportedException_ForUnknownTargetType()
    {
        var factory = new ReportTargetHandlerFactory([]);
        Assert.Throws<NotSupportedException>(() => factory.GetHandler((ReportTargetType)999));
    }
}