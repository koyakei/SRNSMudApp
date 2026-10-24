#region

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;

#endregion

namespace SRNSMudApp.Tests.Services;

public class RightAssetPurchaseServiceTests : IAsyncLifetime
{
    private MsSqlTestDatabase _sharedDb = null!;
    private JpycTransactionVerifier _verifier = null!;

    public async Task InitializeAsync()
    {
        _sharedDb = await SharedMsSqlTestDatabase.GetInstanceAsync();
        _verifier = new JpycTransactionVerifier();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(ApplicationDbContext db, RightAssetPurchaseService sut, string userId, int tagId, string tid)> CreateScopeAsync()
    {
        var tid = Guid.NewGuid().ToString("N")[..8];
        var db = new ApplicationDbContext(_sharedDb.Options);
        var stubFactory = new DbContextFactoryStub(_sharedDb.Options);
        var sut = new RightAssetPurchaseService(stubFactory, _verifier);

        var userId = $"purchase_user_{tid}";
        await db.SeedUsersAsync(userId);

        var tag = new Tag
        {
            Name = $"PurchaseTag_{tid}",
            Content = "Test Purchase Tag",
            OwnerId = userId
        };

        db.Tags.Add(tag);
        await db.SaveChangesAsync();

        return (db, sut, userId, tag.Id, tid);
    }

    [Fact]
    public void GetSupportedNetworks_ReturnsExpectedJpycNetworks()
    {
        var stubFactory = new DbContextFactoryStub(_sharedDb.Options);
        var sut = new RightAssetPurchaseService(stubFactory, _verifier);

        IReadOnlyList<JpycNetworkInfo> networks = sut.GetSupportedNetworks();

        Assert.NotEmpty(networks);
        Assert.Contains(networks, n => n.Name == "polygon-amoy" && n.IsRecommended && n.ContractAddress == "0xE7C3D8C9a439feDe00D2600032D5dB0Be71C3c29");
        Assert.Contains(networks, n => n.Name == "ethereum-sepolia" && n.ContractAddress == "0xE7C3D8C9a439feDe00D2600032D5dB0Be71C3c29");
        Assert.Contains(networks, n => n.Name == "polygon-mainnet" && !n.IsTestnet);
    }

    [Fact]
    public async Task GetOrCreateUserDepositWalletAsync_CreatesAndReturnsUniqueAddressPerUser()
    {
        var (db, sut, userId, _, _) = await CreateScopeAsync();
        await using (db)
        {
            // 初回生成
            var wallet1 = await sut.GetOrCreateUserDepositWalletAsync(userId, "polygon-amoy");
            Assert.NotNull(wallet1);
            Assert.StartsWith("0x", wallet1.DepositAddress);
            Assert.Equal(42, wallet1.DepositAddress.Length);
            Assert.Equal(userId, wallet1.UserId);

            // 2回目取得（同一アドレスが返る）
            var wallet2 = await sut.GetOrCreateUserDepositWalletAsync(userId, "polygon-amoy");
            Assert.Equal(wallet1.DepositAddress, wallet2.DepositAddress);

            // DB に保存されているか確認
            var savedWallet = await db.UserDepositWallets.FirstOrDefaultAsync(w => w.OwnerId == userId);
            Assert.NotNull(savedWallet);
            Assert.Equal(wallet1.DepositAddress, savedWallet.DepositAddress);
        }
    }

    [Fact]
    public async Task PurchaseRightAssetWithJpycAsync_WhenValidTx_VerifiesAndGrantsRightAsset()
    {
        var (db, sut, userId, tagId, _) = await CreateScopeAsync();
        await using (db)
        {
            var amount = 5;
            var unitPrice = 150;
            var totalJpyc = amount * unitPrice; // 750 JPYC

            // ユーザー専用アドレス宛ての送金をシミュレーション
            var txHash = await sut.SimulateDepositAsync(userId, "polygon-amoy", totalJpyc);

            var request = new JpycPurchaseRequestDto(
                RequestedTagId: tagId,
                Amount: amount,
                UnitPriceJpyc: unitPrice,
                NetworkName: "polygon-amoy",
                TransactionHash: txHash
            );

            // Act: システムによるトランザクション確認 & RightAsset付与
            var result = await sut.PurchaseRightAssetWithJpycAsync(userId, request);

            // Assert
            var success = result switch
            {
                Success<RightAsset> s => s,
                _ => throw new InvalidOperationException($"Expected Success but got {result}")
            };
            Assert.Equal(amount, success.Value.Amount);
            Assert.Equal(tagId, success.Value.TargetTagId);
            Assert.Equal(userId, success.Value.OwnerId);

            // DB に RightAsset が保存されたか
            var savedAsset = await db.RightAssets.FirstOrDefaultAsync(a => a.Id == success.Value.Id);
            Assert.NotNull(savedAsset);
            Assert.Equal(amount, savedAsset.Amount);

            // JpycDepositTransaction が記録されたか
            var depositTx = await db.JpycDepositTransactions.FirstOrDefaultAsync(t => t.TransactionHash == txHash);
            Assert.NotNull(depositTx);
            Assert.Equal(JpycDepositStatus.Confirmed, depositTx.Status);
            Assert.Equal(totalJpyc, depositTx.AmountJpyc);
            Assert.Equal(savedAsset.Id, depositTx.RightAssetId);

            // 購入ログ Item が作成されたか
            var logItem = await db.Items.FirstOrDefaultAsync(i => i.OwnerId == userId && i.Content.Contains("システム確認完了"));
            Assert.NotNull(logItem);
            Assert.Contains("単価: 150 JPYC", logItem.Content);
            Assert.Contains("合計: 750 JPYC", logItem.Content);
            Assert.Contains(txHash, logItem.Content);
        }
    }

    [Fact]
    public async Task PurchaseRightAssetWithJpycAsync_WhenTxSentToAnotherUserDepositAddress_ReturnsFailure()
    {
        var (db, sut, userId, tagId, tid) = await CreateScopeAsync();
        await using (db)
        {
            var anotherUserId = $"other_user_{tid}";
            await db.SeedUsersAsync(anotherUserId);

            // 別のユーザーの専用アドレス宛てに送金シミュレーション
            var anotherTxHash = await sut.SimulateDepositAsync(anotherUserId, "polygon-amoy", 500);

            var request = new JpycPurchaseRequestDto(
                RequestedTagId: tagId,
                Amount: 5,
                UnitPriceJpyc: 100,
                NetworkName: "polygon-amoy",
                TransactionHash: anotherTxHash
            );

            // Act: ユーザー userId が、別ユーザー宛ての TxHash で申請
            var result = await sut.PurchaseRightAssetWithJpycAsync(userId, request);

            // Assert: 送金先アドレス不一致で失敗すること
            var fail = result switch
            {
                Failure f => f,
                _ => throw new InvalidOperationException($"Expected Failure but got {result}")
            };
            Assert.Contains("送金先アドレスが一致しません", fail.ErrorMessage);
        }
    }

    [Fact]
    public async Task PurchaseRightAssetWithJpycAsync_WhenAmountIsInsufficient_ReturnsFailure()
    {
        var (db, sut, userId, tagId, _) = await CreateScopeAsync();
        await using (db)
        {
            // 300 JPYC しか送金していないシミュレーション
            var txHash = await sut.SimulateDepositAsync(userId, "polygon-amoy", 300);

            var request = new JpycPurchaseRequestDto(
                RequestedTagId: tagId,
                Amount: 5,
                UnitPriceJpyc: 100, // 合計 500 JPYC 必要
                NetworkName: "polygon-amoy",
                TransactionHash: txHash
            );

            // Act
            var result = await sut.PurchaseRightAssetWithJpycAsync(userId, request);

            // Assert: 金額不足で失敗すること
            var fail = result switch
            {
                Failure f => f,
                _ => throw new InvalidOperationException($"Expected Failure but got {result}")
            };
            Assert.Contains("送金額が不足しています", fail.ErrorMessage);
        }
    }

    [Fact]
    public async Task PurchaseRightAssetWithJpycAsync_WhenTxHashAlreadyUsed_ReturnsFailure()
    {
        var (db, sut, userId, tagId, _) = await CreateScopeAsync();
        await using (db)
        {
            var txHash = await sut.SimulateDepositAsync(userId, "polygon-amoy", 500);

            var request = new JpycPurchaseRequestDto(
                RequestedTagId: tagId,
                Amount: 5,
                UnitPriceJpyc: 100,
                NetworkName: "polygon-amoy",
                TransactionHash: txHash
            );

            // 1回目の購入: 成功
            var result1 = await sut.PurchaseRightAssetWithJpycAsync(userId, request);
            Assert.True(result1 is Success<RightAsset>);

            // 2回目の同一TxHash購入: 失敗すること
            var result2 = await sut.PurchaseRightAssetWithJpycAsync(userId, request);
            var fail = result2 switch
            {
                Failure f => f,
                _ => throw new InvalidOperationException($"Expected Failure but got {result2}")
            };
            Assert.Contains("使用されています", fail.ErrorMessage);
        }
    }

    [Fact]
    public async Task PurchaseRightAssetWithJpycAsync_WhenValidationFails_ReturnsFailure()
    {
        var (_, sut, userId, tagId, _) = await CreateScopeAsync();

        // 数量0
        var invalidAmountReq = new JpycPurchaseRequestDto(tagId, 0, 100, "polygon-amoy", "0x123");
        var result1 = await sut.PurchaseRightAssetWithJpycAsync(userId, invalidAmountReq);
        Assert.True(result1 is Failure);

        // 単価0
        var invalidPriceReq = new JpycPurchaseRequestDto(tagId, 5, 0, "polygon-amoy", "0x123");
        var result2 = await sut.PurchaseRightAssetWithJpycAsync(userId, invalidPriceReq);
        Assert.True(result2 is Failure);

        // TxHash が null / 空
        var missingTxReq = new JpycPurchaseRequestDto(tagId, 5, 100, "polygon-amoy", null);
        var result3 = await sut.PurchaseRightAssetWithJpycAsync(userId, missingTxReq);
        Assert.True(result3 is Failure);
    }

    [Fact]
    public async Task PurchaseRightAssetWithJpycAsync_WhenTagNotFound_ReturnsFailure()
    {
        var (_, sut, userId, _, _) = await CreateScopeAsync();
        var nonExistentTagId = 999999;
        var validTx = "0x" + new string('a', 64);

        var request = new JpycPurchaseRequestDto(
            RequestedTagId: nonExistentTagId,
            Amount: 1,
            UnitPriceJpyc: 100,
            NetworkName: "polygon-amoy",
            TransactionHash: validTx);

        var result = await sut.PurchaseRightAssetWithJpycAsync(userId, request);

        var fail = result switch
        {
            Failure f => f,
            _ => throw new InvalidOperationException($"Expected Failure but got {result}")
        };
        Assert.Contains("対象のタグが見つかりません", fail.ErrorMessage);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task PurchaseRightAssetWithJpycAsync_WhenUserIdIsNullOrWhitespace_ReturnsFailure(string emptyUserId)
    {
        var (_, sut, _, tagId, _) = await CreateScopeAsync();
        var request = new JpycPurchaseRequestDto(tagId, 1, 100, "polygon-amoy", "0x123");

        var result = await sut.PurchaseRightAssetWithJpycAsync(emptyUserId, request);

        var fail = result switch
        {
            Failure f => f,
            _ => throw new InvalidOperationException($"Expected Failure but got {result}")
        };
        Assert.Contains("ログインユーザーが指定されていません", fail.ErrorMessage);
    }

    [Fact]
    public async Task PurchaseRightAssetWithJpycAsync_WhenRequestIsNull_ThrowsArgumentNullException()
    {
        var (_, sut, userId, _, _) = await CreateScopeAsync();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            sut.PurchaseRightAssetWithJpycAsync(userId, null!));
    }

    [Theory]
    [InlineData(-1, 100, "購入数量は1以上")]
    [InlineData(1, -100, "1アセットあたりのJPYC単価は1以上")]
    public async Task PurchaseRightAssetWithJpycAsync_WhenNegativeValues_ReturnsFailure(int amount, int unitPrice, string expectedError)
    {
        var (_, sut, userId, tagId, _) = await CreateScopeAsync();
        var request = new JpycPurchaseRequestDto(tagId, amount, unitPrice, "polygon-amoy", "0x" + new string('a', 64));

        var result = await sut.PurchaseRightAssetWithJpycAsync(userId, request);

        var fail = result switch
        {
            Failure f => f,
            _ => throw new InvalidOperationException($"Expected Failure but got {result}")
        };
        Assert.Contains(expectedError, fail.ErrorMessage);
    }

    [Fact]
    public async Task PurchaseRightAssetWithJpycAsync_WhenInvalidTxHashFormat_ReturnsFailure()
    {
        var (_, sut, userId, tagId, _) = await CreateScopeAsync();
        var request = new JpycPurchaseRequestDto(
            RequestedTagId: tagId,
            Amount: 1,
            UnitPriceJpyc: 100,
            NetworkName: "polygon-amoy",
            TransactionHash: "invalid-not-hex-format");

        var result = await sut.PurchaseRightAssetWithJpycAsync(userId, request);

        var fail = result switch
        {
            Failure f => f,
            _ => throw new InvalidOperationException($"Expected Failure but got {result}")
        };
        Assert.Contains("有効なトランザクションハッシュ", fail.ErrorMessage);
    }

    [Fact]
    public async Task GetOrCreateUserDepositWalletAsync_DifferentNetworks_CreatesSeparateWallets()
    {
        var (db, sut, userId, _, _) = await CreateScopeAsync();
        await using (db)
        {
            var walletAmoy = await sut.GetOrCreateUserDepositWalletAsync(userId, "polygon-amoy");
            var walletSepolia = await sut.GetOrCreateUserDepositWalletAsync(userId, "ethereum-sepolia");

            Assert.Equal("polygon-amoy", walletAmoy.NetworkName);
            Assert.Equal("ethereum-sepolia", walletSepolia.NetworkName);

            // 決定論的アドレス生成ロジックによりアドレス自体は同一導出だが、ネットワークごとに個別レコードが保持される
            var dbWallets = await db.UserDepositWallets.Where(w => w.OwnerId == userId).ToListAsync();
            Assert.Equal(2, dbWallets.Count);
            Assert.Contains(dbWallets, w => w.NetworkName == "polygon-amoy");
            Assert.Contains(dbWallets, w => w.NetworkName == "ethereum-sepolia");
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task GetOrCreateUserDepositWalletAsync_WhenUserIdIsNullOrWhitespace_ThrowsArgumentException(string? invalidUserId)
    {
        var (_, sut, _, _, _) = await CreateScopeAsync();

        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            sut.GetOrCreateUserDepositWalletAsync(invalidUserId!, "polygon-amoy"));
    }

    [Fact]
    public async Task SimulateDepositAsync_CreatesWalletAndReturnsValidTxHash()
    {
        var (db, sut, userId, _, _) = await CreateScopeAsync();
        await using (db)
        {
            var txHash = await sut.SimulateDepositAsync(userId, "polygon-amoy", 500);

            Assert.StartsWith("0x", txHash);
            Assert.Equal(66, txHash.Length);

            // ウォレットがDBに保存されていること
            var wallet = await db.UserDepositWallets.FirstOrDefaultAsync(w => w.OwnerId == userId && w.NetworkName == "polygon-amoy");
            Assert.NotNull(wallet);
        }
    }

    [Fact]
    public async Task PurchaseRightAssetWithJpycAsync_WhenDepositTransactionSaveFails_RollsBackRightAssetAtomically()
    {
        // Arrange: DATA-01 / DATA-02 のアトミック性検証
        // 2回目の SaveChanges（JpycDepositTransaction 保存時）に例外が発生した場合、
        // 最初の SaveChanges で登録された RightAsset もトランザクションロールバックにより保存されないことを確認
        var tid = Guid.NewGuid().ToString("N")[..8];
        var userId = $"atomic_rollback_user_{tid}";

        await using (var seedDb = new ApplicationDbContext(_sharedDb.Options))
        {
            await seedDb.SeedUsersAsync(userId);
            var tag = new Tag
            {
                Name = $"AtomicTag_{tid}",
                Content = "Atomic Tag Content",
                OwnerId = userId
            };
            seedDb.Tags.Add(tag);
            await seedDb.SaveChangesAsync();
        }

        int tagId;
        await using (var queryDb = new ApplicationDbContext(_sharedDb.Options))
        {
            var tag = await queryDb.Tags.FirstAsync(t => t.Name == $"AtomicTag_{tid}");
            tagId = tag.Id;
        }

        // JpycDepositTransaction 保存時に意図的に例外を投げるインターセプターを設定
        var faultyOptions = new DbContextOptionsBuilder<ApplicationDbContext>(_sharedDb.Options)
            .AddInterceptors(new FaultyDepositTransactionInterceptor())
            .Options;
        var faultyFactory = new DbContextFactoryStub(faultyOptions);
        var sut = new RightAssetPurchaseService(faultyFactory, _verifier);

        var amount = 3;
        var unitPrice = 100;
        var totalJpyc = amount * unitPrice;
        var txHash = await sut.SimulateDepositAsync(userId, "polygon-amoy", totalJpyc);

        var request = new JpycPurchaseRequestDto(
            RequestedTagId: tagId,
            Amount: amount,
            UnitPriceJpyc: unitPrice,
            NetworkName: "polygon-amoy",
            TransactionHash: txHash
        );

        // Act: 実行すると JpycDepositTransaction 保存時の例外がスローされる
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.PurchaseRightAssetWithJpycAsync(userId, request));

        // Assert: ロールバックされたため、RightAsset も JpycDepositTransaction も一切保存されていないこと
        await using (var verifyDb = new ApplicationDbContext(_sharedDb.Options))
        {
            var assets = await verifyDb.RightAssets.Where(a => a.OwnerId == userId).ToListAsync();
            Assert.Empty(assets);

            var transactions = await verifyDb.JpycDepositTransactions.Where(t => t.TransactionHash == txHash).ToListAsync();
            Assert.Empty(transactions);
        }
    }

    private sealed class FaultyDepositTransactionInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context is not null)
            {
                var hasDepositTx = eventData.Context.ChangeTracker.Entries<JpycDepositTransaction>().Any();
                if (hasDepositTx)
                {
                    throw new InvalidOperationException("Simulated failure when persisting JpycDepositTransaction");
                }
            }
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    [Fact]
    public async Task PurchaseRightAssetWithJpycAsync_WhenTransientFailureOnFirstSave_RetriesSafelyWithoutDuplicatingAssets()
    {
        var tid = Guid.NewGuid().ToString("N")[..8];
        var userId = $"retry_first_save_{tid}";

        await using (var seedDb = new ApplicationDbContext(_sharedDb.Options))
        {
            await seedDb.SeedUsersAsync(userId);
            var tag = new Tag
            {
                Name = $"RetryTag1_{tid}",
                Content = "Tag for retry test",
                OwnerId = userId
            };
            seedDb.Tags.Add(tag);
            await seedDb.SaveChangesAsync();
        }

        int tagId;
        await using (var queryDb = new ApplicationDbContext(_sharedDb.Options))
        {
            var tag = await queryDb.Tags.FirstAsync(t => t.Name == $"RetryTag1_{tid}");
            tagId = tag.Id;
        }

        var interceptor = new TransientTimeoutOnFirstSaveInterceptor();
        var retryOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(_sharedDb.ConnectionString, sqlOptions =>
            {
                sqlOptions.UseHierarchyId();
                sqlOptions.CommandTimeout(300);
                sqlOptions.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromMilliseconds(50), errorNumbersToAdd: null);
            })
            .AddInterceptors(interceptor)
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;

