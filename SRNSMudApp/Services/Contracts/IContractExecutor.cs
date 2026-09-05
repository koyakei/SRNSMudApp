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
    /// <param name="dbContext">実行に使用するデータベースコンテキスト（同一トランザクション境界）。</param>
    /// <param name="contract">承認対象の契約エンティティ。</param>
    /// <param name="currentUserId">承認操作を実行しているユーザーの ID。</param>
    /// <param name="fulfillerAssetId">バウンティ契約などで使用する、実行者が提供する RightAsset の ID。省略可能。</param>
    /// <returns>
    ///     承認・実行に成功した場合は <see cref="Success{T}" />（成功メッセージ）、
    ///     失敗した場合は <see cref="Failure" />（エラーメッセージ）。
    /// </returns>
    Task<Result<string>> ExecuteAsync(ApplicationDbContext dbContext, TaggingRequestEntity contract, string currentUserId, int? fulfillerAssetId = null);
}