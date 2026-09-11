namespace SRNSMudApp.Services.Contracts;

/// <summary>
///     登録された <see cref="IContractExecutor" /> から契約種別に応じたエグゼキューターを取得するファクトリ。
/// </summary>
/// <param name="executors">DI または手動構成されたエグゼキューターのコレクション。</param>
public class ContractExecutorFactory(IEnumerable<IContractExecutor> executors) : IContractExecutorFactory
{
    private readonly Dictionary<string, IContractExecutor> _executors =
        (executors ?? throw new ArgumentNullException(nameof(executors)))
        .ToDictionary(e => e.ContractType, StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public IContractExecutor? GetExecutor(string contractType) =>
        _executors.GetValueOrDefault(contractType);
}