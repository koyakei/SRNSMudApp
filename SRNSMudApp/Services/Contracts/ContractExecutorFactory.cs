using SRNSMudApp.Data;

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

    /// <summary>
    ///     デフォルトの全コントラクトエグゼキューター（Gratis, Mutual, Trigger, Bounty）を含むファクトリを生成する。
    ///     エグゼキューターはステートレスであるため、DbContext は不要。
    /// </summary>
    /// <param name="timeProvider">時刻プロバイダー（任意）。</param>
    /// <returns>デフォルト構成の <see cref="ContractExecutorFactory" />。</returns>
    public static ContractExecutorFactory CreateDefault(TimeProvider? timeProvider = null) =>
        new([
            new GratisContractExecutor(timeProvider),
            new MutualContractExecutor(timeProvider),
            new TriggerContractExecutor(timeProvider),
            new BountyContractExecutor(timeProvider)
        ]);

    /// <summary>
    ///     後方互換性のためのオーバーロード。
    /// </summary>
    public static ContractExecutorFactory CreateDefault(ApplicationDbContext dbContext, TimeProvider? timeProvider = null) =>
        CreateDefault(timeProvider);
}