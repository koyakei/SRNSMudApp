using SRNSMudApp.Services.Contracts;

namespace SRNSMudApp.Tests.TestSupport;

/// <summary>
/// 標準の契約 executor 構成をテスト用に組み立てるビルダー。
/// </summary>
public static class ContractExecutorFactoryTestBuilder
{
    public static ContractExecutorFactory Create(TimeProvider? timeProvider = null) =>
        new([
            new GratisContractExecutor(timeProvider),
            new MutualContractExecutor(timeProvider),
            new TriggerContractExecutor(timeProvider),
            new BountyContractExecutor(timeProvider),
            new MoveContractExecutor()
        ]);
}