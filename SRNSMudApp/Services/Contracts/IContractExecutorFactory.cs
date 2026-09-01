namespace SRNSMudApp.Services.Contracts;

/// <summary>
///     契約種別に応じた <see cref="IContractExecutor" /> を解決・取得するファクトリインターフェース。
/// </summary>
public interface IContractExecutorFactory
{
    /// <summary>
    ///     指定された契約種別（ContractTypes）に対応する <see cref="IContractExecutor" /> を取得する。
    /// </summary>
    /// <param name="contractType">契約種別文字列。</param>
    /// <returns>対応するエグゼキューター。存在しない場合は null。</returns>
    IContractExecutor? GetExecutor(string contractType);
}