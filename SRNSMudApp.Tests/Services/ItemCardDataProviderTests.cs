#region

using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Services;

using Xunit;

#endregion

namespace SRNSMudApp.Tests.Services;

/// <summary>
///     <see cref="ItemCardDataProvider" /> の投票トグルロジックの単体テスト (InMemory DB)。
/// </summary>
public class ItemCardDataProviderTests
{
    private const string UserId = "voter";
    private const int GoodTagId = 100;

    private readonly ApplicationDbContext _db;
    private readonly ItemCardDataProvider _sut;

    public ItemCardDataProviderTests()
    {
        DbContextOptions<ApplicationDbContext> options =
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
        _db = new ApplicationDbContext(options);
        _sut = new ItemCardDataProvider(
            new DbContextFactoryStub(options));

        _db.Users.Add(new ApplicationUser { Id = UserId, UserName = UserId });
        _db.Tags.Add(new Tag { Id = GoodTagId, Name = "good", IsSystem = true, OwnerId = "system", CachedWeight = 0 });
        _db.Items.Add(new SRNSMudApp.Data.Item { Id = 1, Content = "target", OwnerId = "author" });
        _ = _db.SaveChanges();
    }

    [Fact]
    public async Task ToggleItemVote_FirstClick_AddsRelationWithTargetWeight()
    {
        ItemVoteResult result = await _sut.ToggleItemVoteAsync(1, UserId, GoodTagId, 1);

        Assert.Equal(ItemVoteAction.Added, result.Action);
        Assert.Equal(1, result.Weight);
        TagRelation? relation = await _db.TagRelations.SingleAsync(tr => tr.ItemId == 1 && tr.OwnerId == UserId);
        Assert.Equal(GoodTagId, relation.TagId);
        Assert.Equal(1, relation.Weight);
    }

    [Fact]
    public async Task ToggleItemVote_SecondDifferentWeight_UpdatesAndWritesLedger()
    {
        _ = await _sut.ToggleItemVoteAsync(1, UserId, GoodTagId, 1);

        ItemVoteResult result = await _sut.ToggleItemVoteAsync(1, UserId, GoodTagId, -1);

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
        _ = await _sut.ToggleItemVoteAsync(1, UserId, GoodTagId, 1);
        var cachedAfterAdd = (await _db.Tags.FindAsync(GoodTagId))!.CachedWeight;

        ItemVoteResult result = await _sut.ToggleItemVoteAsync(1, UserId, GoodTagId, 1);

        Assert.Equal(ItemVoteAction.Removed, result.Action);
        Assert.False(await _db.TagRelations.AnyAsync(tr => tr.OwnerId == UserId));
        // 取り消しで CachedWeight が追加直後の値に戻ること
        Assert.Equal(cachedAfterAdd, (await _db.Tags.FindAsync(GoodTagId))!.CachedWeight);
        TimelineEvent timeline = await _db.TimelineEvents!.SingleAsync(e => e.EventType == "Delete");
        Assert.Equal(1, timeline.PreviousWeight);
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
    }

    /// <summary>テスト用のシンプルなファクトリ。</summary>
    private sealed class DbContextFactoryStub(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext()
        {
            return new ApplicationDbContext(options);
        }

        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(CreateDbContext());
        }
    }
}
