using SRNSMudApp.Data;

namespace SRNSMudApp.Services.Contracts;

/// <summary>
///     登録された <see cref="IContractExecutor" /> から契約種別に応じたエグゼキューターを取得するファクトリ。
/// </summary>
/// <param name="executors">DI または手動構成されたエグゼキューターのコレクション。</param>
public class ContractExecutorFactory(IEnumerable<IContractExecutor> executors) : IContractExecutorFactory
{
    private readonly Dictionary<string, IContractExecutor> _executors =
        executors.ToDictionary(e => e.ContractType, StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public IContractExecutor? GetExecutor(string contractType) =>
        _executors.GetValueOrDefault(contractType);

    /// <summary>
    ///     デフォルトの全コントラクトエグゼキューター（Gratis, Mutual, Trigger, Bounty）を含むファクトリを生成する。
    ///     主に DI コンテナ外の単体テストなどで利用される。
    /// </summary>
    /// <param name="dbContext">データベースコンテキスト。</param>
    /// <returns>デフォルト構成の <see cref="ContractExecutorFactory" />。</returns>
    public static ContractExecutorFactory CreateDefault(ApplicationDbContext dbContext) =>
        new([
            new GratisContractExecutor(dbContext),
            new MutualContractExecutor(dbContext),
            new TriggerContractExecutor(dbContext),
            new BountyContractExecutor(dbContext)
        ]);
}

