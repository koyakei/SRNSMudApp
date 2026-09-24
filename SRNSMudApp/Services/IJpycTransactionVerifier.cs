#region

using SRNSMudApp.Models;

#endregion

namespace SRNSMudApp.Services;

/// <summary>
///     JPYC 送金トランザクションのシステム検証およびシミュレーションを提供するインターフェース。
/// </summary>
public interface IJpycTransactionVerifier
{
    /// <summary>
    ///     オンチェーントランザクションが完了したことを検証する。
    ///     送金先がユーザー専用受取アドレスであること、金額が満たされていることを確認する。
    /// </summary>
    /// <param name="networkName">ブロックチェーンネットワーク名</param>
    /// <param name="transactionHash">トランザクションハッシュ</param>
    /// <param name="expectedRecipientAddress">期待されるユーザー専用受取アドレス</param>
    /// <param name="minimumAmountJpyc">最低限必要な JPYC 送金額</param>
    /// <param name="cancellationToken">キャンセラレーショントークン</param>
    Task<JpycTransactionVerificationResult> VerifyTransactionAsync(
        string networkName,
        string transactionHash,
        string expectedRecipientAddress,
        int minimumAmountJpyc,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     テストネット・開発環境用に、ユーザー専用受取アドレス宛ての JPYC 送金トランザクションをシミュレーション登録する。
    /// </summary>
    /// <param name="networkName">ネットワーク名</param>
    /// <param name="recipientAddress">ユーザー専用受取アドレス</param>
    /// <param name="amountJpyc">送金 JPYC 金額</param>
    /// <param name="senderAddress">送信元アドレス（任意）</param>
    /// <param name="cancellationToken">キャンセラレーショントークン</param>
    Task<string> SimulateDepositTransactionAsync(
        string networkName,
        string recipientAddress,
        int amountJpyc,
        string? senderAddress = null,
        CancellationToken cancellationToken = default);
}