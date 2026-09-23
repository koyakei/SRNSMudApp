#region

using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;

#endregion

namespace SRNSMudApp.Tests.Services;

/// <summary>
///     <see cref="RightAssetDataProvider" /> の RightAsset 集計・取得ロジックの単体テスト (MSSQL Testcontainers)。
/// </summary>
public class RightAssetDataProviderTests : IAsyncLifetime
{
    private MsSqlTestDatabase _sharedDb = null!;

    public async Task InitializeAsync()
    {
        _sharedDb = await SharedMsSqlTestDatabase.GetInstanceAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(ApplicationDbContext db, RightAssetDataProvider sut, string userA, string userB, int tagId, string tid)> CreateScopeAsync()
    {
        var tid = Guid.NewGuid().ToString("N")[..8];
        var db = new ApplicationDbContext(_sharedDb.Options);
        var stubFactory = new DbContextFactoryStub(_sharedDb.Options);
        var sut = new RightAssetDataProvider(stubFactory);

        var userA = $"userA_{tid}";
        var userB = $"userB_{tid}";
        await db.SeedUsersAsync(userA, userB);

        var tag = new Tag
        {
            Name = $"TestTag_{tid}",
            Content = "RightAsset Test Tag",
            IsSystem = false,
            OwnerId = userA,
            CachedWeight = 10
        };

        db.Tags.Add(tag);
        await db.SaveChangesAsync();

        return (db, sut, userA, userB, tag.Id, tid);
    }

    [Fact]
    public async Task GetRightAssetOverviewByTagIdAsync_WhenTagNotFound_ReturnsNull()
    {
        var stubFactory = new DbContextFactoryStub(_sharedDb.Options);
        var sut = new RightAssetDataProvider(stubFactory);

        RightAssetOverviewData? result = await sut.GetRightAssetOverviewByTagIdAsync(999999);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetRightAssetOverviewByTagIdAsync_ReturnsHoldersSummaryCorrectly()
    {
        var (db, sut, userA, userB, tagId, tid) = await CreateScopeAsync();
        await using (db)
        {
            // 別タグを用意して、集計が混ざらないことを確認
            var otherTag = new Tag { Name = $"OtherTag_{tid}", OwnerId = userA, CachedWeight = 0 };
            db.Tags.Add(otherTag);
            await db.SaveChangesAsync();

            // UserA: 有効アセット 10, 5 / 燃焼済アセット 3
            var assetA1 = new RightAsset { TargetTagId = tagId, OwnerId = userA, Amount = 10, IsBurned = false };
            var assetA2 = new RightAsset { TargetTagId = tagId, OwnerId = userA, Amount = 5, IsBurned = false };
            var assetABurned = new RightAsset { TargetTagId = tagId, OwnerId = userA, Amount = 3, IsBurned = true };

            // UserB: 有効アセット 20
            var assetB1 = new RightAsset { TargetTagId = tagId, OwnerId = userB, Amount = 20, IsBurned = false };

            // 他タグのアセット
            var otherAsset = new RightAsset { TargetTagId = otherTag.Id, OwnerId = userA, Amount = 50, IsBurned = false };

            db.RightAssets.AddRange(assetA1, assetA2, assetABurned, assetB1, otherAsset);
            await db.SaveChangesAsync();

            // Act
            RightAssetOverviewData? overview = await sut.GetRightAssetOverviewByTagIdAsync(tagId);

            // Assert
            Assert.NotNull(overview);
            Assert.Equal(tagId, overview.Tag.Id);
            Assert.Equal(2, overview.TotalHoldersCount); // UserA and UserB (保有量 > 0)
            Assert.Equal(35, overview.TotalActiveAmount); // 10 + 5 + 20
            Assert.Equal(3, overview.TotalActiveAssetsCount);
            Assert.Equal(3, overview.TotalBurnedAmount);

            // Holders の検証 (UserB が 20 で先頭、UserA が 15 で 2番目)
            Assert.Equal(2, overview.Holders.Count);

            RightAssetHolderSummary firstHolder = overview.Holders[0];
            Assert.Equal(userB, firstHolder.UserId);
            Assert.Equal(20, firstHolder.TotalAmount);
            Assert.Equal(1, firstHolder.ActiveAssetCount);
            Assert.Equal(0, firstHolder.BurnedAmount);

            RightAssetHolderSummary secondHolder = overview.Holders[1];
            Assert.Equal(userA, secondHolder.UserId);
            Assert.Equal(15, secondHolder.TotalAmount);
            Assert.Equal(2, secondHolder.ActiveAssetCount);
            Assert.Equal(3, secondHolder.BurnedAmount);
            Assert.Equal(1, secondHolder.BurnedAssetCount);

            // 個別アセット明細の検証 (該当タグの全4件)
            Assert.Equal(4, overview.Assets.Count);
        }
    }

    [Fact]
    public async Task GetTopTagsWithRightAssetsAsync_ReturnsTagsOrderedByActiveAmount()
    {
        var (db, sut, userA, _, tag1Id, tid) = await CreateScopeAsync();
        await using (db)
        {
            var tag2 = new Tag { Name = $"TopTag2_{tid}", OwnerId = userA };
            var tag3 = new Tag { Name = $"TopTag3_{tid}", OwnerId = userA };
            db.Tags.AddRange(tag2, tag3);
            await db.SaveChangesAsync();

            // tag1: 10, tag2: 30, tag3: 5
            db.RightAssets.AddRange(
                new RightAsset { TargetTagId = tag1Id, OwnerId = userA, Amount = 10, IsBurned = false },
                new RightAsset { TargetTagId = tag2.Id, OwnerId = userA, Amount = 30, IsBurned = false },
                new RightAsset { TargetTagId = tag3.Id, OwnerId = userA, Amount = 5, IsBurned = false }
            );
            await db.SaveChangesAsync();

            // Act
            IReadOnlyList<TagRightAssetSummary> topTags = await sut.GetTopTagsWithRightAssetsAsync(50);

            // Assert
            Assert.True(topTags.Count >= 3);
            TagRightAssetSummary? top1 = topTags.FirstOrDefault(t => t.TagId == tag2.Id);
            TagRightAssetSummary? top2 = topTags.FirstOrDefault(t => t.TagId == tag1Id);
            TagRightAssetSummary? top3 = topTags.FirstOrDefault(t => t.TagId == tag3.Id);

            Assert.NotNull(top1);
            Assert.Equal(30, top1.TotalAmount);
            Assert.NotNull(top2);
            Assert.Equal(10, top2.TotalAmount);
            Assert.NotNull(top3);
            Assert.Equal(5, top3.TotalAmount);
        }
    }

    [Fact]
    public async Task GetAvailableRightAssetsForUserAsync_ReturnsOnlyActiveAssetsForUser()
    {
        var (db, sut, userA, userB, tag1Id, tid) = await CreateScopeAsync();
        await using (db)
        {
            var tag2 = new Tag { Name = $"MyTag2_{tid}", OwnerId = userA };
            db.Tags.Add(tag2);
            await db.SaveChangesAsync();

            // userA: 有効 10 (tag1), 有効 20 (tag2), 燃焼済み 5 (tag1)
            // userB: 有効 15 (tag1)
            db.RightAssets.AddRange(
                new RightAsset { TargetTagId = tag1Id, OwnerId = userA, Amount = 10, IsBurned = false },
                new RightAsset { TargetTagId = tag2.Id, OwnerId = userA, Amount = 20, IsBurned = false },
                new RightAsset { TargetTagId = tag1Id, OwnerId = userA, Amount = 5, IsBurned = true },
                new RightAsset { TargetTagId = tag1Id, OwnerId = userB, Amount = 15, IsBurned = false }
            );
            await db.SaveChangesAsync();

            // Act
            IReadOnlyList<UserAvailableRightAssetDto> assets = await sut.GetAvailableRightAssetsForUserAsync(userA);

            // Assert
            Assert.Equal(2, assets.Count);
            Assert.Contains(assets, a => a.TargetTagId == tag1Id && a.Amount == 10);
            Assert.Contains(assets, a => a.TargetTagId == tag2.Id && a.Amount == 20);
        }
    }

    [Fact]
    public async Task SubmitPermissionRequestAsync_WhenValidGratisRequest_SavesItemAndReturnsSuccess()
    {
        var (db, sut, userA, userB, tagId, _) = await CreateScopeAsync();
        await using (db)
        {
            var request = new TagPermissionRequestDto(
                RequestedTagId: tagId,
                TargetUserId: userA, // userB requests to userA
                RequestedAmount: 5,
                OfferedRightAssetId: null,
                OfferedAmount: 0,
                Message: "分類整理のため5ください"
            );

            // Act
            var result = await sut.SubmitPermissionRequestAsync(userB, request);

            // Assert
            Assert.True(result is Success<bool>);

            // DB にメッセージ Item が作られていること
            var item = await db.Items
                .Include(i => i.NotificationRecipients)
                .FirstOrDefaultAsync(i => i.OwnerId == userB);

            Assert.NotNull(item);
            Assert.Contains("タグ操作権限リクエスト", item.Content);
            Assert.Contains("無償リクエスト", item.Content);
            Assert.Contains("分類整理のため5ください", item.Content);
            Assert.Single(item.NotificationRecipients);
            Assert.Equal(userA, item.NotificationRecipients.First().RecipientUserId);
        }
    }

    [Fact]
    public async Task SubmitPermissionRequestAsync_WhenValidWithOfferedAsset_SavesItemAndReturnsSuccess()
    {
        var (db, sut, userA, userB, tag1Id, tid) = await CreateScopeAsync();
        await using (db)
        {
            var tag2 = new Tag { Name = $"OfferTag_{tid}", OwnerId = userB };
            db.Tags.Add(tag2);
            await db.SaveChangesAsync();

            var offeredAsset = new RightAsset { TargetTagId = tag2.Id, OwnerId = userB, Amount = 10, IsBurned = false };
            db.RightAssets.Add(offeredAsset);
            await db.SaveChangesAsync();

            var request = new TagPermissionRequestDto(
                RequestedTagId: tag1Id,
                TargetUserId: userA,
                RequestedAmount: 3,
                OfferedRightAssetId: offeredAsset.Id,
                OfferedAmount: 2,
                Message: "交換お願いします"
            );

            // Act
            var result = await sut.SubmitPermissionRequestAsync(userB, request);

            // Assert
            Assert.True(result is Success<bool>);

            var item = await db.Items.FirstOrDefaultAsync(i => i.OwnerId == userB);
            Assert.NotNull(item);
            Assert.Contains(tag2.Name, item.Content);
            Assert.Contains("対価:", item.Content);
        }
    }

    [Fact]
    public async Task SubmitPermissionRequestAsync_WhenRequestingSelf_ReturnsFailure()
    {
        var (_, sut, userA, _, tagId, _) = await CreateScopeAsync();

        var request = new TagPermissionRequestDto(
            RequestedTagId: tagId,
            TargetUserId: userA,
            RequestedAmount: 1
        );

        var result = await sut.SubmitPermissionRequestAsync(userA, request);

        Assert.True(result is Failure fail && fail.ErrorMessage.Contains("自分自身"));
    }


    private sealed class DbContextFactoryStub(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);

        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}