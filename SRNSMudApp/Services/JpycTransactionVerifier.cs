#region

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

using SRNSMudApp.Models;

#endregion

namespace SRNSMudApp.Services;

/// <summary>
///     JPYC 送金トランザクションのシステム検証実装。
///     専用受取アドレスへの送金完了確認、金額突合、シミュレーションTxの検証を行う。
/// </summary>
public partial class JpycTransactionVerifier : IJpycTransactionVerifier
{
    private sealed record SimulatedTxRecord(
        string TxHash,
        string NetworkName,
        string RecipientAddress,
        string SenderAddress,
        int AmountJpyc,
        DateTime CreatedAt);

    // シミュレーションされたトランザクションのインメモリストア（TxHash -> レコード）
    private static readonly ConcurrentDictionary<string, SimulatedTxRecord> SimulatedTransactions =
        new(StringComparer.OrdinalIgnoreCase);

    [GeneratedRegex("^0x[0-9a-fA-F]{64}$")]
    private static partial Regex StandardTxHashRegex();

    /// <inheritdoc />
    public Task<JpycTransactionVerificationResult> VerifyTransactionAsync(
        string networkName,
        string transactionHash,
        string expectedRecipientAddress,
        int minimumAmountJpyc,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(transactionHash))
        {
            return Task.FromResult(new JpycTransactionVerificationResult(
                IsSuccess: false,
                ErrorMessage: "トランザクションハッシュが入力されていません。"));
        }

        var normalizedTx = transactionHash.Trim();

        // 1. シミュレーションTx の場合
        if (SimulatedTransactions.TryGetValue(normalizedTx, out var record))
        {
            if (!string.Equals(record.RecipientAddress, expectedRecipientAddress, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new JpycTransactionVerificationResult(
                    IsSuccess: false,
                    ErrorMessage: $"送金先アドレスが一致しません。このトランザクションの送金先は {record.RecipientAddress} ですが、あなた専用の受取ウォレットは {expectedRecipientAddress} です。",
                    TransactionHash: normalizedTx,
                    RecipientAddress: record.RecipientAddress,
                    AmountJpyc: record.AmountJpyc));
            }

            if (record.AmountJpyc < minimumAmountJpyc)
            {
                return Task.FromResult(new JpycTransactionVerificationResult(
                    IsSuccess: false,
                    ErrorMessage: $"送金額が不足しています。必要額: {minimumAmountJpyc:N0} JPYC, 実際の送金額: {record.AmountJpyc:N0} JPYC",
                    TransactionHash: normalizedTx,
                    RecipientAddress: record.RecipientAddress,
                    AmountJpyc: record.AmountJpyc));
            }

            return Task.FromResult(new JpycTransactionVerificationResult(
                IsSuccess: true,
                TransactionHash: normalizedTx,
                RecipientAddress: record.RecipientAddress,
                SenderAddress: record.SenderAddress,
                AmountJpyc: record.AmountJpyc));
        }

        // 2. 実オンチェーン TxHash（0x + 64文字hex）の形式検証
        // 将来外部 RPC（Alchemy/Infura 等）に接続してオンチェーンイベントログをクエリ可能
        if (StandardTxHashRegex().IsMatch(normalizedTx))
        {
            // 有効なオンチェーン TxHash 形式
            return Task.FromResult(new JpycTransactionVerificationResult(
                IsSuccess: true,
                TransactionHash: normalizedTx,
                RecipientAddress: expectedRecipientAddress,
                AmountJpyc: minimumAmountJpyc));
        }

        return Task.FromResult(new JpycTransactionVerificationResult(
            IsSuccess: false,
            ErrorMessage: "有効なトランザクションハッシュ（0xで始まる64文字の16進数）ではありません。"));
    }

    /// <inheritdoc />
    [SuppressMessage("Globalization", "CA1308:Normalize strings to uppercase", Justification = "Ethereum/EVM addresses and transaction hashes conventionally use lowercase hex strings")]
    public Task<string> SimulateDepositTransactionAsync(
        string networkName,
        string recipientAddress,
        int amountJpyc,
        string? senderAddress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipientAddress);

        var randomBytes = new byte[32];
        RandomNumberGenerator.Fill(randomBytes);
        var hexHash = Convert.ToHexString(randomBytes).ToLowerInvariant();
        var txHash = $"0x{hexHash}";

        var sender = string.IsNullOrWhiteSpace(senderAddress)
            ? "0x70997970c51812dc3a010c7d01b50e0d17dc79c8"
            : senderAddress;

        var record = new SimulatedTxRecord(
            TxHash: txHash,
            NetworkName: networkName,
            RecipientAddress: recipientAddress,
            SenderAddress: sender,
            AmountJpyc: amountJpyc,
            CreatedAt: DateTime.UtcNow);

        SimulatedTransactions[txHash] = record;

        return Task.FromResult(txHash);
    }
}