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
}