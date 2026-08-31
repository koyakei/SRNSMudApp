using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;

namespace SRNSMudApp.Services.Contracts;

/// <summary>
///     特定の契約種別に対する承認・実行処理を担当する Strategy インターフェース。
/// </summary>
public interface IContractExecutor
{
    /// <summary>
    ///     対応する契約種別（ContractTypes に準拠）。
    /// </summary>
    string ContractType { get; }

    /// <summary>
    ///     契約をアトミックに承認・実行する。
    /// </summary>
    Task<Result<string>> ExecuteAsync(TaggingRequestEntity contract, string currentUserId, int? fulfillerAssetId = null);
}