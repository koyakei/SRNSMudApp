#region

using System.Security.Cryptography;
using System.Text;

using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Models.Unions;

#endregion

namespace SRNSMudApp.Services;

/// <summary>
///     JPYC による RightAsset 購入・決済処理を提供するインターフェース。
///     ユーザーごとの専用受取ウォレットへの入金・トランザクション完了確認を行う。
/// </summary>
public interface IRightAssetPurchaseService
{
    /// <summary>
    ///     サポートされている JPYC ネットワーク一覧を取得する。
    /// </summary>
    IReadOnlyList<JpycNetworkInfo> GetSupportedNetworks();

    /// <summary>
    ///     ユーザー専用の JPYC 受取ウォレットを取得または新規発行する。
    /// </summary>
    /// <param name="userId">ユーザーID</param>
    /// <param name="networkName">ネットワーク名</param>
    /// <param name="cancellationToken">キャンセラレーショントークン</param>
    Task<UserDepositWalletDto> GetOrCreateUserDepositWalletAsync(
        string userId,
        string networkName = "polygon-amoy",
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     テストネット・開発環境用に、ユーザー専用受取アドレスへの送金をシミュレートする。
    /// </summary>
    /// <param name="userId">ユーザーID</param>
    /// <param name="networkName">ネットワーク名</param>
    /// <param name="amountJpyc">送金 JPYC 金額</param>
    /// <param name="cancellationToken">キャンセラレーショントークン</param>
    Task<string> SimulateDepositAsync(
        string userId,
        string networkName,
        int amountJpyc,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     トランザクション完了をシステムで検証した上で、RightAsset を発行・アカウントへ付与する。
    /// </summary>
    /// <param name="userId">ユーザーID</param>
    /// <param name="request">購入リクエスト情報</param>
    /// <param name="cancellationToken">キャンセラレーショントークン</param>
    Task<Result<RightAsset>> PurchaseRightAssetWithJpycAsync(
        string userId,
        JpycPurchaseRequestDto request,
        CancellationToken cancellationToken = default);
}

/// <summary>
///     JPYC による RightAsset 購入・発行サービス実装。
///     ユーザー専用受取ウォレット管理およびオンチェーントランザクション検証連携を行う。
/// </summary>
public class RightAssetPurchaseService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    IJpycTransactionVerifier transactionVerifier) : IRightAssetPurchaseService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));

    private readonly IJpycTransactionVerifier _transactionVerifier =
        transactionVerifier ?? throw new ArgumentNullException(nameof(transactionVerifier));

    // JPYC 開発者ドキュメント (https://faq.jpyc.co.jp/s/article/developer-documentation) に基づくネットワーク一覧
    private static readonly List<JpycNetworkInfo> SupportedNetworks =
    [
        new(
            Name: "polygon-amoy",
            DisplayName: "Polygon Amoy (テストネット)",
            ChainId: 80002,
            ContractAddress: "0xE7C3D8C9a439feDe00D2600032D5dB0Be71C3c29",
            FaucetUrl: "https://faucet.jpyc.co.jp",
            ExplorerUrl: "https://amoy.polygonscan.com/",
            IsTestnet: true,
            IsRecommended: true),
        new(
            Name: "ethereum-sepolia",
            DisplayName: "Ethereum Sepolia (テストネット)",
            ChainId: 11155111,
            ContractAddress: "0xE7C3D8C9a439feDe00D2600032D5dB0Be71C3c29",
            FaucetUrl: "https://faucet.jpyc.co.jp",
            ExplorerUrl: "https://sepolia.etherscan.io/",
            IsTestnet: true),
        new(
            Name: "avalanche-fuji",
            DisplayName: "Avalanche Fuji (テストネット)",
            ChainId: 43113,
            ContractAddress: "0xE7C3D8C9a439feDe00D2600032D5dB0Be71C3c29",
            FaucetUrl: "https://faucet.jpyc.co.jp",
            ExplorerUrl: "https://testnet.snowtrace.io/",
            IsTestnet: true),
        new(
            Name: "polygon-mainnet",
            DisplayName: "Polygon (メインネット)",
            ChainId: 137,
            ContractAddress: "0x431D5dfF03120AFA4bDf332c61A6e1766eF37BDB",
            FaucetUrl: null,
            ExplorerUrl: "https://polygonscan.com/",
            IsTestnet: false)
    ];

    /// <inheritdoc />
    public IReadOnlyList<JpycNetworkInfo> GetSupportedNetworks() => SupportedNetworks;

    /// <inheritdoc />
    public async Task<UserDepositWalletDto> GetOrCreateUserDepositWalletAsync(
        string userId,
        string networkName = "polygon-amoy",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        await using ApplicationDbContext dbContext = await _dbFactory.CreateDbContextAsync(cancellationToken);

        // 既存の受取ウォレットを検索
        UserDepositWallet? existingWallet = await dbContext.UserDepositWallets
            .FirstOrDefaultAsync(w => w.OwnerId == userId && w.NetworkName == networkName, cancellationToken);

        if (existingWallet is not null)
        {
            return new UserDepositWalletDto(
                UserId: existingWallet.OwnerId,
                NetworkName: existingWallet.NetworkName,
                DepositAddress: existingWallet.DepositAddress,
                CreatedAt: existingWallet.CreatedDate);
        }

        // ユーザー専用の決定論的 EVM アドレスを生成 (0x + 40文字hex)
        var deterministicAddress = GenerateDepositAddressForUser(userId);

        var newWallet = new UserDepositWallet
        {
            OwnerId = userId,
            NetworkName = networkName,
            DepositAddress = deterministicAddress
        };

        dbContext.UserDepositWallets.Add(newWallet);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new UserDepositWalletDto(
            UserId: newWallet.OwnerId,
            NetworkName: newWallet.NetworkName,
            DepositAddress: newWallet.DepositAddress,
            CreatedAt: newWallet.CreatedDate);
    }

    /// <inheritdoc />
    public async Task<string> SimulateDepositAsync(
        string userId,
        string networkName,
        int amountJpyc,
        CancellationToken cancellationToken = default)
    {
        var wallet = await GetOrCreateUserDepositWalletAsync(userId, networkName, cancellationToken);
        return await _transactionVerifier.SimulateDepositTransactionAsync(
            networkName,
            wallet.DepositAddress,
            amountJpyc,
            null,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result<RightAsset>> PurchaseRightAssetWithJpycAsync(
        string userId,
        JpycPurchaseRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(userId))
        {
            return Result.Fail<RightAsset>("ログインユーザーが指定されていません。");
        }

        if (request.Amount <= 0)
        {
            return Result.Fail<RightAsset>("購入数量は1以上を指定してください。");
        }

        if (request.UnitPriceJpyc <= 0)
        {
            return Result.Fail<RightAsset>("1アセットあたりのJPYC単価は1以上を指定してください。");
        }

        if (string.IsNullOrWhiteSpace(request.TransactionHash))
        {
            return Result.Fail<RightAsset>("送金トランザクションハッシュを入力してください。");
        }

        await using ApplicationDbContext dbContext = await _dbFactory.CreateDbContextAsync(cancellationToken);

        Tag? tag = await dbContext.Tags
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == request.RequestedTagId, cancellationToken);

        if (tag is null)
        {
            return Result.Fail<RightAsset>("対象のタグが見つかりません。");
        }

        // ユーザー専用受取ウォレットの取得
        var wallet = await GetOrCreateUserDepositWalletAsync(userId, request.NetworkName, cancellationToken);

        // トランザクションハッシュの二重使用防止チェック
        var normalizedTx = request.TransactionHash.Trim();
        var alreadyUsed = await dbContext.JpycDepositTransactions
            .AnyAsync(t => t.TransactionHash == normalizedTx && t.Status == JpycDepositStatus.Confirmed, cancellationToken);

        if (alreadyUsed)
        {
            return Result.Fail<RightAsset>("このトランザクションハッシュは既にRightAsset発行に使用されています。");
        }

        // システムによるトランザクション完了確認 & 宛先ウォレット・金額の検証
        var verification = await _transactionVerifier.VerifyTransactionAsync(
            request.NetworkName,
            normalizedTx,
            wallet.DepositAddress,
            request.TotalJpycAmount,
            cancellationToken);

        if (!verification.IsSuccess)
        {
            return Result.Fail<RightAsset>(verification.ErrorMessage ?? "トランザクションの完了を確認できませんでした。");
        }

        // 新規 RightAsset の発行
        var newAsset = new RightAsset
        {
            TargetTagId = tag.Id,
            OwnerId = userId,
            Amount = request.Amount,
            IsBurned = false
        };

        dbContext.RightAssets.Add(newAsset);
        await dbContext.SaveChangesAsync(cancellationToken);

        // JpycDepositTransaction レコードの保存
        var depositTx = new JpycDepositTransaction
        {
            OwnerId = userId,
            DepositAddress = wallet.DepositAddress,
            TransactionHash = normalizedTx,
            NetworkName = request.NetworkName,
            AmountJpyc = verification.AmountJpyc > 0 ? verification.AmountJpyc : request.TotalJpycAmount,
            TargetTagId = tag.Id,
            RightAssetAmount = request.Amount,
            RightAssetId = newAsset.Id,
            Status = JpycDepositStatus.Confirmed,
            VerifiedAt = DateTime.UtcNow
        };

        dbContext.JpycDepositTransactions.Add(depositTx);

        // 取引・購入ログの記録用 Item 作成
        var itemContent = $"【JPYC決済によるRightAsset購入（システム確認完了）】\n" +
                          $"タグ「{tag.Name}」の操作権限 {request.Amount} を購入・付与しました。\n" +
                          $"単価: {request.UnitPriceJpyc:N0} JPYC / 合計: {request.TotalJpycAmount:N0} JPYC\n" +
                          $"受取専用アドレス: {wallet.DepositAddress}\n" +
                          $"ネットワーク: {request.NetworkName} / Tx: {normalizedTx}";

        var purchaseItem = new Item
        {
            OwnerId = userId,
            Content = itemContent
        };

        dbContext.Items.Add(purchaseItem);

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Ok(newAsset);
    }

    /// <summary>
    ///     ユーザーIDから決定論的かつユニークな 20 バイト EVM アドレス (0x + 40文字hex) を導出する。
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1308:Normalize strings to uppercase", Justification = "Ethereum/EVM addresses conventionally use lowercase hex strings")]
    private static string GenerateDepositAddressForUser(string userId)
    {
        var rawKey = $"SRNS_JPYC_DEPOSIT_SALT_v1_{userId}";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawKey));
        // 先頭 20 バイト（160ビット）をアドレスとして使用
        var addressHex = Convert.ToHexString(hashBytes, 0, 20).ToLowerInvariant();
        return $"0x{addressHex}";
    }
}