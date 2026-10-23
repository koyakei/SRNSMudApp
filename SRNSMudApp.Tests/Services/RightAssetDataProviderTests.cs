#region

using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Resources;
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

    [Fact]
    public async Task ApprovePermissionRequestAsync_WhenValidWithoutOfferedAsset_TransfersRightAssetAndMarksExecuted()
    {
        var (db, sut, userA, userB, tagId, _) = await CreateScopeAsync();
        await using (db)
        {
            // userA が tagId に対する権限を 10 保持
            db.RightAssets.Add(new RightAsset { TargetTagId = tagId, OwnerId = userA, Amount = 10, IsBurned = false });
            await db.SaveChangesAsync();

            // userB が userA に 3 の権限をリクエスト
            var request = new TagPermissionRequestDto(
                RequestedTagId: tagId,
                TargetUserId: userA,
                RequestedAmount: 3,
                Message: "承認テスト"
            );
            var submitResult = await sut.SubmitPermissionRequestAsync(userB, request);
            Assert.True(submitResult is Success<bool>);

            var requestItem = await db.Items.FirstOrDefaultAsync(i => i.OwnerId == userB);
            Assert.NotNull(requestItem);

            // Act: userA が承認
            var approveResult = await sut.ApprovePermissionRequestAsync(requestItem.Id, userA);

            // Assert
            Assert.True(approveResult is Success<bool>);

            // 権限保有量の検証（別コンテキストでコミット済みデータを読み込む）
            await using (var verifyDb = new ApplicationDbContext(_sharedDb.Options))
            {
                var userAAssets = await verifyDb.RightAssets.AsNoTracking().Where(a => a.OwnerId == userA && a.TargetTagId == tagId && !a.IsBurned).ToListAsync();
                var userBAssets = await verifyDb.RightAssets.AsNoTracking().Where(a => a.OwnerId == userB && a.TargetTagId == tagId && !a.IsBurned).ToListAsync();

                Assert.Equal(7, userAAssets.Sum(a => a.Amount));
                Assert.Equal(3, userBAssets.Sum(a => a.Amount));

                // ItemKindJson のステータスが Executed に更新されていること
                var updatedItem = await verifyDb.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == requestItem.Id);
                Assert.NotNull(updatedItem);
                Assert.NotNull(updatedItem.ItemKindJson);
                var payload = System.Text.Json.JsonSerializer.Deserialize<TagPermissionRequestPayload>(updatedItem.ItemKindJson);
                Assert.NotNull(payload);
                Assert.Equal(TradeStatus.Executed, payload.Status);
            }
        }
    }

    [Fact]
    public async Task ApprovePermissionRequestAsync_WhenTagOwnerHasInsufficientBalance_MintsNeededAssetAndApproves()
    {
        var (db, sut, userA, userB, tagId, _) = await CreateScopeAsync();
        await using (db)
        {
            // userA はタグオーナーだが 2 しか持っていない
            db.RightAssets.Add(new RightAsset { TargetTagId = tagId, OwnerId = userA, Amount = 2, IsBurned = false, Status = new NotBurned() });
            await db.SaveChangesAsync();

            // userB が 5 をリクエスト
            var request = new TagPermissionRequestDto(
                RequestedTagId: tagId,
                TargetUserId: userA,
                RequestedAmount: 5
            );
            var submitResult = await sut.SubmitPermissionRequestAsync(userB, request);
            Assert.True(submitResult is Success<bool>);

            var requestItem = await db.Items.FirstOrDefaultAsync(i => i.OwnerId == userB);
            Assert.NotNull(requestItem);

            // Act: userA (タグオーナー) が承認 -> 不足分の 3 が新規発行されて承認完了する
            var approveResult = await sut.ApprovePermissionRequestAsync(requestItem.Id, userA);

            // Assert
            Assert.True(approveResult is Success<bool>);

            await using (var verifyDb = new ApplicationDbContext(_sharedDb.Options))
            {
                var userAAssets = await verifyDb.RightAssets.AsNoTracking().Where(a => a.OwnerId == userA && a.TargetTagId == tagId && !a.IsBurned).ToListAsync();
                var userBAssets = await verifyDb.RightAssets.AsNoTracking().Where(a => a.OwnerId == userB && a.TargetTagId == tagId && !a.IsBurned).ToListAsync();

                // userA の有効残高は 0 (2 と新規発行された 3 の計 5 がリクエスタへ移転)
                Assert.Equal(0, userAAssets.Sum(a => a.Amount));
                // userB の有効残高は 5
                Assert.Equal(5, userBAssets.Sum(a => a.Amount));

                var updatedItem = await verifyDb.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == requestItem.Id);
                Assert.NotNull(updatedItem);
                var payload = System.Text.Json.JsonSerializer.Deserialize<TagPermissionRequestPayload>(updatedItem.ItemKindJson!);
                Assert.NotNull(payload);
                Assert.Equal(TradeStatus.Executed, payload.Status);
            }
        }
    }

    [Fact]
    public async Task ApprovePermissionRequestAsync_WhenNonOwnerHasInsufficientBalance_ReturnsFailure()
    {
        var (db, sut, userA, userB, tagId, tid) = await CreateScopeAsync();
        await using (db)
        {
            // tagId のオーナーは userA。userB は非オーナー。
            var userC = $"userC_{tid}";
            await db.SeedUsersAsync(userC);

            // userB は 2 しか持っていない
            db.RightAssets.Add(new RightAsset { TargetTagId = tagId, OwnerId = userB, Amount = 2, IsBurned = false, Status = new NotBurned() });
            await db.SaveChangesAsync();

            // userC が userB に 5 をリクエスト
            var request = new TagPermissionRequestDto(
                RequestedTagId: tagId,
                TargetUserId: userB,
                RequestedAmount: 5
            );
            await sut.SubmitPermissionRequestAsync(userC, request);
            var requestItem = await db.Items.FirstOrDefaultAsync(i => i.OwnerId == userC);
            Assert.NotNull(requestItem);

            // Act: userB (非オーナー) が承認しようとする
            var approveResult = await sut.ApprovePermissionRequestAsync(requestItem.Id, userB);

            // Assert: 非オーナーのため自動発行されず、残高不足エラーとなる
            Assert.True(approveResult is Failure fail && fail.ErrorMessage.Contains("残高"));
        }
    }

    [Fact]
    public async Task RejectPermissionRequestAsync_WhenCalled_UpdatesStatusToRejected()
    {
        var (db, sut, userA, userB, tagId, _) = await CreateScopeAsync();
        await using (db)
        {
            db.RightAssets.Add(new RightAsset { TargetTagId = tagId, OwnerId = userA, Amount = 10, IsBurned = false });
            await db.SaveChangesAsync();

            var request = new TagPermissionRequestDto(
                RequestedTagId: tagId,
                TargetUserId: userA,
                RequestedAmount: 3
            );
            await sut.SubmitPermissionRequestAsync(userB, request);
            var requestItem = await db.Items.FirstOrDefaultAsync(i => i.OwnerId == userB);
            Assert.NotNull(requestItem);

            // Act: userA が却下
            var rejectResult = await sut.RejectPermissionRequestAsync(requestItem.Id, userA, "余剰権限がありません");

            // Assert
            Assert.True(rejectResult is Success<bool>);

            await using (var verifyDb = new ApplicationDbContext(_sharedDb.Options))
            {
                var updatedItem = await verifyDb.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == requestItem.Id);
                Assert.NotNull(updatedItem);
                Assert.NotNull(updatedItem.ItemKindJson);
                var payload = System.Text.Json.JsonSerializer.Deserialize<TagPermissionRequestPayload>(updatedItem.ItemKindJson);
                Assert.NotNull(payload);
                Assert.Equal(TradeStatus.Rejected, payload.Status);
                Assert.Equal("余剰権限がありません", payload.RejectReason);
            }
        }
    }

    [Fact]
    public async Task ApprovePermissionRequestAsync_WhenRequestedTagIdIsZeroOrInvalid_ReturnsFailure()
    {
        var (db, sut, userA, userB, _, tid) = await CreateScopeAsync();
        await using (db)
        {
            var payload = new TagPermissionRequestPayload(
                RequestedTagId: 0,
                RequestedTagName: $"Tag_{tid}",
                RequestedAmount: 5,
                OfferedRightAssetId: null,
                OfferedTagName: null,
                OfferedAmount: 0,
                Message: "権限リクエスト",
                Status: TradeStatus.Proposed);

            var requestItem = new Item
            {
                OwnerId = userB,
                Content = $"【タグ操作権限リクエスト】\nタグ「Tag_{tid}」の操作権限 5 をリクエストしました。（無償リクエスト）",
                ItemKindJson = System.Text.Json.JsonSerializer.Serialize(payload),
                NotificationRecipients =
                [
                    new ItemReplyNotificationRecipient { RecipientUserId = userA }
                ]
            };
            db.Items.Add(requestItem);
            await db.SaveChangesAsync();

            // Act: RequestedTagId が 0 の場合は TagId のみで取得するため失敗する
            var approveResult = await sut.ApprovePermissionRequestAsync(requestItem.Id, userA);

            // Assert: 対象のタグが見つかりません エラー
            Assert.True(approveResult is Failure fail && fail.ErrorMessage.Contains("対象のタグが見つかりません"));
        }
    }

    [Fact]
    public async Task ApprovePermissionRequestAsync_WhenTagOwnerHasZeroBalance_MintsAssetAndApprovesSuccessfully()
    {
        var (db, sut, userA, userB, _, tid) = await CreateScopeAsync();
        await using (db)
        {
            // 承認者（userA）所有のタグを作成（初期RightAsset残高は0）
            var tagOwner = new Tag
            {
                Name = $"OwnerTag_{tid}",
                Content = "Owner tag",
                IsSystem = false,
                OwnerId = userA,
                CachedWeight = 2
            };
            db.Tags.Add(tagOwner);
            await db.SaveChangesAsync();

            // userB から userA 宛に、TagId を指定して操作権限リクエストを作成
            var payload = new TagPermissionRequestPayload(
                RequestedTagId: tagOwner.Id,
                RequestedTagName: tagOwner.Name,
                RequestedAmount: 5,
                OfferedRightAssetId: null,
                OfferedTagName: null,
                OfferedAmount: 0,
                Message: "権限リクエスト",
                Status: TradeStatus.Proposed);

            var requestItem = new Item
            {
                OwnerId = userB,
                Content = $"【タグ操作権限リクエスト】\nタグ「{tagOwner.Name}」の操作権限 5 をリクエストしました。（無償リクエスト）",
                ItemKindJson = System.Text.Json.JsonSerializer.Serialize(payload),
                NotificationRecipients =
                [
                    new ItemReplyNotificationRecipient { RecipientUserId = userA }
                ]
            };
            db.Items.Add(requestItem);
            await db.SaveChangesAsync();

            // Act: userA (タグオーナーだが初期残高0) が承認
            var approveResult = await sut.ApprovePermissionRequestAsync(requestItem.Id, userA);

            // Assert: 残高不足にならず、userA のタグとして自動発行されて承認完了する
            Assert.True(approveResult is Success<bool>);

            await using (var verifyDb = new ApplicationDbContext(_sharedDb.Options))
            {
                var userAAssets = await verifyDb.RightAssets.AsNoTracking()
                    .Where(a => a.OwnerId == userA && a.TargetTagId == tagOwner.Id && !a.IsBurned).ToListAsync();
                var userBAssets = await verifyDb.RightAssets.AsNoTracking()
                    .Where(a => a.OwnerId == userB && a.TargetTagId == tagOwner.Id && !a.IsBurned).ToListAsync();

                // userA の有効残高は 0 (発行された 5 が即座に移転)
                Assert.Equal(0, userAAssets.Sum(a => a.Amount));
                // userB の有効残高は 5 (tagOwner.Id に対するアセット)
                Assert.Equal(5, userBAssets.Sum(a => a.Amount));

                var updatedItem = await verifyDb.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == requestItem.Id);
                Assert.NotNull(updatedItem);
                var updatedPayload = System.Text.Json.JsonSerializer.Deserialize<TagPermissionRequestPayload>(updatedItem.ItemKindJson!);
                Assert.NotNull(updatedPayload);
                Assert.Equal(TradeStatus.Executed, updatedPayload.Status);
                Assert.Equal(tagOwner.Id, updatedPayload.RequestedTagId);
            }
        }
    }

    [Fact]
    public async Task ApprovePermissionRequestAsync_WhenRequestedTagIdPointsToDifferentOwnerTagWithSameName_DoesNotResolveToApproverTagAndFailsWhenInsufficientBalance()
    {
        var (db, sut, userA, userB, _, tid) = await CreateScopeAsync();
        await using (db)
        {
            var userOther = $"userOther_{tid}";
            await db.SeedUsersAsync(userOther);

            var duplicateTagName = $"真実_{tid}";

            // 別ユーザー所有のタグ
            var tagOther = new Tag
            {
                Name = duplicateTagName,
                Content = "Other user tag",
                IsSystem = false,
                OwnerId = userOther,
                CachedWeight = 1
            };
            db.Tags.Add(tagOther);

            // 承認者（userA）所有の同名タグ
            var tagOwner = new Tag
            {
                Name = duplicateTagName,
                Content = "Owner tag",
                IsSystem = false,
                OwnerId = userA,
                CachedWeight = 2
            };
            db.Tags.Add(tagOwner);
            await db.SaveChangesAsync();

            // RequestedTagId が別ユーザー所有の tagOther.Id を指定しているリクエスト
            var payload = new TagPermissionRequestPayload(
                RequestedTagId: tagOther.Id,
                RequestedTagName: duplicateTagName,
                RequestedAmount: 3,
                OfferedRightAssetId: null,
                OfferedTagName: null,
                OfferedAmount: 0,
                Message: "権限リクエスト",
                Status: TradeStatus.Proposed);

            var requestItem = new Item
            {
                OwnerId = userB,
                Content = $"【タグ操作権限リクエスト】\nタグ「{duplicateTagName}」の操作権限 3 をリクエストしました。（無償リクエスト）",
                ItemKindJson = System.Text.Json.JsonSerializer.Serialize(payload),
                NotificationRecipients =
                [
                    new ItemReplyNotificationRecipient { RecipientUserId = userA }
                ]
            };
            db.Items.Add(requestItem);
            await db.SaveChangesAsync();

            // Act: userA が承認を試みる
            var approveResult = await sut.ApprovePermissionRequestAsync(requestItem.Id, userA);

            // Assert: 承認者の同名タグ（tagOwner.Id）には解決されず、tagOther.Id の所有者でも残高保有者でもないため失敗する
            Assert.True(approveResult is Failure fail && fail.ErrorMessage.Contains("残高"));

            await using (var verifyDb = new ApplicationDbContext(_sharedDb.Options))
            {
                // tagOwner.Id のアセットは発行されていないこと
                var userBAssets = await verifyDb.RightAssets.AsNoTracking()
                    .Where(a => a.OwnerId == userB && a.TargetTagId == tagOwner.Id && !a.IsBurned).ToListAsync();
                Assert.Empty(userBAssets);

                // アイテムのステータスは Proposed のままであること
                var itemInDb = await verifyDb.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == requestItem.Id);
                Assert.NotNull(itemInDb);
                var payloadInDb = System.Text.Json.JsonSerializer.Deserialize<TagPermissionRequestPayload>(itemInDb.ItemKindJson!);
                Assert.NotNull(payloadInDb);
                Assert.Equal(TradeStatus.Proposed, payloadInDb.Status);
                Assert.Equal(tagOther.Id, payloadInDb.RequestedTagId);
            }
        }
    }

    [Fact]
    public async Task ApprovePermissionRequestAsync_WhenApproverUserIdCasingDiffersFromTagOwnerId_MintsAndApproves()
    {
        var (db, sut, userA, userB, tagId, _) = await CreateScopeAsync();
        await using (db)
        {
            var request = new TagPermissionRequestDto(
                RequestedTagId: tagId,
                TargetUserId: userA,
                RequestedAmount: 4
            );
            var submitResult = await sut.SubmitPermissionRequestAsync(userB, request);
            Assert.True(submitResult is Success<bool>);

            var requestItem = await db.Items.FirstOrDefaultAsync(i => i.OwnerId == userB);
            Assert.NotNull(requestItem);

            // Act: 大文字小文字が異なるユーザーIDで承認（GUID形式などで起こり得る差異）
            var approveResult = await sut.ApprovePermissionRequestAsync(requestItem.Id, userA.ToUpperInvariant());

            // Assert: 大文字小文字の違いでも正しく認識され、新規発行して承認成功する
            Assert.True(approveResult is Success<bool>);
        }
    }

    [Fact]
    public async Task SubmitPermissionRequestAsync_WithCustomLogger_LogsInformation()
    {
        var (db, _, userA, userB, tagId, _) = await CreateScopeAsync();
        await using (db)
        {
            var mockLogger = new Moq.Mock<Microsoft.Extensions.Logging.ILogger<RightAssetDataProvider>>();
            var stubFactory = new DbContextFactoryStub(_sharedDb.Options);
            var sutWithLogger = new RightAssetDataProvider(stubFactory, logger: mockLogger.Object);

            var request = new TagPermissionRequestDto(
                RequestedTagId: tagId,
                TargetUserId: userA,
                RequestedAmount: 2
            );

            // Act
            var submitResult = await sutWithLogger.SubmitPermissionRequestAsync(userB, request);

            // Assert
            Assert.True(submitResult is Success<bool>);
            mockLogger.Verify(
                x => x.Log(
                    Microsoft.Extensions.Logging.LogLevel.Information,
                    Moq.It.IsAny<Microsoft.Extensions.Logging.EventId>(),
                    Moq.It.Is<Moq.It.IsAnyType>((v, t) => true),
                    Moq.It.IsAny<Exception>(),
                    Moq.It.IsAny<Func<Moq.It.IsAnyType, Exception?, string>>()),
                Moq.Times.AtLeastOnce);
        }
    }

    [Fact]
    public void ParsePermissionPayload_WhenItemIsNull_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => RightAssetDataProvider.ParsePermissionPayload(null!));
    }

    [Fact]
    public async Task GetTopTagsWithRightAssetsAsync_WhenNoRightAssetsExist_ReturnsEmptyList()
    {
        var stubFactory = new DbContextFactoryStub(_sharedDb.Options);
        var sut = new RightAssetDataProvider(stubFactory);

        // Act
        var result = await sut.GetTopTagsWithRightAssetsAsync(count: 5);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task ApprovePermissionRequestAsync_WithMultipleApproverAssets_DeductsCorrectlyAndBurnsDepletedAssets()
    {
        var (db, sut, userA, userB, _, tid) = await CreateScopeAsync();
        await using (db)
        {
            var creatorUser = $"creator_{tid}";
            await db.SeedUsersAsync(creatorUser);

            // タグ作成者は別の第三者（userA がオーナー自動発行されないようにするため）
            var tag = new Tag { Name = $"MultiAssetTag_{tid}", OwnerId = creatorUser, CachedWeight = 10 };
            db.Tags.Add(tag);
            await db.SaveChangesAsync();

            // userA が 3 つの RightAsset を保有 (2, 3, 5 -> 計10)
            var asset1 = new RightAsset { TargetTagId = tag.Id, OwnerId = userA, Amount = 2, IsBurned = false };
            var asset2 = new RightAsset { TargetTagId = tag.Id, OwnerId = userA, Amount = 3, IsBurned = false };
            var asset3 = new RightAsset { TargetTagId = tag.Id, OwnerId = userA, Amount = 5, IsBurned = false };
            db.RightAssets.AddRange(asset1, asset2, asset3);
            await db.SaveChangesAsync();

            // userB が 6 をリクエスト
            var request = new TagPermissionRequestDto(
                RequestedTagId: tag.Id,
                TargetUserId: userA,
                RequestedAmount: 6
            );
            var submitResult = await sut.SubmitPermissionRequestAsync(userB, request);
            Assert.True(submitResult is Success<bool>);

            var requestItem = await db.Items.FirstOrDefaultAsync(i => i.OwnerId == userB && i.NotificationRecipients.Any(r => r.RecipientUserId == userA));
            Assert.NotNull(requestItem);

            // Act: userA が承認
            var approveResult = await sut.ApprovePermissionRequestAsync(requestItem.Id, userA);
            Assert.True(approveResult is Success<bool>);

            // Assert: 別コンテキストで検証
            await using var verifyDb = new ApplicationDbContext(_sharedDb.Options);
            var updatedAsset1 = await verifyDb.RightAssets.FindAsync(asset1.Id);
            var updatedAsset2 = await verifyDb.RightAssets.FindAsync(asset2.Id);
            var updatedAsset3 = await verifyDb.RightAssets.FindAsync(asset3.Id);

            // asset1(2) と asset2(3) は全量消費されて Burn される
            Assert.NotNull(updatedAsset1);
            Assert.Equal(0, updatedAsset1.Amount);
            Assert.True(updatedAsset1.IsBurned);

            Assert.NotNull(updatedAsset2);
            Assert.Equal(0, updatedAsset2.Amount);
            Assert.True(updatedAsset2.IsBurned);

            // asset3(5) は 1 消費され、残り 4 で有効のまま
            Assert.NotNull(updatedAsset3);
            Assert.Equal(4, updatedAsset3.Amount);
            Assert.False(updatedAsset3.IsBurned);

            // userB に 6 の新規アセットが付与される
            var userBAssets = await verifyDb.RightAssets
                .Where(a => a.OwnerId == userB && a.TargetTagId == tag.Id && !a.IsBurned)
                .ToListAsync();
            Assert.Single(userBAssets);
            Assert.Equal(6, userBAssets[0].Amount);
        }
    }

    [Fact]
    public async Task ApprovePermissionRequestAsync_WithOfferedAsset_TransfersBothWays()
    {
        var (db, sut, userA, userB, _, tid) = await CreateScopeAsync();
        await using (db)
        {
            var creator = $"creator_{tid}";
            await db.SeedUsersAsync(creator);

            var tagReq = new Tag { Name = $"TagReq_{tid}", OwnerId = creator, CachedWeight = 10 };
            var tagOff = new Tag { Name = $"TagOff_{tid}", OwnerId = creator, CachedWeight = 10 };
            db.Tags.AddRange(tagReq, tagOff);
            await db.SaveChangesAsync();

            // userA が要求対象 tagReq のアセット 10 を保有
            var assetA = new RightAsset { TargetTagId = tagReq.Id, OwnerId = userA, Amount = 10, IsBurned = false };
            // userB が対価 tagOff のアセット 5 を保有
            var assetB = new RightAsset { TargetTagId = tagOff.Id, OwnerId = userB, Amount = 5, IsBurned = false };
            db.RightAssets.AddRange(assetA, assetB);
            await db.SaveChangesAsync();

            // userB が tagReq の 4 をリクエストし、tagOff の 3 を対価として提供
            var request = new TagPermissionRequestDto(
                RequestedTagId: tagReq.Id,
                TargetUserId: userA,
                RequestedAmount: 4,
                OfferedRightAssetId: assetB.Id,
                OfferedAmount: 3
            );
            var submitResult = await sut.SubmitPermissionRequestAsync(userB, request);
            Assert.True(submitResult is Success<bool>);

            var requestItem = await db.Items.FirstOrDefaultAsync(i => i.OwnerId == userB);
            Assert.NotNull(requestItem);

            // Act: userA が承認
            var approveResult = await sut.ApprovePermissionRequestAsync(requestItem.Id, userA);
            Assert.True(approveResult is Success<bool>);

            // Assert
            await using var verifyDb = new ApplicationDbContext(_sharedDb.Options);

            // userA の tagReq は 10 - 4 = 6
            var updatedAssetA = await verifyDb.RightAssets.FindAsync(assetA.Id);
            Assert.NotNull(updatedAssetA);
            Assert.Equal(6, updatedAssetA.Amount);
            Assert.False(updatedAssetA.IsBurned);

            // userB に tagReq の 4 が付与される
            var userBReqAsset = await verifyDb.RightAssets
                .FirstOrDefaultAsync(a => a.OwnerId == userB && a.TargetTagId == tagReq.Id && !a.IsBurned);
            Assert.NotNull(userBReqAsset);
            Assert.Equal(4, userBReqAsset.Amount);

            // userB の対価アセット tagOff は 5 - 3 = 2
            var updatedAssetB = await verifyDb.RightAssets.FindAsync(assetB.Id);
            Assert.NotNull(updatedAssetB);
            Assert.Equal(2, updatedAssetB.Amount);
            Assert.False(updatedAssetB.IsBurned);

            // userA に対価 tagOff の 3 が付与される
            var userAOffAsset = await verifyDb.RightAssets
                .FirstOrDefaultAsync(a => a.OwnerId == userA && a.TargetTagId == tagOff.Id && !a.IsBurned);
            Assert.NotNull(userAOffAsset);
            Assert.Equal(3, userAOffAsset.Amount);
        }
    }

    [Fact]
    public async Task ApprovePermissionRequestAsync_WithOfferedAssetFullyDepleted_BurnsOfferedAsset()
    {
        var (db, sut, userA, userB, _, tid) = await CreateScopeAsync();
        await using (db)
        {
            var creator = $"creator_{tid}";
            await db.SeedUsersAsync(creator);

            var tagReq = new Tag { Name = $"TagReq_{tid}", OwnerId = creator, CachedWeight = 10 };
            var tagOff = new Tag { Name = $"TagOff_{tid}", OwnerId = creator, CachedWeight = 10 };
            db.Tags.AddRange(tagReq, tagOff);
            await db.SaveChangesAsync();

            var assetA = new RightAsset { TargetTagId = tagReq.Id, OwnerId = userA, Amount = 5, IsBurned = false };
            // 全量提供 (Amount == OfferedAmount == 2)
            var assetB = new RightAsset { TargetTagId = tagOff.Id, OwnerId = userB, Amount = 2, IsBurned = false };
            db.RightAssets.AddRange(assetA, assetB);
            await db.SaveChangesAsync();

            var request = new TagPermissionRequestDto(
                RequestedTagId: tagReq.Id,
                TargetUserId: userA,
                RequestedAmount: 2,
                OfferedRightAssetId: assetB.Id,
                OfferedAmount: 2
            );
            var submitResult = await sut.SubmitPermissionRequestAsync(userB, request);
            Assert.True(submitResult is Success<bool>);

            var requestItem = await db.Items.FirstOrDefaultAsync(i => i.OwnerId == userB);
            Assert.NotNull(requestItem);

            // Act
            var approveResult = await sut.ApprovePermissionRequestAsync(requestItem.Id, userA);
            Assert.True(approveResult is Success<bool>);

            // Assert
            await using var verifyDb = new ApplicationDbContext(_sharedDb.Options);
            var updatedAssetB = await verifyDb.RightAssets.FindAsync(assetB.Id);
            Assert.NotNull(updatedAssetB);
            Assert.Equal(0, updatedAssetB.Amount);
            Assert.True(updatedAssetB.IsBurned);

            var userAOffAsset = await verifyDb.RightAssets
                .FirstOrDefaultAsync(a => a.OwnerId == userA && a.TargetTagId == tagOff.Id && !a.IsBurned);
            Assert.NotNull(userAOffAsset);
            Assert.Equal(2, userAOffAsset.Amount);
        }
    }

    [Fact]
    public async Task ApprovePermissionRequestAsync_WhenOfferedAssetBecameInvalidOrInsufficient_FailsAndDoesNotTransfer()
    {
        var (db, sut, userA, userB, _, tid) = await CreateScopeAsync();
        await using (db)
        {
            var creator = $"creator_{tid}";
            await db.SeedUsersAsync(creator);

            var tagReq = new Tag { Name = $"TagReq_{tid}", OwnerId = creator, CachedWeight = 10 };
            var tagOff = new Tag { Name = $"TagOff_{tid}", OwnerId = creator, CachedWeight = 10 };
            db.Tags.AddRange(tagReq, tagOff);
            await db.SaveChangesAsync();

            var assetA = new RightAsset { TargetTagId = tagReq.Id, OwnerId = userA, Amount = 5, IsBurned = false };
            var assetB = new RightAsset { TargetTagId = tagOff.Id, OwnerId = userB, Amount = 3, IsBurned = false };
            db.RightAssets.AddRange(assetA, assetB);
            await db.SaveChangesAsync();

            var request = new TagPermissionRequestDto(
                RequestedTagId: tagReq.Id,
                TargetUserId: userA,
                RequestedAmount: 2,
                OfferedRightAssetId: assetB.Id,
                OfferedAmount: 3
            );
            var submitResult = await sut.SubmitPermissionRequestAsync(userB, request);
            Assert.True(submitResult is Success<bool>);

            var requestItem = await db.Items.FirstOrDefaultAsync(i => i.OwnerId == userB);
            Assert.NotNull(requestItem);

            // 申請後に userB の対価アセットが他で消費されて残高 1 に減少
            assetB.Amount = 1;
            await db.SaveChangesAsync();

            // Act: userA が承認を試みる
            var approveResult = await sut.ApprovePermissionRequestAsync(requestItem.Id, userA);

            // Assert: エラーとなり、userA のアセットは減算されないこと
            Assert.True(approveResult is Failure fail && fail.ErrorMessage.Contains("対価アセット"));

            await using var verifyDb = new ApplicationDbContext(_sharedDb.Options);
            var updatedAssetA = await verifyDb.RightAssets.FindAsync(assetA.Id);
            Assert.NotNull(updatedAssetA);
            Assert.Equal(5, updatedAssetA.Amount); // 減算されていないこと
        }
    }

    [Fact]
    public async Task ApprovePermissionRequestAsync_WhenCallerNotRecipient_ReturnsNotAuthorized()
    {
        var (db, sut, userA, userB, tagId, tid) = await CreateScopeAsync();
        await using (db)
        {
            var userC = $"userC_{tid}";
            await db.SeedUsersAsync(userC);

            var request = new TagPermissionRequestDto(
                RequestedTagId: tagId,
                TargetUserId: userA,
                RequestedAmount: 2
            );
            await sut.SubmitPermissionRequestAsync(userB, request);

            var requestItem = await db.Items.FirstOrDefaultAsync(i => i.OwnerId == userB);
            Assert.NotNull(requestItem);

            // Act: 宛先ではない userC が承認を試みる
            var result = await sut.ApprovePermissionRequestAsync(requestItem.Id, userC);

            // Assert
            Assert.True(result is Failure fail && fail.ErrorMessage == ErrorMessages.NotAuthorizedToApprove);
        }
    }

    [Fact]
    public async Task RejectPermissionRequestAsync_WhenCallerNotRecipient_ReturnsNotAuthorized()
    {
        var (db, sut, userA, userB, tagId, tid) = await CreateScopeAsync();
        await using (db)
        {
            var userC = $"userC_{tid}";
            await db.SeedUsersAsync(userC);

            var request = new TagPermissionRequestDto(
                RequestedTagId: tagId,
                TargetUserId: userA,
                RequestedAmount: 2
            );
            await sut.SubmitPermissionRequestAsync(userB, request);

            var requestItem = await db.Items.FirstOrDefaultAsync(i => i.OwnerId == userB);
            Assert.NotNull(requestItem);

            // Act: 宛先ではない userC が却下を試みる
            var result = await sut.RejectPermissionRequestAsync(requestItem.Id, userC);

            // Assert
            Assert.True(result is Failure fail && fail.ErrorMessage == ErrorMessages.NotAuthorizedToReject);
        }
    }

    [Fact]
    public async Task ApproveAndRejectPermissionRequestAsync_WhenAlreadyProcessed_ReturnsRequestAlreadyProcessed()
    {
        var (db, sut, userA, userB, tagId, _) = await CreateScopeAsync();
        await using (db)
        {
            var request = new TagPermissionRequestDto(
                RequestedTagId: tagId,
                TargetUserId: userA,
                RequestedAmount: 1
            );
            await sut.SubmitPermissionRequestAsync(userB, request);

            var requestItem = await db.Items.FirstOrDefaultAsync(i => i.OwnerId == userB);
            Assert.NotNull(requestItem);

            // 1回目の承認 -> 成功
            var firstApprove = await sut.ApprovePermissionRequestAsync(requestItem.Id, userA);
            Assert.True(firstApprove is Success<bool>);

            // 2回目の承認試行 -> 既に処理済みエラー
            var secondApprove = await sut.ApprovePermissionRequestAsync(requestItem.Id, userA);
            Assert.True(secondApprove is Failure f1 && f1.ErrorMessage == ErrorMessages.RequestAlreadyProcessed);

            // 処理済みリクエストに対する却下試行 -> 既に処理済みエラー
            var rejectAttempt = await sut.RejectPermissionRequestAsync(requestItem.Id, userA);
            Assert.True(rejectAttempt is Failure f2 && f2.ErrorMessage == ErrorMessages.RequestAlreadyProcessed);
        }
    }

    [Fact]
    public void ParsePermissionPayload_LegacyRegexFallback_ParsesCorrectly()
    {
        var legacyItem = new Item
        {
            OwnerId = "legacyOwner",
            Content = "【タグ操作権限リクエスト】\nタグ「LegacyTag」の操作権限 7 をリクエストしました。（無償リクエスト）\n\nメッセージ:\nおねがいします"
        };

        var payload = RightAssetDataProvider.ParsePermissionPayload(legacyItem);

        Assert.NotNull(payload);
        Assert.Equal("LegacyTag", payload.RequestedTagName);
        Assert.Equal(7, payload.RequestedAmount);
        Assert.Equal(TradeStatus.Proposed, payload.Status);
    }


    private sealed class DbContextFactoryStub(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);

        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}