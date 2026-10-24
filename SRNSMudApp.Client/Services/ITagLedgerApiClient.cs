namespace SRNSMudApp.Client.Services;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using SRNSMudApp.Client.Models.Api;

/// <summary>
/// タグの重み変動履歴（台帳データ）を取得する API クライアントインターフェース。
/// </summary>
public interface ITagLedgerApiClient
{
    /// <summary>
    /// 指定されたタグの重み変動履歴台帳を取得します。
    /// </summary>
    Task<IReadOnlyList<TagWeightLedgerDto>> GetTagWeightLedgersAsync(int tagId, CancellationToken cancellationToken = default);
}