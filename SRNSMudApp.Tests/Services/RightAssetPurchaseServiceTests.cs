#region

using Microsoft.EntityFrameworkCore;

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

    private sealed class DbContextFactoryStub(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);

        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}