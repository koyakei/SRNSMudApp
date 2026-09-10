namespace SRNSMudApp.Services.Commands;

/// <summary>
///     コマンドハンドラー共通の入力検証を提供する Template Method 基底クラス。
/// </summary>
/// <typeparam name="TCommand">実行対象のコマンド型。</typeparam>
/// <typeparam name="TResult">実行結果の型。</typeparam>
public abstract class CommandHandlerBase<TCommand, TResult> : ICommandHandler<TCommand, TResult>
    where TCommand : class
{
    /// <inheritdoc />
    public async Task<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return await ExecuteAsync(command, cancellationToken);
    }

    /// <summary>
    ///     コマンド固有の処理を実行する。
    /// </summary>
    /// <param name="command">検証済みのコマンド。</param>
    /// <param name="cancellationToken">キャンセル通知。</param>
    /// <returns>実行結果。</returns>
    protected abstract Task<TResult> ExecuteAsync(TCommand command, CancellationToken cancellationToken);
}