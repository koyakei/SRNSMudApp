namespace SRNSMudApp.Services.Commands;

/// <summary>
///     特定のコマンドに対する処理を担当するハンドラーインターフェース。
/// </summary>
/// <typeparam name="TCommand">実行対象のコマンド型。</typeparam>
/// <typeparam name="TResult">実行結果の型。</typeparam>
public interface ICommandHandler<in TCommand, TResult>
{
    /// <summary>
    ///     コマンドを非同期に実行する。
    /// </summary>
    /// <param name="command">コマンドパラメータ。</param>
    /// <param name="cancellationToken">キャンセレーショントークン。</param>
    /// <returns>実行結果。</returns>
    Task<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken = default);
}

