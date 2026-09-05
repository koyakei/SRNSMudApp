using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;
using SRNSMudApp.Tests.TestSupport;

namespace SRNSMudApp.Tests.Services;

/// <summary>
///     TimelineRecorder の単体テスト。
///     タグ関連付けの追加・削除・更新イベントが期待通りの属性値で
///     TimelineEvents DbSet に追加されることを検証する。
/// </summary>
public class TimelineRecorderTests : IAsyncLifetime
{
    private MsSqlTestDatabase _sharedDb = null!;
    private readonly TimelineRecorder _recorder = new();

    public async Task InitializeAsync()
    {
        _sharedDb = await SharedMsSqlTestDatabase.GetInstanceAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task RecordTagRelationAdded_AddsInsertEvent()
    {
        var tid = Guid.NewGuid().ToString("N")[..8];
        await using var context = new ApplicationDbContext(_sharedDb.Options);

        var item = new Item { Content = $"Timeline item {tid}", OwnerId = "system_root" };
        var tag = new Tag { Name = $"TimelineTag_{tid}", OwnerId = "system_root" };
        context.Items.Add(item);
        context.Tags.Add(tag);
        _ = await context.SaveChangesAsync();

        _recorder.RecordTagRelationAdded(context, "system_root", item.Id, tag.Id, 1);
        _ = await context.SaveChangesAsync();

        var evt = await context.TimelineEvents
            .FirstOrDefaultAsync(e => e.OwnerId == "system_root" && e.FollowedTagId == tag.Id && e.EventType == "Insert");

        Assert.NotNull(evt);
        Assert.Equal(1, evt.NewWeight);
        Assert.True(evt.Target is ItemTarget it && it.TargetItemId == item.Id);
    }

    [Fact]
    public async Task RecordTagRelationDeleted_AddsDeleteEvent()
    {
        var tid = Guid.NewGuid().ToString("N")[..8];
        await using var context = new ApplicationDbContext(_sharedDb.Options);

        var item = new Item { Content = $"Timeline item {tid}", OwnerId = "system_root" };
        var tag = new Tag { Name = $"TimelineTag_{tid}", OwnerId = "system_root" };
        context.Items.Add(item);
        context.Tags.Add(tag);
        _ = await context.SaveChangesAsync();

        _recorder.RecordTagRelationDeleted(context, "system_root", item.Id, tag.Id, 5);
        _ = await context.SaveChangesAsync();

        var evt = await context.TimelineEvents
            .FirstOrDefaultAsync(e => e.OwnerId == "system_root" && e.FollowedTagId == tag.Id && e.EventType == "Delete");

        Assert.NotNull(evt);
        Assert.Equal(5, evt.PreviousWeight);
    }

    [Fact]
    public async Task RecordTagRelationUpdated_AddsUpdateEvent()
    {
        var tid = Guid.NewGuid().ToString("N")[..8];
        await using var context = new ApplicationDbContext(_sharedDb.Options);

        var item = new Item { Content = $"Timeline item {tid}", OwnerId = "system_root" };
        var tag = new Tag { Name = $"TimelineTag_{tid}", OwnerId = "system_root" };
        context.Items.Add(item);
        context.Tags.Add(tag);
        _ = await context.SaveChangesAsync();

        _recorder.RecordTagRelationUpdated(context, "system_root", item.Id, tag.Id, 2, 7);
        _ = await context.SaveChangesAsync();

        var evt = await context.TimelineEvents
            .FirstOrDefaultAsync(e => e.OwnerId == "system_root" && e.FollowedTagId == tag.Id && e.EventType == "Update");

        Assert.NotNull(evt);
        Assert.Equal(2, evt.PreviousWeight);
        Assert.Equal(7, evt.NewWeight);
    }

    [Fact]
    public void ThrowsArgumentNullException_WhenContextIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => _recorder.RecordTagRelationAdded(null!, "user", 1, 1));
        Assert.Throws<ArgumentNullException>(() => _recorder.RecordTagRelationDeleted(null!, "user", 1, 1, 1));
        Assert.Throws<ArgumentNullException>(() => _recorder.RecordTagRelationUpdated(null!, "user", 1, 1, 1, 2));
    }
}