        var factory = new DbContextFactoryStub(retryOptions);
        var sut = new RightAssetPurchaseService(factory, _verifier);

        var amount = 2;
        var unitPrice = 100;
        var totalJpyc = amount * unitPrice;
        var txHash = await sut.SimulateDepositAsync(userId, "polygon-amoy", totalJpyc);

        var request = new JpycPurchaseRequestDto(
            RequestedTagId: tagId,
            Amount: amount,
            UnitPriceJpyc: unitPrice,
            NetworkName: "polygon-amoy",
            TransactionHash: txHash
        );

        // Act
        var result = await sut.PurchaseRightAssetWithJpycAsync(userId, request);

        // Assert
        Assert.True(result is Success<RightAsset>, $"Expected success on retry but got: {result}");

        await using (var verifyDb = new ApplicationDbContext(_sharedDb.Options))
        {
            var assets = await verifyDb.RightAssets.Where(a => a.OwnerId == userId).ToListAsync();
            // 重要な検証: リトライによりアセットが二重発行されていないこと（1レコードのみ存在すること）
            Assert.Single(assets);
            Assert.Equal(amount, assets[0].Amount);

            var transactions = await verifyDb.JpycDepositTransactions.Where(t => t.TransactionHash == txHash).ToListAsync();
            Assert.Single(transactions);
        }
    }

    [Fact]
    public async Task PurchaseRightAssetWithJpycAsync_WhenTransientFailureOnSecondSave_RetriesSafelyAndAtomically()
    {
        var tid = Guid.NewGuid().ToString("N")[..8];
        var userId = $"retry_second_save_{tid}";

        await using (var seedDb = new ApplicationDbContext(_sharedDb.Options))
        {
            await seedDb.SeedUsersAsync(userId);
            var tag = new Tag
            {
                Name = $"RetryTag2_{tid}",
                Content = "Tag for retry test",
                OwnerId = userId
            };
            seedDb.Tags.Add(tag);
            await seedDb.SaveChangesAsync();
        }

        int tagId;
        await using (var queryDb = new ApplicationDbContext(_sharedDb.Options))
        {
            var tag = await queryDb.Tags.FirstAsync(t => t.Name == $"RetryTag2_{tid}");
            tagId = tag.Id;
        }

        var interceptor = new TransientTimeoutOnSecondSaveInterceptor();
        var retryOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(_sharedDb.ConnectionString, sqlOptions =>
            {
                sqlOptions.UseHierarchyId();
                sqlOptions.CommandTimeout(300);
                sqlOptions.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromMilliseconds(50), errorNumbersToAdd: null);
            })
            .AddInterceptors(interceptor)
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;

        var factory = new DbContextFactoryStub(retryOptions);
        var sut = new RightAssetPurchaseService(factory, _verifier);

        var amount = 2;
        var unitPrice = 100;
        var totalJpyc = amount * unitPrice;
        var txHash = await sut.SimulateDepositAsync(userId, "polygon-amoy", totalJpyc);

        var request = new JpycPurchaseRequestDto(
            RequestedTagId: tagId,
            Amount: amount,
            UnitPriceJpyc: unitPrice,
            NetworkName: "polygon-amoy",
            TransactionHash: txHash
        );

        // Act
        var result = await sut.PurchaseRightAssetWithJpycAsync(userId, request);

        // Assert
        Assert.True(result is Success<RightAsset>, $"Expected success on retry but got: {result}");

        await using (var verifyDb = new ApplicationDbContext(_sharedDb.Options))
        {
            var assets = await verifyDb.RightAssets.Where(a => a.OwnerId == userId).ToListAsync();
            // 重要な検証: ロールバックとリトライにより二重発行や孤立レコードが生じないこと
            Assert.Single(assets);
            Assert.Equal(amount, assets[0].Amount);

            var transactions = await verifyDb.JpycDepositTransactions.Where(t => t.TransactionHash == txHash).ToListAsync();
            Assert.Single(transactions);
        }
    }

    [Fact]
    public async Task PurchaseRightAssetWithJpycAsync_ConcurrentSameTxHash_OnlyOneSucceeds()
    {
        var (db, sut, userId, tagId, _) = await CreateScopeAsync();
        await using (db)
        {
            var amount = 5;
            var unitPrice = 100;
            var totalJpyc = amount * unitPrice;
            var txHash = await sut.SimulateDepositAsync(userId, "polygon-amoy", totalJpyc);

            var request = new JpycPurchaseRequestDto(
                RequestedTagId: tagId,
                Amount: amount,
                UnitPriceJpyc: unitPrice,
                NetworkName: "polygon-amoy",
                TransactionHash: txHash
            );

            // Act: 同一 TxHash で2つの購入リクエストを並行実行
            var task1 = sut.PurchaseRightAssetWithJpycAsync(userId, request);
            var task2 = sut.PurchaseRightAssetWithJpycAsync(userId, request);
            var results = await Task.WhenAll(task1, task2);

            // Assert: 1つは成功、もう1つは重複検出で失敗すること
            var successCount = results.Count(r => r is Success<RightAsset>);
            var failureCount = results.Count(r => r is Failure);

            Assert.Equal(1, successCount);
            Assert.Equal(1, failureCount);

            var failedResult = results.First(r => r is Failure) switch
            {
                Failure f => f,
                _ => throw new InvalidOperationException("Expected Failure")
            };
            Assert.Contains("使用されています", failedResult.ErrorMessage);

            // DB にアセットとトランザクションが重複登録されていないこと
            var assets = await db.RightAssets.Where(a => a.OwnerId == userId).ToListAsync();
            Assert.Single(assets);
            Assert.Equal(amount, assets[0].Amount);

            var transactions = await db.JpycDepositTransactions.Where(t => t.TransactionHash == txHash).ToListAsync();
            Assert.Single(transactions);
        }
    }

    private sealed class TransientTimeoutOnFirstSaveInterceptor : SaveChangesInterceptor
    {
        private int _attempt;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context is not null)
            {
                var hasRightAsset = eventData.Context.ChangeTracker.Entries<RightAsset>().Any();
                var hasDepositTx = eventData.Context.ChangeTracker.Entries<JpycDepositTransaction>().Any();
                if (hasRightAsset && !hasDepositTx)
                {
                    if (Interlocked.Increment(ref _attempt) == 1)
                    {
                        throw new TimeoutException("Simulated transient timeout on first SaveChanges");
                    }
                }
            }
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class TransientTimeoutOnSecondSaveInterceptor : SaveChangesInterceptor
    {
        private int _attempt;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context is not null)
            {
                var hasDepositTx = eventData.Context.ChangeTracker.Entries<JpycDepositTransaction>().Any();
                if (hasDepositTx)
                {
                    if (Interlocked.Increment(ref _attempt) == 1)
                    {
                        throw new TimeoutException("Simulated transient timeout on second SaveChanges");
                    }
                }
            }
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class DbContextFactoryStub(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);

        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}