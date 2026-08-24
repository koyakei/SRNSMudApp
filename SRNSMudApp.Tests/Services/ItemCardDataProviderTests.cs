#region

using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Services;
using SRNSMudApp.Tests.TestSupport;

using Xunit;

#endregion

namespace SRNSMudApp.Tests.Services;

/// <summary>
///     <see cref="ItemCardDataProvider" /> の投票トグルロジックの単体テスト (MSSQL Testcontainers)。
/// </summary>
[Collection(MsSqlCollection.Name)]
public class ItemCardDataProviderTests : IAsyncLifetime
{
    private const string UserId = "voter";
    private int _goodTagId;
    private int _itemId;

    private readonly MsSqlContainerFixture _fixture;
    private MsSqlTestDatabase _testDb = null!;
    private ApplicationDbContext _db = null!;
    private ItemCardDataProvider _sut = null!;

    public ItemCardDataProviderTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _testDb = await MsSqlTestDatabase.CreateAsync(_fixture.ConnectionString, nameof(ItemCardDataProviderTests));
        _db = new ApplicationDbContext(_testDb.Options);
        _sut = new ItemCardDataProvider(new DbContextFactoryStub(_testDb.Options));

        var tag = new Tag { Name = "good", IsSystem = true, OwnerId = "system", CachedWeight = 0 };
        var item = new SRNSMudApp.Data.Item { Content = "target", OwnerId = "author" };

        _db.Users.AddRange(
            new ApplicationUser { Id = UserId, UserName = UserId },
            new ApplicationUser { Id = "system", UserName = "system" },
            new ApplicationUser { Id = "author", UserName = "author" });
        _db.Tags.Add(tag);
        _db.Items.Add(item);
        _ = await _db.SaveChangesAsync();

        _goodTagId = tag.Id;
        _itemId = item.Id;
    }

    [Fact]
    public async Task ToggleItemVote_FirstClick_AddsRelationWithTargetWeight()
    {
        ItemVoteResult result = await _sut.ToggleItemVoteAsync(_itemId, UserId, _goodTagId, 1);

        Assert.Equal(ItemVoteAction.Added, result.Action);
        Assert.Equal(1, result.Weight);
        TagRelation? relation = await _db.TagRelations.SingleAsync(tr => tr.ItemId == _itemId && tr.OwnerId == UserId);
        Assert.Equal(_goodTagId, relation.TagId);
        Assert.Equal(1, relation.Weight);
    }

    [Fact]
    public async Task ToggleItemVote_SecondDifferentWeight_UpdatesAndWritesLedger()
    {
        _ = await _sut.ToggleItemVoteAsync(_itemId, UserId, _goodTagId, 1);

        ItemVoteResult result = await _sut.ToggleItemVoteAsync(_itemId, UserId, _goodTagId, -1);

        Assert.Equal(ItemVoteAction.Updated, result.Action);
        TagRelation relation = await _db.TagRelations.SingleAsync(tr => tr.Id == result.RelationId);
        Assert.Equal(-1, relation.Weight);
        TagWeightLedger ledger = await _db.TagWeightLedgers.SingleAsync(l => l.SourceType == "TagRelationUpdate");
        Assert.Equal(-2, ledger.Delta);
        Assert.Equal(-2, ledger.NewWeight - ledger.PreviousWeight);
        TimelineEvent timeline = await _db.TimelineEvents!.SingleAsync(e => e.EventType == "Update");
        Assert.Equal(1, timeline.PreviousWeight);
        Assert.Equal(-1, timeline.NewWeight);
    }

    [Fact]
    public async Task ToggleItemVote_SameWeightTwice_CancelsVoteAndRemovesRelation()
    {
        _ = await _sut.ToggleItemVoteAsync(_itemId, UserId, _goodTagId, 1);
        var cachedAfterAdd = (await _db.Tags.FindAsync(_goodTagId))!.CachedWeight;

        ItemVoteResult result = await _sut.ToggleItemVoteAsync(_itemId, UserId, _goodTagId, 1);

        Assert.Equal(ItemVoteAction.Removed, result.Action);
        Assert.False(await _db.TagRelations.AnyAsync(tr => tr.OwnerId == UserId));
        // 取り消しで CachedWeight が追加直後の値に戻ること
        Assert.Equal(cachedAfterAdd, (await _db.Tags.FindAsync(_goodTagId))!.CachedWeight);
        TimelineEvent timeline = await _db.TimelineEvents!.SingleAsync(e => e.EventType == "Delete");
        Assert.Equal(1, timeline.PreviousWeight);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _testDb.DisposeAsync();
    }

    /// <summary>テスト用のシンプルなファクトリ。</summary>
    private sealed class DbContextFactoryStub(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new ApplicationDbContext(options);

        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }
}