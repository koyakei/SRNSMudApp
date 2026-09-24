#region

using System.Diagnostics.CodeAnalysis;

using SRNSMudApp.Data;

#endregion

namespace SRNSMudApp.Models;

/// <summary>
///     JPYC がサポートするブロックチェーンネットワーク情報。
///     開発者ドキュメント (https://faq.jpyc.co.jp/s/article/developer-documentation) に準拠。
/// </summary>
[SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "URL strings are used directly for UI binding and links")]
[SuppressMessage("Design", "CA1054:URI-like parameters should not be strings", Justification = "URL strings are used directly for UI binding and links")]
public sealed record JpycNetworkInfo(
    string Name,
    string DisplayName,
    long ChainId,
    string ContractAddress,
    string? FaucetUrl,
    string ExplorerUrl,
    bool IsTestnet,
    bool IsRecommended = false);

/// <summary>
///     JPYC を利用した RightAsset 購入リクエストDTO。
/// </summary>
public sealed record JpycPurchaseRequestDto(
    int RequestedTagId,
    int Amount,
    int UnitPriceJpyc,
    string NetworkName,
    string? TransactionHash = null,
    string? SenderWalletAddress = null)
{
    /// <summary>
    ///     支払うべき合計 JPYC 金額。
    /// </summary>
    public int TotalJpycAmount => Amount * UnitPriceJpyc;
}

/// <summary>
///     ユーザーごとの専用 JPYC 受取ウォレットDTO。
/// </summary>
public sealed record UserDepositWalletDto(
    string UserId,
    string NetworkName,
    string DepositAddress,
    DateTime CreatedAt);

/// <summary>
///     JPYC 送金トランザクションのシステム検証結果。
/// </summary>
public sealed record JpycTransactionVerificationResult(
    bool IsSuccess,
    string? ErrorMessage = null,
    string? TransactionHash = null,
    string? RecipientAddress = null,
    string? SenderAddress = null,
    int AmountJpyc = 0);

/// <summary>
///     JPYC 購入処理の完了結果DTO。
/// </summary>
public sealed record JpycPurchaseResultDto(
    int RightAssetId,
    int TagId,
    string TagName,
    int Amount,
    int TotalJpycAmount,
    string NetworkName,
    string TransactionHash,
    string DepositAddress,
    DateTime PurchasedAt);