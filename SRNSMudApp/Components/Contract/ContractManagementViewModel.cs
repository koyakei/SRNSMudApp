// Components/Contract/ContractManagementViewModel.cs
#region

using System.Diagnostics.CodeAnalysis;

using MudBlazor;

using SRNSMudApp.Components.UI;
using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;

#endregion

namespace SRNSMudApp.Components.Contract;

/// <summary>
///     コントラクト管理画面で表示するデータ。
/// </summary>
public sealed record ContractManagementData(
    IReadOnlyList<TaggingRequestEntity> IncomingContracts,
    IReadOnlyList<TaggingRequestEntity> OutgoingContracts);

/// <summary>
///     ContractManagement コンポーネントの表示・判定・データ操作を担当する ViewModel。
///     コントラクト一覧取得、承認、拒否、キャンセルなどのビジネスロジックをカプセル化する。
/// </summary>
public class ContractManagementViewModel
{
    private readonly IContractManagementDataProvider _contractData;
    private readonly ITaggingContractService _contractService;

    /// <summary>
    ///     コンストラクタ。
    /// </summary>
    public ContractManagementViewModel(
        IContractManagementDataProvider contractData,
        ITaggingContractService contractService)
    {
        _contractData = contractData ?? throw new ArgumentNullException(nameof(contractData));
        _contractService = contractService ?? throw new ArgumentNullException(nameof(contractService));
    }

    /// <summary>
    ///     ページの非同期読み込み状態。
    /// </summary>
    public AsyncPageState<ContractManagementData> PageState { get; private set; } = new Loading();

    /// <summary>
    ///     指定ユーザーのコントラクト一覧を取得し、ページ状態を更新する。
    /// </summary>
    /// <param name="userId">現在のユーザーID。</param>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "非同期読み込み失敗時は例外をキャッチして Failed 状態に遷移させるため")]
    public async Task LoadDataAsync(string userId)
    {
        try
        {
            PageState = new Loading();
            ContractManagementPageData page = await _contractData.GetContractsAsync(userId);
            PageState = new Loaded<ContractManagementData>(
                new ContractManagementData(page.IncomingContracts, page.OutgoingContracts));
        }
        catch (Exception ex)
        {
            PageState = new Failed(ex);
        }
    }

    /// <summary>
    ///     指定されたコントラクトを承認する。成功時はデータを再読み込みする。
    /// </summary>
    /// <param name="contractId">コントラクトID。</param>
    /// <param name="userId">現在のユーザーID。</param>
    /// <returns>処理結果。</returns>
    public async Task<Result<string>> AcceptContractAsync(int contractId, string userId)
    {
        var result = await _contractService.AcceptContractAsync(contractId, userId);
        if (result is Success<string>)
        {
            await LoadDataAsync(userId);
        }

        return result;
    }

    /// <summary>
    ///     指定されたコントラクトを拒否する。成功時はデータを再読み込みする。
    /// </summary>
    /// <param name="contractId">コントラクトID。</param>
    /// <param name="userId">現在のユーザーID。</param>
    /// <returns>処理結果。</returns>
    public async Task<Result<string>> RejectContractAsync(int contractId, string userId)
    {
        var result = await _contractService.RejectContractAsync(contractId, userId);
        if (result is Success<string>)
        {
            await LoadDataAsync(userId);
        }

        return result;
    }

    /// <summary>
    ///     指定されたコントラクトを取り下げる。成功時はデータを再読み込みする。
    /// </summary>
    /// <param name="contractId">コントラクトID。</param>
    /// <param name="userId">現在のユーザーID。</param>
    /// <returns>処理結果。</returns>
    public async Task<Result<string>> CancelContractAsync(int contractId, string userId)
    {
        var result = await _contractService.CancelContractAsync(contractId, userId);
        if (result is Success<string>)
        {
            await LoadDataAsync(userId);
        }

        return result;
    }

    /// <summary>
    ///     TradeStatus に対応する日本語表示文字列を取得する。
    /// </summary>
    /// <param name="status">取引ステータス。</param>
    /// <returns>ステータスの日本語表示名。</returns>
    public static string GetStatusDisplayText(TradeStatus status) => status switch
    {
        TradeStatus.Proposed => "提案中",
        TradeStatus.Executed => "承認済み",
        TradeStatus.Rejected => "拒否",
        TradeStatus.Canceled => "キャンセル",
        _ => "その他"
    };

    /// <summary>
    ///     TradeStatus に対応する MudBlazor の Color を取得する。
    /// </summary>
    /// <param name="status">取引ステータス。</param>
    /// <returns>MudBlazor Color。</returns>
    public static Color GetStatusColor(TradeStatus status) => status switch
    {
        TradeStatus.Proposed => Color.Info,
        TradeStatus.Executed => Color.Success,
        TradeStatus.Rejected => Color.Error,
        TradeStatus.Canceled => Color.Default,
        _ => Color.Default
    };

    /// <summary>
    ///     コントラクトが取り下げ（キャンセル）可能かどうかを判定する。
    /// </summary>
    /// <param name="contract">対象のコントラクト。</param>
    /// <returns>提案中（Proposed）であれば true。</returns>
    public static bool CanCancelContract(TaggingRequestEntity contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        return contract.Status == TradeStatus.Proposed;
    }
}