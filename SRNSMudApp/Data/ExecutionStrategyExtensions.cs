using System;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace SRNSMudApp.Data;

/// <summary>
///     <see cref="DatabaseFacade" /> の拡張メソッド。
///     SQL Server 再実行戦略 (<see cref="SqlServerRetryingExecutionStrategy" />) 対応の
///     トランザクション実行ヘルパーを提供する。
///     手動 <c>BeginTransactionAsync</c> は再実行戦略と互換性がないため、
///     <see cref="IExecutionStrategy" /> を使用して再実行可能な単位として処理する。
/// </summary>
public static class ExecutionStrategyExtensions
{
    /// <summary>
    ///     再実行戦略を使用して、データベース操作をアトミックに実行する。
    ///     失敗時は自動的にロールバックされ、設定に従って再試行される。
    /// </summary>
    /// <typeparam name="TResult">戻り値の型。</typeparam>
    /// <param name="database">対象の <see cref="DatabaseFacade" />。</param>
    /// <param name="operation">実行する操作。トランザクションは内部で管理されるため、明示的な Begin/Commit/Rollback は不要。</param>
    /// <returns>操作の結果。</returns>
    public static async Task<TResult> ExecuteWithStrategyAsync<TResult>(
        this DatabaseFacade database,
        Func<Task<TResult>> operation)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(operation);

        var strategy = database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(operation);
    }

    /// <summary>
    ///     再実行戦略を使用して、データベース操作をアトミックに実行する（戻り値なし）。
    /// </summary>
    /// <param name="database">対象の <see cref="DatabaseFacade" />。</param>
    /// <param name="operation">実行する操作。</param>
    public static async Task ExecuteWithStrategyAsync(
        this DatabaseFacade database,
        Func<Task> operation)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(operation);

        var strategy = database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(operation);
    }

    /// <summary>
    ///     状態を保持しながら再実行戦略で操作を実行する（高度なシナリオ用）。
    ///     状態オブジェクトは再試行時にも同じインスタンスが渡されるため、ミュータブルな状態を持たせる場合は注意が必要。
    /// </summary>
    /// <typeparam name="TState">状態の型。</typeparam>
    /// <typeparam name="TResult">戻り値の型。</typeparam>
    /// <param name="database">対象の <see cref="DatabaseFacade" />。</param>
    /// <param name="state">状態オブジェクト。</param>
    /// <param name="operation">実行する操作。</param>
    /// <returns>操作の結果。</returns>
    public static async Task<TResult> ExecuteWithStrategyAsync<TState, TResult>(
        this DatabaseFacade database,
        TState state,
        Func<TState, Task<TResult>> operation)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(operation);

        var strategy = database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(state, operation);
    }
}