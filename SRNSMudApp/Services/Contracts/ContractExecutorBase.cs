using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;

namespace SRNSMudApp.Services.Contracts;

/// <summary>
///     契約執行 Strategy（<see cref="IContractExecutor"/>）共通の前提条件検証および
///     日時プロバイダー連携を提供する Template Method 基底クラス。
/// </summary>
/// <param name="timeProvider">日時プロバイダー（省略時はシステム日時）。</param>
public abstract class ContractExecutorBase(TimeProvider? timeProvider = null) : IContractExecutor
{
    /// <summary>
    ///     契約処理で使用する日時プロバイダー。
    /// </summary>
    protected TimeProvider TimeProvider { get; } = timeProvider ?? TimeProvider.System;

    /// <inheritdoc />
    public abstract string ContractType { get; }

    /// <inheritdoc />
    public async Task<Result<string>> ExecuteAsync(
        ApplicationDbContext dbContext,
        TaggingRequestEntity contract,
        string currentUserId,
        int? fulfillerAssetId = null)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(contract);

        return await ExecuteCoreAsync(dbContext, contract, currentUserId, fulfillerAssetId);
    }

    /// <summary>
    ///     検証済みのコンテキストおよび契約リクエストに対する契約固有の執行ロジックを実行する。
    /// </summary>
    /// <param name="dbContext">データベースコンテキスト。</param>
    /// <param name="contract">対象のタグ付けリクエスト契約エンティティ。</param>
    /// <param name="currentUserId">実行操作を行うユーザー ID。</param>
    /// <param name="fulfillerAssetId">提供アセット ID（任意）。</param>
    /// <returns>執行結果。</returns>
    protected abstract Task<Result<string>> ExecuteCoreAsync(
        ApplicationDbContext dbContext,
        TaggingRequestEntity contract,
        string currentUserId,
        int? fulfillerAssetId);
}