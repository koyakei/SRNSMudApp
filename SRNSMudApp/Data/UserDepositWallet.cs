namespace SRNSMudApp.Data;

/// <summary>
///     ユーザーごとの JPYC 受取専用ウォレット（Deposit Wallet）。
///     ユーザーに個別の受取アドレスを割り当て、トランザクションの確実な追跡・照合を行う。
/// </summary>
public class UserDepositWallet : BaseEntity
{
    /// <summary>
    ///     対応するブロックチェーンネットワーク識別子（例: polygon-amoy, ethereum-sepolia, または evm-common）。
    /// </summary>
    public string NetworkName { get; set; } = string.Empty;

    /// <summary>
    ///     ユーザー専用の受取ウォレットアドレス（0x から始まる 42 文字の EVM アドレス）。
    /// </summary>
    public string DepositAddress { get; set; } = string.Empty;
}